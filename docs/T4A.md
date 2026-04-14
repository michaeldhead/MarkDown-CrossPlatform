# Task: BL-04 Phase T4 — Singleton enforcement + IPC (named pipe)

This phase prevents a second instance of the app from opening and instead routes
file paths to the already-running instance, which opens them in new tabs.

No existing tab code changes. Two files only: `Program.cs` and `App.axaml.cs`.

---

## BEFORE WRITING ANY CODE — read this file in full:

`GhsMarkdown.Cross/src/GhsMarkdown.Cross/App.axaml.cs`

Do not guess its content. The pipe server must integrate cleanly with the existing
`OnFrameworkInitializationCompleted` structure.

---

## Overview

- **Named mutex** (`Global\GHSMarkdownEditor`) in `Program.cs` detects whether an
  instance is already running.
- **Second instance:** sends any `.md` command-line args to the primary via named
  pipe (`\\.\pipe\GHSMarkdownEditor`), then exits immediately.
- **Primary instance:** starts a pipe server on a background thread after the DI
  container is built. When a message arrives, opens each file path in a new tab
  (or activates the window if no paths were sent).
- **macOS:** stub with `// TODO: BL-11 macOS IPC (NSRunningApplication + Apple Events)`.

---

## Change 1 — `Program.cs`

Replace the entire file with:

```csharp
using Avalonia;
using System.IO.Pipes;
using System.Text;

namespace GhsMarkdown.Cross;

internal class Program
{
    internal const string MutexName = "Global\\GHSMarkdownEditor";
    internal const string PipeName  = "GHSMarkdownEditor";

    [STAThread]
    public static void Main(string[] args)
    {
        // ── Singleton enforcement (Windows) ──────────────────────────────────
        // On macOS this is handled via NSRunningApplication + Apple Events (BL-11).
        if (OperatingSystem.IsWindows())
        {
            var mutex = new System.Threading.Mutex(
                initiallyOwned: true,
                name: MutexName,
                out bool createdNew);

            if (!createdNew)
            {
                // Another instance is running — send file paths and exit.
                SendToRunningInstance(args);
                return;
            }

            // We are the primary instance. Keep mutex alive for the process lifetime.
            GC.KeepAlive(mutex);
        }

        // ── WebView2 stale folder cleanup ─────────────────────────────────────
        try
        {
            var baseFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GHSMarkdownEditor");
            foreach (var dir in Directory.GetDirectories(baseFolder, "WebView2*"))
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }
        catch { }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Sends file paths (or an empty activate signal) to the running instance
    /// via the named pipe, then returns.
    /// </summary>
    private static void SendToRunningInstance(string[] args)
    {
        try
        {
            using var client = new NamedPipeClientStream(
                ".", PipeName, PipeDirection.Out);

            // 500ms timeout — if primary is busy or not listening, just exit.
            client.Connect(timeout: 500);

            var mdFiles = args
                .Where(a => a.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                         && File.Exists(a))
                .ToList();

            // Protocol: one file path per line, terminated by an empty line.
            // An empty message (just the terminator) signals "activate window only".
            using var writer = new StreamWriter(client, Encoding.UTF8, leaveOpen: true);
            foreach (var path in mdFiles)
                writer.WriteLine(path);
            writer.WriteLine(); // terminator
            writer.Flush();
        }
        catch
        {
            // If the pipe send fails, silently do nothing.
            // The user will see a second window open — acceptable edge case.
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
```

---

## Change 2 — `App.axaml.cs`: start pipe server after DI container is built

After reading `App.axaml.cs`, find the line `Services = services.BuildServiceProvider();`
and add the pipe server startup immediately after it:

```csharp
Services = services.BuildServiceProvider();

// Start named pipe server (Windows only — macOS IPC is BL-11)
if (OperatingSystem.IsWindows())
    StartPipeServer();
```

Add this private method to `App`:

```csharp
/// <summary>
/// Listens on the named pipe for file-open requests from second instances.
/// Runs on a background thread for the lifetime of the app.
/// </summary>
private static void StartPipeServer()
{
    Task.Run(async () =>
    {
        while (true)
        {
            try
            {
                using var server = new System.IO.Pipes.NamedPipeServerStream(
                    Program.PipeName,
                    System.IO.Pipes.PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    System.IO.Pipes.PipeTransmissionMode.Byte,
                    System.IO.Pipes.PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync();

                var paths = new List<string>();
                using var reader = new StreamReader(server, System.Text.Encoding.UTF8,
                    leaveOpen: true);

                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (line.Length == 0) break; // empty line = terminator
                    if (File.Exists(line)) paths.Add(line);
                }

                // Marshal to the UI thread to open tabs
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var vm = Services.GetService(typeof(MainWindowViewModel))
                             as MainWindowViewModel;
                    if (vm is null) return;

                    // Bring main window to front
                    var lifetime = Current?.ApplicationLifetime
                        as Avalonia.Controls.ApplicationLifetimes
                            .IClassicDesktopStyleApplicationLifetime;
                    lifetime?.MainWindow?.Activate();

                    if (paths.Count == 0)
                        return; // activate only — no files to open

                    foreach (var path in paths)
                    {
                        // If the active tab is untitled and empty, open in it.
                        // Otherwise open a new tab.
                        var activeTab = vm.ActiveTab;
                        bool activeIsEmpty = activeTab.FileService.CurrentFilePath is null
                                          && !activeTab.IsDirty;

                        if (activeIsEmpty)
                        {
                            await activeTab.FileService.OpenFile(path);
                        }
                        else
                        {
                            vm.NewTabCommand.Execute(null);
                            await vm.ActiveTab.FileService.OpenFile(path);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PipeServer] {ex.Message}");
                // Brief pause before accepting the next connection
                await Task.Delay(500);
            }
        }
    });
}
```

**Required using directives** — add to `App.axaml.cs` if not already present:
```csharp
using System.IO.Pipes;
using System.Text;
```

---

## What NOT to change

- The existing command-line argument handler at the bottom of
  `OnFrameworkInitializationCompleted` — it handles the case where the primary
  instance itself was launched with a `.md` arg. Leave it exactly as-is.
- All tab code (T1–T3) — no changes.
- `MainWindowViewModel` — no changes.

---

## Acceptance criteria

- [ ] Launching the app once opens normally
- [ ] Launching the app a second time (no args) activates the first window and
      exits without opening a second window
- [ ] Launching the app a second time with a `.md` file arg opens that file in a
      new tab in the running instance and exits
- [ ] Launching the app a second time with multiple `.md` file args opens each in
      its own tab
- [ ] If the active tab is empty/untitled, the file opens in it (no unnecessary
      extra tab)
- [ ] If the pipe send fails (primary not ready), the second instance opens
      normally rather than silently vanishing
- [ ] App builds without warnings on Windows

---

## Reporting

When complete, summarize:
1. Exact location in `App.axaml.cs` where `StartPipeServer()` was called
2. Any using directives added
3. Whether `maxNumberOfServerInstances` caused any issues (some platforms limit
   this — if so, note the adaptation)
4. Any uncertainties around the mutex lifetime or pipe server loop
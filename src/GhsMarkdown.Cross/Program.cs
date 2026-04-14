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

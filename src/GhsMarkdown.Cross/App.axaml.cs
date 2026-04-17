using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using GhsMarkdown.Cross.Services;
using GhsMarkdown.Cross.ViewModels;
using GhsMarkdown.Cross.Views;
using Microsoft.Extensions.DependencyInjection;

namespace GhsMarkdown.Cross;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    private bool _closingConfirmed;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();

        // Pre-construct per-tab services for the initial tab so that all
        // dependent singletons (TopologyVM, OutlineVM, etc.) receive the
        // same instances that TabViewModel will wrap.
        var settingsService = new SettingsService();
        var themeService    = new ThemeService(settingsService);
        var parsingService  = new MarkdownParsingService();
        var editorVm        = new EditorViewModel(parsingService);
        var mappingService  = new SourceMappingService(parsingService);
        var previewVm       = new PreviewViewModel(parsingService, themeService);
        var fileServiceInst = new FileService(editorVm, settingsService);

        services.AddSingleton(settingsService);
        services.AddSingleton(themeService);
        services.AddSingleton<CommandRegistry>();
        // Per-tab services registered as instances (for the initial tab);
        // new tabs construct fresh instances outside DI.
        services.AddSingleton(parsingService);
        services.AddSingleton(editorVm);
        services.AddSingleton(mappingService);
        services.AddSingleton(previewVm);
        services.AddSingleton(fileServiceInst);
        services.AddSingleton<TopologyViewModel>();
        services.AddSingleton<OutlineViewModel>();
        services.AddSingleton<SnippetService>();
        services.AddSingleton<SnippetModeController>();
        services.AddSingleton<SnippetInsertionService>();
        services.AddSingleton<SnippetsPlaceholderViewModel>();
        services.AddSingleton<AiAssistService>();
        services.AddSingleton<AiAssistViewModel>();
        services.AddSingleton<SnapshotService>();
        services.AddSingleton<TimelineViewModel>();
        services.AddSingleton<SnippetStudioViewModel>();
        services.AddSingleton<ExportService>();
        services.AddSingleton<ExportPanelViewModel>();
        services.AddSingleton<FileBrowserViewModel>();
        services.AddSingleton<CommandPaletteViewModel>();
        services.AddSingleton<JumpListService>();
        services.AddSingleton<MainWindowViewModel>();
        Services = services.BuildServiceProvider();

        // Start named pipe server (Windows only — macOS IPC is BL-11)
        if (OperatingSystem.IsWindows())
            StartPipeServer();

        // Apply the persisted theme (ThemeService reads settings in its constructor)
        themeService.SetTheme(themeService.CurrentTheme);

        // Load snippets
        var snippetService = Services.GetRequiredService<SnippetService>();
        _ = snippetService.LoadAsync();

        // Prune stale snapshots on startup (fire-and-forget)
        var snapshotService = Services.GetRequiredService<SnapshotService>();
        _ = snapshotService.PruneAll();

        var vm = Services.GetRequiredService<MainWindowViewModel>();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = vm
            };
            desktop.MainWindow.Closing += async (sender, e) =>
            {
                if (_closingConfirmed)
                {
                    vm.ForceSaveSettings();
                    return;
                }

                if (vm.HasUnsavedTabs)
                {
                    e.Cancel = true;

                    var dirty = vm.GetDirtyTabs();
                    foreach (var tab in dirty)
                    {
                        vm.SwitchTabCommand.Execute(tab);

                        var label = tab.FileService.CurrentFilePath is null
                            ? "Untitled"
                            : Path.GetFileName(tab.FileService.CurrentFilePath);

                        var result = await ShowCloseUnsavedDialog(label, desktop.MainWindow!);

                        if (result == UnsavedAction.Cancel)
                            return;

                        if (result == UnsavedAction.Save)
                            await tab.FileService.SaveFile();
                    }

                    _closingConfirmed = true;
                    vm.ForceSaveSettings();
                    desktop.MainWindow!.Close();
                }
                else
                {
                    vm.ForceSaveSettings();
                }
            };
        }

        // Windows Jump List — populate on startup + subscribe to changes
        var jumpListService = Services.GetRequiredService<JumpListService>();
        var fileService = vm.ActiveTab.FileService;
        jumpListService.UpdateJumpList(fileService.GetRecentFiles());
        fileService.RecentFilesChanged += (_, _) =>
            jumpListService.UpdateJumpList(fileService.GetRecentFiles());

        // Restore tabs from previous session (T5)
        var settingsForRestore = settingsService.Load();
        if (settingsForRestore.OpenTabPaths.Count > 0)
        {
            Dispatcher.UIThread.Post(async () =>
            {
                await vm.RestoreTabsAsync(
                    settingsForRestore.OpenTabPaths,
                    settingsForRestore.ActiveTabIndex);
            }, DispatcherPriority.Loaded);
        }

        // Open .md file passed as command-line argument
        var args = Environment.GetCommandLineArgs();
        var mdArg = args.Skip(1).FirstOrDefault(a =>
            a.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && File.Exists(a));
        if (mdArg is not null)
        {
            // Background priority runs after Loaded (where restore runs),
            // so the CLI arg always opens after session restore completes.
            // BL-34: route through OpenFileInTab so that a CLI path matching
            // an already-restored tab just activates that tab.
            Dispatcher.UIThread.Post(async () =>
            {
                var mainVm = Services.GetRequiredService<MainWindowViewModel>();
                await mainVm.OpenFileInTab(mdArg);
            }, DispatcherPriority.Background);
        }

        base.OnFrameworkInitializationCompleted();
    }

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
                        var vm = Services.GetService(typeof(ViewModels.MainWindowViewModel))
                                 as ViewModels.MainWindowViewModel;
                        if (vm is null) return;

                        // BL-36: bring main window to the foreground. On Windows,
                        // Activate() alone is unreliable when a process other than
                        // the foreground one calls it — the OS flashes the taskbar
                        // icon instead of raising the window. The idiomatic
                        // workaround is a brief Topmost toggle around Activate(),
                        // which satisfies Windows' SetForegroundWindow rules
                        // without leaving the window permanently above other apps.
                        var lifetime = Current?.ApplicationLifetime
                            as Avalonia.Controls.ApplicationLifetimes
                                .IClassicDesktopStyleApplicationLifetime;
                        var mainWindow = lifetime?.MainWindow;
                        if (mainWindow is not null)
                        {
                            if (mainWindow.WindowState == WindowState.Minimized)
                                mainWindow.WindowState = WindowState.Normal;
                            var wasTopmost = mainWindow.Topmost;
                            mainWindow.Topmost = true;
                            mainWindow.Activate();
                            mainWindow.Topmost = wasTopmost;
                        }

                        if (paths.Count == 0)
                            return; // activate only — no files to open

                        // BL-34: OpenFileInTab dedupes — if the path is already
                        // open in another tab, switch to it instead of creating
                        // a duplicate. Otherwise load into the current empty tab
                        // or spawn a new tab.
                        foreach (var path in paths)
                        {
                            await vm.OpenFileInTab(path);
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

    private static async Task<UnsavedAction> ShowCloseUnsavedDialog(
        string fileName, Window owner)
    {
        var action = UnsavedAction.Cancel;
        var dlg = new Window
        {
            Title = "Unsaved Changes",
            Width = 420, Height = 180,
            CanResize = false,
            WindowDecorations = WindowDecorations.None,
            Background = new Avalonia.Media.SolidColorBrush(
                Avalonia.Media.Color.Parse("#181818")),
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var saveBtn = new Button
        {
            Content = "Save",
            Background = new Avalonia.Media.SolidColorBrush(
                Avalonia.Media.Color.Parse("#4A9EFF")),
            Foreground = Avalonia.Media.Brushes.White,
            Width = 100,
            Margin = new Avalonia.Thickness(0, 0, 8, 0),
            Cursor = new Avalonia.Input.Cursor(
                Avalonia.Input.StandardCursorType.Hand)
        };
        var discardBtn = new Button
        {
            Content = "Don't Save",
            Background = Avalonia.Media.Brushes.Transparent,
            Foreground = new Avalonia.Media.SolidColorBrush(
                Avalonia.Media.Color.Parse("#C75050")),
            Width = 100,
            Margin = new Avalonia.Thickness(0, 0, 8, 0),
            Cursor = new Avalonia.Input.Cursor(
                Avalonia.Input.StandardCursorType.Hand)
        };
        var cancelBtn = new Button
        {
            Content = "Cancel",
            Background = Avalonia.Media.Brushes.Transparent,
            Foreground = new Avalonia.Media.SolidColorBrush(
                Avalonia.Media.Color.Parse("#888888")),
            Width = 100,
            Cursor = new Avalonia.Input.Cursor(
                Avalonia.Input.StandardCursorType.Hand)
        };

        saveBtn.Click    += (_, _) => { action = UnsavedAction.Save;    dlg.Close(); };
        discardBtn.Click += (_, _) => { action = UnsavedAction.Discard; dlg.Close(); };
        cancelBtn.Click  += (_, _) => { action = UnsavedAction.Cancel;  dlg.Close(); };

        dlg.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Children =
            {
                new TextBlock
                {
                    Text = $"\"{fileName}\" has unsaved changes.\nSave before closing?",
                    Foreground = new Avalonia.Media.SolidColorBrush(
                        Avalonia.Media.Color.Parse("#E8E8E8")),
                    FontSize = 13,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Margin = new Avalonia.Thickness(0, 0, 0, 16)
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Children = { cancelBtn, discardBtn, saveBtn }
                }
            }
        };

        await dlg.ShowDialog(owner);
        return action;
    }
}

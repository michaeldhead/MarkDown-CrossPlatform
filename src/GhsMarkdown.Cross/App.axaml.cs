using Avalonia;
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

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<CommandRegistry>();
        services.AddSingleton<MarkdownParsingService>();
        services.AddSingleton<SourceMappingService>();
        services.AddSingleton<EditorViewModel>();
        services.AddSingleton<PreviewViewModel>();
        services.AddSingleton<FileService>();
        services.AddSingleton<TopologyViewModel>();
        services.AddSingleton<OutlineViewModel>();
        services.AddSingleton<SnippetService>();
        services.AddSingleton<SnippetModeController>();
        services.AddSingleton<SnippetInsertionService>();
        services.AddSingleton<SnippetsPlaceholderViewModel>();
        services.AddSingleton<AiAssistPlaceholderViewModel>();
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

        // Apply the persisted theme (ThemeService reads settings in its constructor)
        var themeService = Services.GetRequiredService<ThemeService>();
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
        }

        // Windows Jump List — populate on startup + subscribe to changes
        var jumpListService = Services.GetRequiredService<JumpListService>();
        var fileService = Services.GetRequiredService<FileService>();
        jumpListService.UpdateJumpList(fileService.GetRecentFiles());
        fileService.RecentFilesChanged += (_, _) =>
            jumpListService.UpdateJumpList(fileService.GetRecentFiles());

        // Open .md file passed as command-line argument
        var args = Environment.GetCommandLineArgs();
        var mdArg = args.Skip(1).FirstOrDefault(a =>
            a.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && File.Exists(a));
        if (mdArg is not null)
        {
            Dispatcher.UIThread.Post(async () =>
            {
                var fs = Services.GetRequiredService<FileService>();
                await fs.OpenFile(mdArg);
            }, DispatcherPriority.Loaded);
        }

        base.OnFrameworkInitializationCompleted();
    }
}

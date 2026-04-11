using Avalonia;

namespace GhsMarkdown.Cross;

internal class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Clean up stale WebView2 folders from previous sessions
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

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}

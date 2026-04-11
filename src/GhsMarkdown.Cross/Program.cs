using Avalonia;

namespace GhsMarkdown.Cross;

internal class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Main preview WebView gets its own subfolder
        var baseFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GHSMarkdownEditor");
        var mainWebViewFolder = Path.Combine(baseFolder, "WebView2Main");
        try { Directory.CreateDirectory(mainWebViewFolder); } catch { }
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", mainWebViewFolder);

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}

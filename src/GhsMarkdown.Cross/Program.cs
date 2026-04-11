using Avalonia;

namespace GhsMarkdown.Cross;

internal class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // WebView2 user data folder — must be writable; Program Files is not.
        var webViewDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GHSMarkdownEditor",
            "WebView2");
        Directory.CreateDirectory(webViewDataFolder);
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", webViewDataFolder);

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}

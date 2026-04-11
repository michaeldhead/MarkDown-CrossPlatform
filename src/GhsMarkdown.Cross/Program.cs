using Avalonia;

namespace GhsMarkdown.Cross;

internal class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Use unique WebView2 data folder based on exe location hash
        // so installed and dev builds never conflict
        var baseDir = AppContext.BaseDirectory;
        var dirHash = Math.Abs(baseDir.GetHashCode()).ToString("X8");
        var webViewDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GHSMarkdownEditor",
            $"WebView2_{dirHash}");
        try { Directory.CreateDirectory(webViewDataFolder); } catch { }
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", webViewDataFolder);

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using GhsMarkdown.Cross.Services;
using GhsMarkdown.Cross.ViewModels;

namespace GhsMarkdown.Cross.Views;

public class DetachedPreviewWindow : Window
{
    private NativeWebView?   _webView;
    private PreviewViewModel _previewVm;
    private bool             _webViewReady;
    #pragma warning disable CS0649 // reserved for future bidirectional scroll sync
    private bool             _isSyncingScroll;
    #pragma warning restore CS0649

    public event EventHandler? DetachedWindowClosed;

    public DetachedPreviewWindow(
        PreviewViewModel previewVm,
        SettingsService settingsService,
        double x, double y, double width, double height)
    {
        _previewVm = previewVm;

        Title          = "GHS Markdown Editor \u2014 Preview";
        Width          = width;
        Height         = height;
        Position       = new PixelPoint((int)x, (int)y);

        // Load icon from embedded asset
        try
        {
            var stream = Avalonia.Platform.AssetLoader.Open(
                new Uri("avares://GhsMarkdown.Cross/Assets/icon.ico"));
            Icon = new WindowIcon(stream);
        }
        catch { /* non-fatal — window works without icon */ }

        // Build layout
        _webView = new NativeWebView
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment   = Avalonia.Layout.VerticalAlignment.Stretch
        };

        Content = _webView;

        // Wire WebView2 user data folder
        _webView.EnvironmentRequested += (_, args) =>
        {
            var prop = args.GetType().GetProperty("UserDataFolder");
            prop?.SetValue(args, System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GHSMarkdownEditor", "WebView2Detached"));
        };

        // Wire WebView events
        _webView.AdapterCreated += (_, _) =>
        {
            _webViewReady = true;
            NavigateTo(_previewVm.PreviewHtml);
        };

        _webView.NavigationCompleted += (_, _) =>
        {
            InjectScrollListener();
        };

        _webView.WebMessageReceived += OnWebMessageReceived;

        // Subscribe to preview HTML changes
        _previewVm.PropertyChanged += OnPreviewPropertyChanged;

        // Save position/size on close
        Closing += (_, _) =>
        {
            var s = settingsService.Load();
            settingsService.Save(s with
            {
                DetachedPreviewX      = Position.X,
                DetachedPreviewY      = Position.Y,
                DetachedPreviewWidth  = Width,
                DetachedPreviewHeight = Height
            });

            // Unsubscribe to prevent memory leaks
            _previewVm.PropertyChanged -= OnPreviewPropertyChanged;
            DetachedWindowClosed?.Invoke(this, EventArgs.Empty);
        };
    }

    private void OnPreviewPropertyChanged(object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PreviewViewModel.PreviewHtml))
            Dispatcher.UIThread.Post(() =>
                NavigateTo(_previewVm.PreviewHtml));
    }

    private void NavigateTo(string html)
    {
        if (!_webViewReady || _webView is null) return;
        _webView.NavigateToString(html);
    }

    private void InjectScrollListener()
    {
        if (!_webViewReady || _webView is null) return;

        const string js = """
            (function() {
                window._ghsScrolling = false;
                window.addEventListener('scroll', function() {
                    if (window._ghsScrolling) return;
                    try {
                        window.chrome.webview.postMessage(JSON.stringify({
                            type: 'detached-scroll',
                            y: window.scrollY,
                            h: document.body.scrollHeight - window.innerHeight
                        }));
                    } catch(e) {}
                });
            })();
            """;

        _ = _webView.InvokeScript(js);
    }

    private void OnWebMessageReceived(object? sender,
        Avalonia.Controls.WebMessageReceivedEventArgs e)
    {
        // Scroll sync from detached preview → host handled
        // via ScrollToFraction delegate (wired from MainWindow)
    }

    /// <summary>
    /// Scroll the detached preview to a fractional position.
    /// Called by MainWindow when the editor scrolls.
    /// </summary>
    public void ScrollToFraction(double fraction)
    {
        if (!_webViewReady || _webView is null || _isSyncingScroll) return;

        var fractionStr = fraction.ToString("F6",
            System.Globalization.CultureInfo.InvariantCulture);
        var js = $"window._ghsScrolling=true; " +
                 $"window.scrollTo(0, (document.body.scrollHeight - " +
                 $"window.innerHeight) * {fractionStr}); " +
                 $"setTimeout(function(){{window._ghsScrolling=false;}}, 200);";

        _ = _webView.InvokeScript(js);
    }
}

using Avalonia.Controls;
using Avalonia.Input;
using GhsMarkdown.Cross.Models;
using GhsMarkdown.Cross.ViewModels;

namespace GhsMarkdown.Cross.Views;

public partial class ExportPanelView : UserControl
{
    private bool _webViewReady;
    private bool _webViewCreated;
    private NativeWebView? _exportWebView;
    private Grid? _webViewHost;
    private ExportPanelViewModel? _vm;

    public ExportPanelView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        // Escape to close
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape && DataContext is ExportPanelViewModel vm)
            {
                vm.CloseCommand.Execute(null);
                e.Handled = true;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not ExportPanelViewModel vm) return;
        _vm = vm;
        _webViewHost = this.FindControl<Grid>("ExportWebViewHost");

        // Create WebView lazily when panel opens
        vm.PropertyChanged += (_, pe) =>
        {
            if (pe.PropertyName == nameof(ExportPanelViewModel.IsOpen) && vm.IsOpen)
                EnsureWebViewCreated();

            if (pe.PropertyName == nameof(ExportPanelViewModel.PreviewHtml) && _webViewReady && _exportWebView is not null)
                _exportWebView.NavigateToString(vm.PreviewHtml);
        };

        // Wire up PDF export via WebView's PrintToPdfStreamAsync
        vm.ExportWithWebViewFunc = async (format, markdownContent, themedCss, filePath) =>
        {
            EnsureWebViewCreated();

            if (_exportWebView is null || !_webViewReady)
                return new ExportResult { Success = false, ErrorMessage = "WebView not ready for PDF export." };

            try
            {
                var exportService = vm.GetExportService();
                var html = exportService.BuildStyledHtml(markdownContent, themedCss);
                _exportWebView.NavigateToString(html);

                var tcs = new TaskCompletionSource<bool>();
                EventHandler<Avalonia.Controls.WebViewNavigationCompletedEventArgs>? handler = null;
                handler = (s, args) =>
                {
                    tcs.TrySetResult(true);
                    _exportWebView.NavigationCompleted -= handler;
                };
                _exportWebView.NavigationCompleted += handler;
                await Task.WhenAny(tcs.Task, Task.Delay(5000));

                using var pdfStream = await _exportWebView.PrintToPdfStreamAsync();
                using var fileStream = File.Create(filePath);
                await pdfStream.CopyToAsync(fileStream);

                return new ExportResult { Success = true, FilePath = filePath };
            }
            catch (Exception ex)
            {
                return new ExportResult { Success = false, ErrorMessage = ex.Message };
            }
        };
    }

    private void EnsureWebViewCreated()
    {
        if (_webViewCreated || _webViewHost is null) return;
        _webViewCreated = true;

        _exportWebView = new NativeWebView();

        _exportWebView.EnvironmentRequested += (_, args) =>
        {
            var prop = args.GetType().GetProperty("UserDataFolder");
            prop?.SetValue(args, Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GHSMarkdownEditor", "WebView2Export"));
        };

        _webViewHost.Children.Add(_exportWebView);

        _exportWebView.AdapterCreated += (_, _) =>
        {
            _webViewReady = true;
            if (_vm is not null && !string.IsNullOrEmpty(_vm.PreviewHtml))
                _exportWebView.NavigateToString(_vm.PreviewHtml);
        };
    }
}

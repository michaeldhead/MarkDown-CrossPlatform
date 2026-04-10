using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GhsMarkdown.Cross.Models;
using GhsMarkdown.Cross.Services;

namespace GhsMarkdown.Cross.ViewModels;

public partial class ExportPanelViewModel : ObservableObject
{
    private readonly ExportService _exportService;
    private readonly MarkdownParsingService _parsingService;
    private readonly EditorViewModel _editorVm;
    private readonly ThemeService _themeService;

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private ExportFormat _selectedFormat = ExportFormat.HtmlStyled;
    [ObservableProperty] private string _previewHtml = string.Empty;
    [ObservableProperty] private bool _isExporting;

    public event EventHandler? Opened;
    public event EventHandler? Closed;

    private CancellationTokenSource? _previewCts;

    // Static format values for XAML binding
    public static ExportFormat PdfFormat => ExportFormat.PdfStyled;
    public static ExportFormat DocxFormat => ExportFormat.Docx;
    public static ExportFormat HtmlStyledFormat => ExportFormat.HtmlStyled;
    public static ExportFormat HtmlCleanFormat => ExportFormat.HtmlClean;
    public static ExportFormat PlainTextFormat => ExportFormat.PlainText;

    public ExportService GetExportService() => _exportService;

    // Delegate for save dialog — wired from code-behind
    public Func<ExportFormat, Task<string?>>? ShowSaveDialogFunc { get; set; }

    // Delegate for export execution needing WebView — wired from code-behind
    public Func<ExportFormat, string, string, string, Task<ExportResult>>? ExportWithWebViewFunc { get; set; }

    public ExportPanelViewModel(ExportService exportService, MarkdownParsingService parsingService,
        EditorViewModel editorVm, ThemeService themeService)
    {
        _exportService = exportService;
        _parsingService = parsingService;
        _editorVm = editorVm;
        _themeService = themeService;

        _parsingService.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MarkdownParsingService.ParsedDocument) && IsOpen)
                SchedulePreviewRefresh();
        };
    }

    public void Open()
    {
        SelectedFormat = ExportFormat.HtmlStyled;
        RefreshPreview();
        IsOpen = true;
        Opened?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void SelectFormat(string formatName)
    {
        if (Enum.TryParse<ExportFormat>(formatName, out var format))
        {
            SelectedFormat = format;
            RefreshPreview();
        }
    }

    [RelayCommand]
    private async Task Export()
    {
        if (ShowSaveDialogFunc is null) return;

        var filePath = await ShowSaveDialogFunc(SelectedFormat);
        if (string.IsNullOrEmpty(filePath)) return;

        IsExporting = true;
        try
        {
            ExportResult result;
            if (SelectedFormat == ExportFormat.PdfStyled && ExportWithWebViewFunc is not null)
            {
                result = await ExportWithWebViewFunc(SelectedFormat,
                    _editorVm.DocumentText, _themeService.GetThemeCss(), filePath);
            }
            else
            {
                result = await _exportService.ExportAsync(SelectedFormat,
                    _editorVm.DocumentText, _themeService.GetThemeCss(), filePath);
            }

            if (result.Success)
            {
                IsOpen = false;
                Closed?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            IsExporting = false;
        }
    }

    public void OpenWithFormat(ExportFormat format)
    {
        SelectedFormat = format;
        RefreshPreview();
        IsOpen = true;
        Opened?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshPreview()
    {
        PreviewHtml = _exportService.GeneratePreviewHtml(
            SelectedFormat, _editorVm.DocumentText, _themeService.GetThemeCss());
    }

    private void SchedulePreviewRefresh()
    {
        _previewCts?.Cancel();
        _previewCts = new CancellationTokenSource();
        var token = _previewCts.Token;

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(500, token);
                if (token.IsCancellationRequested) return;
                Dispatcher.UIThread.Post(RefreshPreview);
            }
            catch (OperationCanceledException) { }
        }, token);
    }
}

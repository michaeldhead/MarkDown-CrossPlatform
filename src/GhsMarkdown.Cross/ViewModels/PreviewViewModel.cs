using CommunityToolkit.Mvvm.ComponentModel;
using GhsMarkdown.Cross.Services;
using Markdig.Syntax;

namespace GhsMarkdown.Cross.ViewModels;

public partial class PreviewViewModel : ObservableObject
{
    private readonly MarkdownParsingService _parsingService;
    private readonly ThemeService _themeService;

    // Cached HTML fragment (body content only) — re-used on theme change
    private string _bodyHtml = string.Empty;

    /// <summary>True when the preview is rendering a timeline snapshot instead of live ParsedDocument.</summary>
    [ObservableProperty]
    private bool _isTimelinePreviewActive;

    /// <summary>
    /// The full HTML document to inject into the WebView via NavigateToString().
    /// </summary>
    [ObservableProperty]
    private string _previewHtml = string.Empty;

    public PreviewViewModel(MarkdownParsingService parsingService, ThemeService themeService)
    {
        _parsingService = parsingService;
        _themeService   = themeService;

        // Subscribe to parsed document changes
        _parsingService.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MarkdownParsingService.ParsedDocument))
                OnDocumentChanged(_parsingService.ParsedDocument);
        };

        // Subscribe to theme changes — re-wrap without re-parsing
        _themeService.ThemeChanged += (_, _) => UpdatePreviewHtml();

        // Emit an initial blank preview with current CSS
        UpdatePreviewHtml();
    }

    private void OnDocumentChanged(MarkdownDocument? doc)
    {
        if (IsTimelinePreviewActive) return; // Suppress live updates during timeline preview
        _bodyHtml = doc is null ? string.Empty : _parsingService.RenderToHtml(doc);
        UpdatePreviewHtml();
    }

    /// <summary>Render snapshot markdown instead of live ParsedDocument.</summary>
    public void SetPreviewOverride(string markdownContent)
    {
        var doc = _parsingService.Parse(markdownContent);
        var html = _parsingService.RenderToHtml(doc);
        IsTimelinePreviewActive = true;
        PreviewHtml = BuildDocument(html, _themeService.GetThemeCss());
    }

    /// <summary>Return to live rendering from current ParsedDocument.</summary>
    public void ClearPreviewOverride()
    {
        IsTimelinePreviewActive = false;
        _bodyHtml = _parsingService.ParsedDocument is null
            ? string.Empty
            : _parsingService.RenderToHtml(_parsingService.ParsedDocument);
        UpdatePreviewHtml();
    }

    private void UpdatePreviewHtml()
    {
        PreviewHtml = BuildDocument(_bodyHtml, _themeService.GetThemeCss());
    }

    private static string BuildDocument(string bodyHtml, string css) =>
        "<!DOCTYPE html>\n<html>\n<head>\n<meta charset=\"utf-8\">\n<style>\n" +
        css +
        "\nbody { margin: 2rem; font-family: sans-serif; line-height: 1.7; }\n" +
        "</style>\n</head>\n<body>\n" +
        bodyHtml +
        "\n</body>\n</html>";
}

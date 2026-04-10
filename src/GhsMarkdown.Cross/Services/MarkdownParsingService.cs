using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;

namespace GhsMarkdown.Cross.Services;

public partial class MarkdownParsingService : ObservableObject
{
    private readonly MarkdownPipeline _pipeline = SourceLineRenderer.BuildPipeline();

    private CancellationTokenSource? _cts;

    // Observable — consumers bind to this
    [ObservableProperty]
    private MarkdownDocument? _parsedDocument;

    private string _rawText = string.Empty;

    /// <summary>
    /// Setting this triggers a debounced re-parse (300 ms).
    /// </summary>
    public string RawText
    {
        get => _rawText;
        set
        {
            if (_rawText == value) return;
            _rawText = value;
            ScheduleDebounce(value);
        }
    }

    /// <summary>
    /// Synchronous one-off parse — used by consumers needing an immediate result.
    /// </summary>
    public MarkdownDocument Parse(string markdown)
    {
        var doc = Markdig.Markdown.Parse(markdown, _pipeline);
        SourceLineRenderer.InjectAttributes(doc, markdown);
        return doc;
    }

    /// <summary>
    /// Renders a MarkdownDocument to an HTML fragment string.
    /// </summary>
    public string RenderToHtml(MarkdownDocument document)
    {
        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        _pipeline.Setup(renderer);
        renderer.Render(document);
        writer.Flush();
        return writer.ToString();
    }

    private void ScheduleDebounce(string text)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300, token);
                if (token.IsCancellationRequested) return;

                var doc = Parse(text);

                Dispatcher.UIThread.Post(() =>
                {
                    ParsedDocument = doc;
                });
            }
            catch (OperationCanceledException)
            {
                // Expected — a newer keystroke superseded this parse
            }
        }, token);
    }
}

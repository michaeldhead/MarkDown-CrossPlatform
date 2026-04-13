using Markdig.Extensions.Tables;
using Markdig.Syntax;

namespace GhsMarkdown.Cross.Services;

/// <summary>
/// Maps source line numbers ↔ CSS selectors for the corresponding HTML blocks.
/// Rebuilt automatically whenever MarkdownParsingService.ParsedDocument changes.
/// </summary>
public class SourceMappingService
{
    private readonly MarkdownParsingService _parsingService;
    private readonly object _lock = new();

    // Immutable snapshots swapped atomically inside _lock
    private Dictionary<int, string> _lineToSelector = new();
    private Dictionary<string, (int start, int end)> _selectorToRange = new();

    public SourceMappingService(MarkdownParsingService parsingService)
    {
        _parsingService = parsingService;
        _parsingService.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MarkdownParsingService.ParsedDocument))
            {
                var doc  = _parsingService.ParsedDocument;
                var text = _parsingService.RawText;
                Task.Run(() => Rebuild(doc, text));
            }
        };
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>Source line (1-based) → CSS attribute selector, or null if no block starts on that line.</summary>
    public string? GetElementSelector(int sourceLine)
    {
        Dictionary<int, string> dict;
        lock (_lock) { dict = _lineToSelector; }
        return dict.TryGetValue(sourceLine, out var sel) ? sel : null;
    }

    /// <summary>CSS selector → (startLine, endLine) 1-based source range, or null if not found.</summary>
    public (int start, int end)? GetSourceRange(string elementSelector)
    {
        Dictionary<string, (int, int)> dict;
        lock (_lock) { dict = _selectorToRange; }
        return dict.TryGetValue(elementSelector, out var range) ? range : null;
    }

    // ─── Rebuild (runs on background thread) ─────────────────────────────────

    public void Rebuild(MarkdownDocument? doc, string sourceText = "")
    {
        var lineToSel  = new Dictionary<int, string>();
        var selToRange = new Dictionary<string, (int, int)>();

        if (doc is not null)
        {
            var lineStarts = BuildLineStartIndex(sourceText);
            WalkBlocks(doc, lineToSel, selToRange, lineStarts);
        }

        lock (_lock)
        {
            _lineToSelector  = lineToSel;
            _selectorToRange = selToRange;
        }
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    private static void WalkBlocks(
        ContainerBlock container,
        Dictionary<int, string> lineToSel,
        Dictionary<string, (int, int)> selToRange,
        int[] lineStarts)
    {
        foreach (var block in container)
        {
            if (block is ParagraphBlock or HeadingBlock or ListItemBlock
                      or QuoteBlock    or FencedCodeBlock or CodeBlock
                      or Table)
            {
                var startLine = block.Line + 1;                                 // 1-based
                var endLine   = OffsetToLine(block.Span.End, lineStarts) + 1;   // 1-based
                var selector  = $"[data-source-line=\"{startLine}\"]";

                lineToSel.TryAdd(startLine, selector);
                selToRange.TryAdd(selector, (startLine, endLine));
            }

            if (block is ContainerBlock child)
                WalkBlocks(child, lineToSel, selToRange, lineStarts);
        }
    }

    /// <summary>Builds an array where lineStarts[i] = char offset of the first char on line i (0-based).</summary>
    private static int[] BuildLineStartIndex(string text)
    {
        var starts = new List<int>(64) { 0 };
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\n') starts.Add(i + 1);
        return starts.ToArray();
    }

    /// <summary>Binary-searches the line-start index to find the 0-based line number for a char offset.</summary>
    private static int OffsetToLine(int offset, int[] lineStarts)
    {
        int lo = 0, hi = lineStarts.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (lineStarts[mid] <= offset) lo = mid;
            else hi = mid - 1;
        }
        return lo;
    }
}

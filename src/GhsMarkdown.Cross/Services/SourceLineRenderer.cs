using Markdig;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace GhsMarkdown.Cross.Services;

/// <summary>
/// Injects data-source-line and data-source-end attributes onto all block-level elements.
/// Foundation for bidirectional scroll sync (Phase 2c onward).
///
/// Markdig's SourceSpan.Start / .End are character offsets into the source string, not line
/// numbers. Block.Line gives the 0-based start line. For end lines we build a line-start index
/// from the source text and binary-search the char offset.
/// </summary>
public static class SourceLineRenderer
{
    /// <summary>Returns the Markdig pipeline with AdvancedExtensions.</summary>
    public static MarkdownPipeline BuildPipeline()
    {
        return new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();
    }

    /// <summary>
    /// Walks the parsed document AST and injects data-source-line / data-source-end
    /// onto every block-level element. Line numbers are 1-based (matching AvaloniaEdit).
    /// </summary>
    public static void InjectAttributes(MarkdownDocument document, string sourceText)
    {
        var lineStarts = BuildLineStartIndex(sourceText);
        InjectInContainer(document, lineStarts);
    }

    // ─── Private ─────────────────────────────────────────────────────────────

    private static void InjectInContainer(ContainerBlock container, int[] lineStarts)
    {
        foreach (var block in container)
        {
            if (block is ParagraphBlock
                     or HeadingBlock
                     or ListItemBlock
                     or QuoteBlock
                     or FencedCodeBlock
                     or CodeBlock)
            {
                var startLine = block.Line + 1;                              // 1-based
                var endLine   = OffsetToLine(block.Span.End, lineStarts) + 1; // 1-based
                var attrs = block.GetAttributes();
                attrs.AddPropertyIfNotExist("data-source-line", startLine.ToString());
                attrs.AddPropertyIfNotExist("data-source-end",  endLine.ToString());
            }

            if (block is ContainerBlock child)
                InjectInContainer(child, lineStarts);
        }
    }

    /// <summary>
    /// Builds an array where lineStarts[i] = character offset of the first char on line i.
    /// </summary>
    private static int[] BuildLineStartIndex(string text)
    {
        var starts = new List<int>(capacity: 64) { 0 };
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
                starts.Add(i + 1);
        }
        return starts.ToArray();
    }

    /// <summary>
    /// Binary-searches the line-start index to find the 0-based line number for a char offset.
    /// </summary>
    private static int OffsetToLine(int offset, int[] lineStarts)
    {
        int lo = 0, hi = lineStarts.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (lineStarts[mid] <= offset)
                lo = mid;
            else
                hi = mid - 1;
        }
        return lo;
    }
}

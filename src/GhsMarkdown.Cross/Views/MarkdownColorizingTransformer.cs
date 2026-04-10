using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using System.Text.RegularExpressions;

namespace GhsMarkdown.Cross.Views;

public class MarkdownColorizingTransformer : DocumentColorizingTransformer
{
    private static readonly SolidColorBrush[] HeadingBrushes =
    {
        new(Color.Parse("#4A9EFF")), // H1
        new(Color.Parse("#5AB865")), // H2
        new(Color.Parse("#B8954A")), // H3
        new(Color.Parse("#888888")), // H4
        new(Color.Parse("#666666")), // H5
        new(Color.Parse("#555555")), // H6
    };

    private static readonly SolidColorBrush CodeBrush   = new(Color.Parse("#C792EA"));
    private static readonly SolidColorBrush ItalicBrush = new(Color.Parse("#D0D0D0"));
    private static readonly SolidColorBrush LinkBrush   = new(Color.Parse("#4A9EFF"));
    private static readonly SolidColorBrush QuoteBrush  = new(Color.Parse("#888888"));
    private static readonly SolidColorBrush HRBrush     = new(Color.Parse("#444444"));
    private static readonly SolidColorBrush BoldBrush   = new(Color.Parse("#E8E8E8"));

    protected override void ColorizeLine(DocumentLine line)
    {
        var doc  = CurrentContext.Document;
        var text = doc.GetText(line.Offset, line.Length);
        if (string.IsNullOrEmpty(text)) return;

        // ── Headings (whole line) ───────────────────────────────────
        var hm = Regex.Match(text, @"^(#{1,6})\s");
        if (hm.Success)
        {
            int level = Math.Min(hm.Groups[1].Length, 6);
            var brush = HeadingBrushes[level - 1];
            ChangeLinePart(line.Offset, line.EndOffset, el =>
            {
                el.TextRunProperties.SetForegroundBrush(brush);
                el.TextRunProperties.SetTypeface(new Typeface(
                    el.TextRunProperties.Typeface.FontFamily,
                    FontStyle.Normal, FontWeight.Bold));
            });
            return;
        }

        // ── Horizontal rule (whole line) ────────────────────────────
        if (Regex.IsMatch(text, @"^-{3,}\s*$"))
        {
            ChangeLinePart(line.Offset, line.EndOffset, el =>
                el.TextRunProperties.SetForegroundBrush(HRBrush));
            return;
        }

        // ── Blockquote (whole line) ─────────────────────────────────
        if (text.StartsWith("> ") || text == ">")
        {
            ChangeLinePart(line.Offset, line.EndOffset, el =>
                el.TextRunProperties.SetForegroundBrush(QuoteBrush));
            return;
        }

        // ── List bullet marker only ─────────────────────────────────
        var lm = Regex.Match(text, @"^(\s*[-\*\+])\s");
        if (lm.Success)
        {
            int end = line.Offset + lm.Groups[1].Length;
            ChangeLinePart(line.Offset, end, el =>
                el.TextRunProperties.SetForegroundBrush(LinkBrush));
        }

        // ── Inline: code (highest inline priority) ──────────────────
        foreach (Match m in Regex.Matches(text, @"`[^`\n]+`"))
            Colorize(line, m, el =>
                el.TextRunProperties.SetForegroundBrush(CodeBrush));

        // ── Inline: bold ────────────────────────────────────────────
        foreach (Match m in Regex.Matches(text, @"\*\*[^*\n]+\*\*|__[^_\n]+__"))
            Colorize(line, m, el =>
            {
                el.TextRunProperties.SetForegroundBrush(BoldBrush);
                el.TextRunProperties.SetTypeface(new Typeface(
                    el.TextRunProperties.Typeface.FontFamily,
                    FontStyle.Normal, FontWeight.Bold));
            });

        // ── Inline: italic ──────────────────────────────────────────
        foreach (Match m in Regex.Matches(text, @"\*[^*\n]+\*|_[^_\n]+_"))
            Colorize(line, m, el =>
            {
                el.TextRunProperties.SetForegroundBrush(ItalicBrush);
                el.TextRunProperties.SetTypeface(new Typeface(
                    el.TextRunProperties.Typeface.FontFamily,
                    FontStyle.Italic, FontWeight.Normal));
            });

        // ── Inline: links and images ────────────────────────────────
        foreach (Match m in Regex.Matches(text, @"!?\[.+?\]\(.+?\)"))
            Colorize(line, m, el =>
                el.TextRunProperties.SetForegroundBrush(LinkBrush));
    }

    private void Colorize(DocumentLine line, Match m,
        Action<VisualLineElement> action)
    {
        ChangeLinePart(line.Offset + m.Index,
                       line.Offset + m.Index + m.Length,
                       action);
    }
}

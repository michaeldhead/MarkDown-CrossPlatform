using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using System.Text.RegularExpressions;

namespace GhsMarkdown.Cross.Views;

public class MarkdownColorizingTransformer : DocumentColorizingTransformer
{
    private readonly bool _isLightTheme;

    private static readonly SolidColorBrush[] HeadingBrushesDark =
    {
        new(Color.Parse("#4A9EFF")), // H1
        new(Color.Parse("#5AB865")), // H2
        new(Color.Parse("#B8954A")), // H3
        new(Color.Parse("#888888")), // H4
        new(Color.Parse("#666666")), // H5
        new(Color.Parse("#555555")), // H6
    };

    private static readonly SolidColorBrush[] HeadingBrushesLight =
    {
        new(Color.Parse("#1A6BC4")), // H1
        new(Color.Parse("#2E7D32")), // H2
        new(Color.Parse("#7B5E20")), // H3
        new(Color.Parse("#5A5A5A")), // H4
        new(Color.Parse("#777777")), // H5
        new(Color.Parse("#888888")), // H6
    };

    private static readonly SolidColorBrush CodeBrushDark    = new(Color.Parse("#C792EA"));
    private static readonly SolidColorBrush CodeBrushLight   = new(Color.Parse("#7C3AED"));
    private static readonly SolidColorBrush ItalicBrushDark  = new(Color.Parse("#D0D0D0"));
    private static readonly SolidColorBrush ItalicBrushLight = new(Color.Parse("#555566"));
    private static readonly SolidColorBrush LinkBrush        = new(Color.Parse("#4A9EFF"));
    private static readonly SolidColorBrush QuoteBrush       = new(Color.Parse("#888888"));
    private static readonly SolidColorBrush HRBrush          = new(Color.Parse("#444444"));
    private static readonly SolidColorBrush BoldBrushDark    = new(Color.Parse("#E8E8E8"));
    private static readonly SolidColorBrush BoldBrushLight   = new(Color.Parse("#1A1A1A"));

    // Per-instance brushes (may be overridden by customColors)
    private readonly IBrush[] _headingBrushes = new IBrush[6];
    private readonly IBrush _codeBrush;
    private readonly IBrush _italicBrush;
    private readonly IBrush _boldBrush;

    public MarkdownColorizingTransformer(bool isLightTheme = false,
        Dictionary<string, string>? customColors = null)
    {
        _isLightTheme = isLightTheme;

        var defaultHeadings = isLightTheme ? HeadingBrushesLight : HeadingBrushesDark;
        for (int i = 0; i < 6; i++)
        {
            _headingBrushes[i] = BrushFromHex(customColors, $"syntax-h{i + 1}")
                                 ?? defaultHeadings[i];
        }

        _codeBrush   = BrushFromHex(customColors, "syntax-code")
                       ?? (isLightTheme ? CodeBrushLight : CodeBrushDark);
        _italicBrush = BrushFromHex(customColors, "syntax-italic")
                       ?? (isLightTheme ? ItalicBrushLight : ItalicBrushDark);
        _boldBrush   = BrushFromHex(customColors, "syntax-bold")
                       ?? (isLightTheme ? BoldBrushLight : BoldBrushDark);
    }

    private static IBrush? BrushFromHex(Dictionary<string, string>? dict, string key)
    {
        if (dict is null || !dict.TryGetValue(key, out var hex)) return null;
        return Avalonia.Media.Color.TryParse(hex, out var color)
            ? new Avalonia.Media.SolidColorBrush(color)
            : null;
    }

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
            var brush = _headingBrushes[level - 1];
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
                el.TextRunProperties.SetForegroundBrush(_codeBrush));

        // ── Inline: bold ────────────────────────────────────────────
        foreach (Match m in Regex.Matches(text, @"\*\*[^*\n]+\*\*|__[^_\n]+__"))
            Colorize(line, m, el =>
            {
                el.TextRunProperties.SetForegroundBrush(_boldBrush);
                el.TextRunProperties.SetTypeface(new Typeface(
                    el.TextRunProperties.Typeface.FontFamily,
                    FontStyle.Normal, FontWeight.Bold));
            });

        // ── Inline: italic ──────────────────────────────────────────
        var italicBrush = _italicBrush;
        foreach (Match m in Regex.Matches(text, @"\*[^*\n]+\*|_[^_\n]+_"))
            Colorize(line, m, el =>
            {
                el.TextRunProperties.SetForegroundBrush(italicBrush);
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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using GhsMarkdown.Cross.Services;

namespace GhsMarkdown.Cross.Views;

public class GitDiffMargin : AbstractMargin
{
    private Dictionary<int, GitLineState> _diffState = new();

    // Colors
    private static readonly IBrush AddedBrush    = new SolidColorBrush(Color.FromRgb(0x4E, 0xA0, 0x4E)); // green
    private static readonly IBrush ModifiedBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xA0, 0x30)); // amber
    private static readonly IBrush DeletedBrush  = new SolidColorBrush(Color.FromRgb(0xCC, 0x44, 0x44)); // red

    private const double BarWidth     = 3.0;
    private const double MarginWidth  = 5.0;
    private const double DeleteSize   = 5.0;

    public void UpdateDiff(Dictionary<int, GitLineState> diffState)
    {
        _diffState = diffState;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Only show margin if there is diff data
        return _diffState.Count > 0
            ? new Size(MarginWidth, 0)
            : new Size(0, 0);
    }

    public override void Render(DrawingContext drawingContext)
    {
        if (_diffState.Count == 0 || TextView is null) return;

        var visualLines = TextView.VisualLines;
        if (visualLines.Count == 0) return;

        foreach (var visualLine in visualLines)
        {
            var lineNumber = visualLine.FirstDocumentLine.LineNumber;

            // Added or modified bar
            if (_diffState.TryGetValue(lineNumber, out var state))
            {
                var brush = state == GitLineState.Added ? AddedBrush : ModifiedBrush;
                var y     = visualLine.VisualTop - TextView.ScrollOffset.Y;
                var h     = visualLine.Height;
                drawingContext.FillRectangle(brush, new Rect(0, y, BarWidth, h));
            }

            // Deletion marker — small red triangle above the line
            // Stored as negative key: -N means "after line N", so appears before line N+1
            if (_diffState.TryGetValue(-(lineNumber - 1), out var delState)
                && delState == GitLineState.Deleted)
            {
                var y = visualLine.VisualTop - TextView.ScrollOffset.Y;
                DrawDeletionMarker(drawingContext, y);
            }
        }
    }

    private static void DrawDeletionMarker(DrawingContext ctx, double y)
    {
        // Small downward-pointing triangle at top of next line
        var geo = new StreamGeometry();
        using var sgc = geo.Open();
        double cx = BarWidth / 2;
        sgc.BeginFigure(new Point(cx - DeleteSize / 2, y), true);
        sgc.LineTo(new Point(cx + DeleteSize / 2, y));
        sgc.LineTo(new Point(cx, y + DeleteSize * 0.7));
        sgc.EndFigure(true);
        ctx.DrawGeometry(DeletedBrush, null, geo);
    }
}

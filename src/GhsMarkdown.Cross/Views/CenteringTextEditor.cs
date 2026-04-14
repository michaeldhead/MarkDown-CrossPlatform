using Avalonia;
using Avalonia.Controls.Primitives;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using System.Reflection;

namespace GhsMarkdown.Cross.Views;

/// <summary>
/// TextView subclass that centers the caret vertically when typewriter mode is active,
/// instead of AvaloniaEdit's default "just make it visible" behavior.
/// </summary>
public class CenteringTextView : TextView
{
    // Tell Avalonia to use the base TextView's theme/template for this subclass
    protected override Type StyleKeyOverride => typeof(TextView);

    public bool TypewriterMode { get; set; }
    public bool SuppressMakeVisible { get; set; }

    private bool _isCentering;

    // Reflection fallback for SetScrollOffset (private in AvaloniaEdit 12)
    private static readonly MethodInfo? _setScrollOffset =
        typeof(TextView).GetMethod("SetScrollOffset",
            BindingFlags.NonPublic | BindingFlags.Instance);

    public override void MakeVisible(Rect rectangle)
    {
        if (SuppressMakeVisible) return;

        if (!TypewriterMode || _isCentering)
        {
            base.MakeVisible(rectangle);
            return;
        }

        var scrollable = (IScrollable)this;
        var viewportHeight = scrollable.Viewport.Height;
        var maxY = scrollable.Extent.Height - viewportHeight;

        // If the document fits in the viewport, no centering needed
        if (viewportHeight <= 0 || maxY <= 0)
        {
            base.MakeVisible(rectangle);
            return;
        }

        _isCentering = true;
        try
        {
            var currentOffset = scrollable.Offset;
            var targetY = rectangle.Y - (viewportHeight / 2.0) + (rectangle.Height / 2.0);
            targetY = Math.Clamp(targetY, 0, maxY);

            var newOffset = new Vector(currentOffset.X, targetY);

            // Try reflection-based SetScrollOffset first (most reliable),
            // fall back to IScrollable.Offset setter
            if (_setScrollOffset is not null)
                _setScrollOffset.Invoke(this, [newOffset]);
            else
                scrollable.Offset = newOffset;
        }
        finally
        {
            _isCentering = false;
        }
    }
}

/// <summary>
/// TextArea subclass that uses CenteringTextView.
/// </summary>
public class CenteringTextArea : TextArea
{
    // Tell Avalonia to use the base TextArea's theme/template for this subclass
    protected override Type StyleKeyOverride => typeof(TextArea);

    public CenteringTextArea() : base(new CenteringTextView())
    {
    }

    public new CenteringTextView TextView => (CenteringTextView)base.TextView;
}

/// <summary>
/// TextEditor subclass that uses CenteringTextArea and exposes TypewriterMode.
/// </summary>
public class CenteringTextEditor : TextEditor
{
    // Tell Avalonia to use the base TextEditor's theme/template for this subclass
    protected override Type StyleKeyOverride => typeof(TextEditor);

    public CenteringTextEditor() : base(new CenteringTextArea())
    {
    }

    public new CenteringTextArea TextArea => (CenteringTextArea)base.TextArea;

    public bool TypewriterMode
    {
        get => TextArea.TextView.TypewriterMode;
        set => TextArea.TextView.TypewriterMode = value;
    }

    public bool SuppressMakeVisible
    {
        get => TextArea.TextView.SuppressMakeVisible;
        set => TextArea.TextView.SuppressMakeVisible = value;
    }
}

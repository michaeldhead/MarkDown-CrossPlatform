using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace GhsMarkdown.Cross.Views;

/// <summary>
/// Borderless overlay window for inline editing of a Markdown block from the preview pane.
/// Show with Show(ownerWindow). Subscribe to Committed for the result.
/// </summary>
public class InlineEditWindow : Window
{
    private readonly TextBox _textBox;
    private bool _committed;

    /// <summary>Fires with the new Markdown text when the user commits. Not raised on cancel.</summary>
    public event EventHandler<string>? Committed;

    public InlineEditWindow(string elementType, string initialText)
    {
        Title             = string.Empty;
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        Width             = 480;
        MinHeight         = 120;
        MaxHeight         = 400;
        CanResize         = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Topmost           = true;

        _textBox = new TextBox
        {
            Text           = initialText,
            AcceptsReturn  = true,
            AcceptsTab     = false,
            TextWrapping   = TextWrapping.Wrap,
            MinHeight      = 60,
            MaxHeight      = 260,
            Background     = new SolidColorBrush(Color.Parse("#141414")),
            Foreground     = new SolidColorBrush(Color.Parse("#E8E8E8")),
            BorderBrush    = new SolidColorBrush(Color.Parse("#222222")),
            BorderThickness = new Thickness(1),
            Padding        = new Thickness(8),
            FontFamily     = FontFamily.Parse("Consolas,Menlo,monospace"),
            FontSize       = 13
        };

        var label = new TextBlock
        {
            Text       = $"Editing {elementType}",
            Foreground = new SolidColorBrush(Color.Parse("#888888")),
            FontSize   = 12,
            Margin     = new Thickness(0, 0, 0, 8)
        };

        var commitBtn = new Button { Content = "Commit" };
        var cancelBtn = new Button
        {
            Content = "Cancel",
            Margin  = new Thickness(8, 0, 0, 0)
        };

        commitBtn.Click += (_, _) => Commit();
        cancelBtn.Click += (_, _) => Close();

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin      = new Thickness(0, 10, 0, 0),
            Children    = { commitBtn, cancelBtn }
        };

        var inner = new StackPanel
        {
            Margin   = new Thickness(16),
            Children = { label, _textBox, btnRow }
        };

        Content = new Border
        {
            Background      = new SolidColorBrush(Color.Parse("#181818")),
            BorderBrush     = new SolidColorBrush(Color.Parse("#333333")),
            BorderThickness = new Thickness(1),
            Child           = inner
        };

        // Intercept keyboard before TextBox processes it (tunneling)
        this.AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _textBox.Focus();
        _textBox.SelectAll();
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                e.Handled = true;
                break;

            case Key.Enter when (e.KeyModifiers & KeyModifiers.Control) != 0:
                Commit();
                e.Handled = true;
                break;

            case Key.Enter when !(_textBox.Text?.Contains('\n') ?? false):
                // Single-line content: Enter submits
                Commit();
                e.Handled = true;
                break;
        }
    }

    private void Commit()
    {
        if (_committed) return;
        _committed = true;
        Committed?.Invoke(this, _textBox.Text ?? string.Empty);
        Close();
    }

    // ─── Factory helper ───────────────────────────────────────────────────────

    private static string FriendlyElementType(string tag) => tag switch
    {
        "p"          => "paragraph",
        "h1" or "h2" or "h3" or "h4" or "h5" or "h6" => "heading",
        "li"         => "list item",
        "blockquote" => "blockquote",
        _            => tag
    };

    /// <summary>
    /// Creates and shows the overlay. Returns false if the tag is not a supported inline-edit target.
    /// The <paramref name="onCommit"/> callback is invoked with the new Markdown text if the user commits.
    /// </summary>
    public static bool TryOpen(Window owner, string tag, string markdown, Action<string> onCommit)
    {
        if (tag is not ("p" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "li" or "blockquote"))
            return false;

        var win = new InlineEditWindow(FriendlyElementType(tag), markdown);
        win.Committed += (_, text) => onCommit(text);
        win.Show(owner);
        return true;
    }
}

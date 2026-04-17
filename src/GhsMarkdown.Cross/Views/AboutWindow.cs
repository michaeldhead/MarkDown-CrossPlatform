using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace GhsMarkdown.Cross.Views;

/// <summary>
/// Borderless About dialog. Programmatic (no AXAML) — same pattern as InlineEditWindow.
/// Shows app title, version (read from assembly), description, credit line, CLOSE button.
/// </summary>
public class AboutWindow : Window
{
    private static readonly Color AccentBlue = Color.Parse("#4A9EFF");
    private static readonly Color PanelBg    = Color.Parse("#1E1E1E");
    private static readonly Color CardBorder = Color.Parse("#3E3E42");
    private static readonly Color TextPrimary = Color.Parse("#E8E8E8");
    private static readonly Color TextMuted   = Color.Parse("#A0A0A0");

    public AboutWindow()
    {
        Title             = string.Empty;
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        Background        = new SolidColorBrush(PanelBg);
        CanResize         = false;
        ShowInTaskbar     = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width             = 340;
        SizeToContent     = SizeToContent.Height;

        // Read version from assembly — never hardcoded.
        var version = System.Reflection.Assembly
            .GetExecutingAssembly()
            .GetName()
            .Version;
        var versionString = version is null
            ? "v· Cross-Platform"
            : $"v{version.Major}.{version.Minor}.{version.Build} · Cross-Platform";

        var title = new TextBlock
        {
            Text                = "GHS Markdown Editor",
            FontSize            = 18,
            FontWeight          = FontWeight.SemiBold,
            Foreground          = new SolidColorBrush(AccentBlue),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin              = new Thickness(0, 0, 0, 6)
        };

        var versionText = new TextBlock
        {
            Text                = versionString,
            FontSize            = 12,
            Foreground          = new SolidColorBrush(TextMuted),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin              = new Thickness(0, 0, 0, 16)
        };

        var description = new TextBlock
        {
            Text         = "A modern Markdown editor for Windows and macOS, built with Avalonia UI, AvaloniaEdit, and WebView2.",
            FontSize     = 12,
            Foreground   = new SolidColorBrush(TextPrimary),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin       = new Thickness(0, 0, 0, 18)
        };

        // Credit line — "A Head & CC Production" in accent blue, remainder muted.
        var credit = new TextBlock
        {
            FontSize            = 11,
            TextAlignment       = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin              = new Thickness(0, 0, 0, 18)
        };
        credit.Inlines!.Add(new Run("A Head & CC Production")
        {
            Foreground = new SolidColorBrush(AccentBlue),
            FontWeight = FontWeight.Medium
        });
        credit.Inlines!.Add(new Run(" · Mike & The Machine · 2026")
        {
            Foreground = new SolidColorBrush(TextMuted)
        });

        var closeBtn = new Button
        {
            Content             = "CLOSE",
            Background          = Avalonia.Media.Brushes.Transparent,
            Foreground          = new SolidColorBrush(AccentBlue),
            BorderThickness     = new Thickness(0),
            FontSize            = 12,
            FontWeight          = FontWeight.Medium,
            Cursor              = new Cursor(StandardCursorType.Hand),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        closeBtn.Click += (_, _) => Close();

        var inner = new StackPanel
        {
            Margin   = new Thickness(24, 22, 24, 16),
            Children = { title, versionText, description, credit, closeBtn }
        };

        Content = new Border
        {
            Background      = new SolidColorBrush(PanelBg),
            BorderBrush     = new SolidColorBrush(CardBorder),
            BorderThickness = new Thickness(1),
            Child           = inner
        };

        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
    }
}

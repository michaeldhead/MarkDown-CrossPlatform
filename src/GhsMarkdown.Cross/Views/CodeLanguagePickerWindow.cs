using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace GhsMarkdown.Cross.Views;

public class CodeLanguagePickerWindow : Window
{
    public event EventHandler<string>? LanguageSelected;

    private static readonly string[] AllLanguages =
    {
        "plaintext", "csharp", "javascript", "typescript", "python",
        "html", "css", "sql", "bash", "json", "xml", "yaml",
        "markdown", "powershell", "rust", "go", "java", "cpp",
        "c", "ruby", "php", "swift", "kotlin", "r", "scala",
        "dockerfile", "toml", "ini", "graphql"
    };

    private readonly WrapPanel _pillsPanel;
    private readonly TextBox   _searchBox;

    public CodeLanguagePickerWindow(string title = "Insert Code Block")
    {
        Title             = string.Empty;
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        Width           = 440;
        CanResize       = false;
        SizeToContent   = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Topmost           = true;

        _searchBox = new TextBox
        {
            PlaceholderText = "Search language...",
            Margin          = new Thickness(0, 0, 0, 14),
            Background      = new SolidColorBrush(Color.Parse("#1A1A1A")),
            Foreground      = new SolidColorBrush(Color.Parse("#E8E8E8")),
            BorderBrush     = new SolidColorBrush(Color.Parse("#4A9EFF")),
            BorderThickness = new Thickness(1),
            Padding         = new Thickness(8, 6, 8, 6),
            FontSize        = 13
        };
        _searchBox.TextChanged += (_, _) => RefreshPills(_searchBox.Text ?? "");

        _pillsPanel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            ItemWidth   = double.NaN
        };

        var cancelBtn = new Button
        {
            Content    = "CANCEL",
            Background = Avalonia.Media.Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.Parse("#4A9EFF")),
            BorderThickness = new Thickness(0),
            FontSize   = 12,
            FontWeight = FontWeight.Medium,
            Cursor     = new Cursor(StandardCursorType.Hand),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin     = new Thickness(0, 14, 0, 0)
        };
        cancelBtn.Click += (_, _) => Close();

        var heading = new TextBlock
        {
            Text       = title,
            FontSize   = 16,
            FontWeight = FontWeight.Medium,
            Foreground = new SolidColorBrush(Color.Parse("#E8E8E8")),
            Margin     = new Thickness(0, 0, 0, 14)
        };

        var inner = new StackPanel
        {
            Margin   = new Thickness(20),
            Children = { heading, _searchBox, _pillsPanel, cancelBtn }
        };

        Content = new Border
        {
            Background      = new SolidColorBrush(Color.Parse("#1E1E1E")),
            BorderBrush     = new SolidColorBrush(Color.Parse("#3E3E42")),
            BorderThickness = new Thickness(1),
            Child           = inner
        };

        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        }, RoutingStrategies.Tunnel);

        RefreshPills("");
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _searchBox.Focus();
    }

    private void RefreshPills(string filter)
    {
        _pillsPanel.Children.Clear();
        var matches = string.IsNullOrWhiteSpace(filter)
            ? AllLanguages
            : AllLanguages.Where(l => l.Contains(filter.Trim(),
                StringComparison.OrdinalIgnoreCase)).ToArray();

        foreach (var lang in matches)
        {
            var pill = new Button
            {
                Content         = lang,
                Margin          = new Thickness(4),
                Padding         = new Thickness(12, 6, 12, 6),
                Background      = Avalonia.Media.Brushes.Transparent,
                Foreground      = new SolidColorBrush(Color.Parse("#E8E8E8")),
                BorderBrush     = new SolidColorBrush(Color.Parse("#4A9EFF")),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(20),
                FontSize        = 12,
                Cursor          = new Cursor(StandardCursorType.Hand)
            };
            var captured = lang;
            pill.Click += (_, _) =>
            {
                LanguageSelected?.Invoke(this, captured);
                Close();
            };
            _pillsPanel.Children.Add(pill);
        }
    }

    /// <summary>
    /// Shows the picker and calls onSelected with the chosen language.
    /// </summary>
    public static void Show(Window owner, string title, Action<string> onSelected)
    {
        var picker = new CodeLanguagePickerWindow(title);
        picker.LanguageSelected += (_, lang) => onSelected(lang);
        picker.Show(owner);
    }
}

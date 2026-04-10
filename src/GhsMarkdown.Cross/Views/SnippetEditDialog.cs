using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using GhsMarkdown.Cross.Models;

namespace GhsMarkdown.Cross.Views;

public class SnippetEditDialog : Window
{
    private readonly TextBox _titleBox;
    private readonly TextBox _categoryBox;
    private readonly TextBox _bodyBox;
    private readonly Button _saveButton;

    public Snippet? Result { get; private set; }

    public SnippetEditDialog(Snippet? existing = null)
    {
        Title = existing is null ? "Add Snippet" : "Edit Snippet";
        Width = 440;
        Height = 340;
        CanResize = false;
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x18));
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var borderBrush = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
        var textBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
        var hintBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
        var accentBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x9E, 0xFF));

        _titleBox = new TextBox
        {
            PlaceholderText = "Snippet name",
            Text = existing?.Title ?? "",
            FontSize = 13,
            Foreground = textBrush,
            Background = Brushes.Transparent,
            BorderBrush = borderBrush,
            Margin = new Thickness(0, 0, 0, 8)
        };

        _categoryBox = new TextBox
        {
            PlaceholderText = "Category (e.g. Formatting)",
            Text = existing?.Category ?? "General",
            FontSize = 13,
            Foreground = textBrush,
            Background = Brushes.Transparent,
            BorderBrush = borderBrush,
            Margin = new Thickness(0, 0, 0, 8)
        };

        _bodyBox = new TextBox
        {
            PlaceholderText = "Snippet body \u2014 use $1 $2 \u2026 for tab stops",
            Text = existing?.Body ?? "",
            FontSize = 13,
            FontFamily = new FontFamily("Cascadia Code,Consolas,Menlo,monospace"),
            Foreground = textBrush,
            Background = Brushes.Transparent,
            BorderBrush = borderBrush,
            AcceptsReturn = true,
            MinHeight = 100,
            MaxHeight = 160,
            TextWrapping = TextWrapping.Wrap
        };

        _saveButton = new Button
        {
            Content = "Save",
            FontSize = 12,
            Foreground = Brushes.White,
            Background = accentBrush,
            Padding = new Thickness(16, 6),
            HorizontalAlignment = HorizontalAlignment.Right,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        _saveButton.Click += OnSave;

        var cancelButton = new Button
        {
            Content = "Cancel",
            FontSize = 12,
            Foreground = hintBrush,
            Background = Brushes.Transparent,
            Padding = new Thickness(16, 6),
            HorizontalAlignment = HorizontalAlignment.Right,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        cancelButton.Click += (_, _) => Close();

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { cancelButton, _saveButton }
        };

        var panel = new StackPanel
        {
            Margin = new Thickness(20),
            Children =
            {
                new TextBlock { Text = Title, FontSize = 15, Foreground = textBrush, Margin = new Thickness(0, 0, 0, 12) },
                _titleBox,
                _categoryBox,
                _bodyBox,
                buttonPanel
            }
        };

        Content = new Border
        {
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            Child = panel
        };

        // Wire Ctrl+Enter → Save, Escape → Cancel
        AddHandler(KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        // Update save button enabled state
        _titleBox.TextChanged += (_, _) => UpdateSaveEnabled();
        _bodyBox.TextChanged += (_, _) => UpdateSaveEnabled();
        UpdateSaveEnabled();
    }

    private void UpdateSaveEnabled()
    {
        _saveButton.IsEnabled = !string.IsNullOrWhiteSpace(_titleBox.Text)
                             && !string.IsNullOrWhiteSpace(_bodyBox.Text);
    }

    private void OnSave(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_saveButton.IsEnabled) return;
        Result = new Snippet
        {
            Title = _titleBox.Text?.Trim() ?? "",
            Category = string.IsNullOrWhiteSpace(_categoryBox.Text) ? "General" : _categoryBox.Text.Trim(),
            Body = _bodyBox.Text ?? ""
        };
        Close();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.Control)
        {
            OnSave(this, new Avalonia.Interactivity.RoutedEventArgs());
            e.Handled = true;
        }
    }

    /// <summary>
    /// Shows the dialog and returns the resulting Snippet, or null if cancelled.
    /// </summary>
    public static async Task<Snippet?> Open(Window owner, Snippet? existing = null)
    {
        var dialog = new SnippetEditDialog(existing);

        await dialog.ShowDialog<object?>(owner);

        if (dialog.Result is not null && existing is not null)
            dialog.Result.Id = existing.Id; // preserve original id on edit

        return dialog.Result;
    }
}

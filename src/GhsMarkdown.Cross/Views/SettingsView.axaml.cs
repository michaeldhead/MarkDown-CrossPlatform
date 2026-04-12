using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GhsMarkdown.Cross.ViewModels;

namespace GhsMarkdown.Cross.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        var browseBtn = this.FindControl<Button>("BrowseSnippetPathBtn");
        if (browseBtn is not null)
            browseBtn.Click += OnBrowseSnippetPath;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        vm.CustomColorsReset += (_, _) => RefreshColorPickers();
    }

    public void RefreshColorPickers()
    {
        if (DataContext is not MainWindowViewModel vm) return;

        Dispatcher.UIThread.Post(() =>
        {
            foreach (var cp in this.GetVisualDescendants().OfType<ColorPicker>())
            {
                var key = cp.Tag as string;
                if (key is null) continue;
                if (vm.CustomColors.TryGetValue(key, out var hex) &&
                    Avalonia.Media.Color.TryParse(hex, out var color))
                {
                    cp.Color = color;
                }
            }
        }, DispatcherPriority.Loaded);
    }

    private void OnColorPickerChanged(object? sender,
        ColorChangedEventArgs e)
    {
        if (sender is not ColorPicker cp) return;
        var key = cp.Tag as string;
        if (key is null) return;

        // Convert to #RRGGBB hex string
        var color = e.NewColor;
        var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";

        if (DataContext is MainWindowViewModel vm)
            vm.SetCustomColorCommand.Execute($"{key}:{hex}");
    }

    private async void OnBrowseSnippetPath(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var window = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (window is null) return;

        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Snippet Library Folder",
            AllowMultiple = false
        });

        if (folders.Count > 0 && DataContext is MainWindowViewModel vm)
        {
            vm.SnippetLibraryPath = folders[0].Path.LocalPath;
        }
    }
}

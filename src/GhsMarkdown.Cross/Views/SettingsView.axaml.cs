using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using GhsMarkdown.Cross.ViewModels;

namespace GhsMarkdown.Cross.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        var browseBtn = this.FindControl<Button>("BrowseSnippetPathBtn");
        if (browseBtn is not null)
            browseBtn.Click += OnBrowseSnippetPath;
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

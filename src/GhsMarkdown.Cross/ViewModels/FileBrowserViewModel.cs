using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GhsMarkdown.Cross.Services;

namespace GhsMarkdown.Cross.ViewModels;

public partial class RecentFileViewModel : ObservableObject
{
    public string FileName  { get; }
    public string FullPath  { get; }
    public string Directory { get; }

    [ObservableProperty]
    private bool _isActive;

    public RecentFileViewModel(string fullPath)
    {
        FullPath  = fullPath;
        FileName  = Path.GetFileName(fullPath);
        var dir   = Path.GetDirectoryName(fullPath) ?? "";
        Directory = dir.Length > 32 ? "…" + dir[^31..] : dir;
    }
}

public partial class FileBrowserViewModel : ViewModelBase
{
    private readonly FileService _fileService;

    public ObservableCollection<RecentFileViewModel> RecentFiles { get; } = new();

    public FileBrowserViewModel(FileService fileService)
    {
        _fileService = fileService;
        RebuildRecentFiles();
        _fileService.RecentFilesChanged += (_, _) => RebuildRecentFiles();
        _fileService.PropertyChanged += (_, pe) =>
        {
            if (pe.PropertyName == nameof(FileService.CurrentFilePath))
                UpdateActiveState();
        };
    }

    [RelayCommand]
    private async Task OpenFile(RecentFileViewModel? item)
    {
        if (item is null) return;
        await _fileService.OpenFile(item.FullPath);
    }

    [RelayCommand]
    private void ClearRecent()
    {
        _fileService.ClearRecentFiles();
    }

    private void RebuildRecentFiles()
    {
        RecentFiles.Clear();
        foreach (var path in _fileService.GetRecentFiles())
            RecentFiles.Add(new RecentFileViewModel(path));
        UpdateActiveState();
    }

    private void UpdateActiveState()
    {
        var current = _fileService.CurrentFilePath;
        foreach (var rf in RecentFiles)
            rf.IsActive = string.Equals(rf.FullPath, current, StringComparison.OrdinalIgnoreCase);
    }
}

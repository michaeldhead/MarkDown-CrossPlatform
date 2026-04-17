using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using GhsMarkdown.Cross.ViewModels;

namespace GhsMarkdown.Cross.Services;

public enum UnsavedAction { Cancel, Save, Discard }

public partial class FileService : ObservableObject
{
    private readonly EditorViewModel _editor;
    private readonly SettingsService _settingsService;
    private AppSettings _settings;
    private bool _skipDraftCheck;
    private bool _suppressDirtyTracking;

    [ObservableProperty]
    private string? _currentFilePath;

    /// <summary>
    /// Flag-based dirty tracking. Previously this was computed as
    /// <c>CurrentContent != _editor.DocumentText</c>, which silently broke
    /// after save whenever line-ending normalization or any other transparent
    /// text transform made the two strings unequal even after a successful
    /// write. As an ObservableProperty, any change here fires PropertyChanged
    /// automatically — <see cref="TabViewModel"/> only needs one subscription
    /// on this service's PropertyChanged event to refresh IsDirty.
    /// </summary>
    [ObservableProperty]
    private bool _hasUnsavedChanges;

    public event EventHandler? RecentFilesChanged;
    public event EventHandler<DraftFoundEventArgs>? DraftFound;

    public FileService(EditorViewModel editor, SettingsService settingsService)
    {
        _editor = editor;
        _settingsService = settingsService;
        _settings = settingsService.Load();

        // Any user edit of the document marks it dirty. Programmatic loads
        // (OpenFile, NewFile, snapshot restore via DocumentText=) guard with
        // _suppressDirtyTracking and explicitly reset the flag afterward.
        _editor.PropertyChanged += (_, pe) =>
        {
            if (!_suppressDirtyTracking
                && pe.PropertyName == nameof(EditorViewModel.DocumentText))
            {
                HasUnsavedChanges = true;
            }
        };
    }

    public async Task NewFile()
    {
        if (HasUnsavedChanges)
        {
            var result = await ShowUnsavedChangesDialogAsync();
            if (result == UnsavedAction.Save)
                await SaveFile();
            else if (result == UnsavedAction.Cancel)
                return;
            // Discard — fall through
        }

        _suppressDirtyTracking = true;
        try { _editor.DocumentText = string.Empty; }
        finally { _suppressDirtyTracking = false; }

        CurrentFilePath = null;
        HasUnsavedChanges = false;
    }

    public async Task OpenFile(string? path = null)
    {
        if (HasUnsavedChanges)
        {
            var result = await ShowUnsavedChangesDialogAsync();
            if (result == UnsavedAction.Save)
                await SaveFile();
            else if (result == UnsavedAction.Cancel)
                return;
        }

        string? filePath = path;

        if (filePath is null)
        {
            var window = GetMainWindow();
            if (window is null) return;

            var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open Markdown File",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Markdown Files") { Patterns = new[] { "*.md", "*.markdown" } },
                    new FilePickerFileType("All Files")      { Patterns = new[] { "*" } }
                }
            });

            if (files.Count == 0) return;
            filePath = files[0].Path.LocalPath;
        }

        // Draft check (Phase 9)
        if (!_skipDraftCheck)
        {
            var draftPath = GetDraftPath(filePath);
            if (File.Exists(draftPath))
            {
                var draftTime = File.GetLastWriteTimeUtc(draftPath);
                var fileTime  = File.GetLastWriteTimeUtc(filePath);
                if (draftTime > fileTime)
                {
                    DraftFound?.Invoke(this, new DraftFoundEventArgs
                    {
                        FilePath = filePath,
                        DraftPath = draftPath,
                        DraftTimestamp = draftTime
                    });
                    return; // wait for user decision
                }
                else
                {
                    DeleteDraft(filePath);
                }
            }
        }

        var raw     = await File.ReadAllTextAsync(filePath);
        var content = raw.Replace("\r\n", "\n").Replace("\r", "\n");
        CurrentFilePath = filePath;

        _suppressDirtyTracking = true;
        try { _editor.DocumentText = content; }
        finally { _suppressDirtyTracking = false; }

        HasUnsavedChanges = false;
        AddToRecent(filePath);
    }

    public event EventHandler? FileSaved;

    public async Task SaveFile()
    {
        if (CurrentFilePath is null)
        {
            await SaveFileAs();
            return;
        }

        await File.WriteAllTextAsync(CurrentFilePath, _editor.DocumentText);
        HasUnsavedChanges = false;
        FileSaved?.Invoke(this, EventArgs.Empty);
    }

    public async Task SaveFileAs()
    {
        var window = GetMainWindow();
        if (window is null) return;

        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Markdown File",
            DefaultExtension = "md",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Markdown Files") { Patterns = new[] { "*.md", "*.markdown" } },
                new FilePickerFileType("All Files")      { Patterns = new[] { "*" } }
            },
            SuggestedFileName = CurrentFilePath is null
                ? "Untitled.md"
                : Path.GetFileName(CurrentFilePath)
        });

        if (file is null) return;

        CurrentFilePath = file.Path.LocalPath;
        await File.WriteAllTextAsync(CurrentFilePath, _editor.DocumentText);
        HasUnsavedChanges = false;
        FileSaved?.Invoke(this, EventArgs.Empty);
        AddToRecent(CurrentFilePath);
    }

    // ─── Recent files ───────────────────────────────────────────────────────

    public IEnumerable<string> GetRecentFiles()
    {
        return _settings.RecentFiles.Where(File.Exists);
    }

    public void AddToRecent(string filePath)
    {
        var list = _settings.RecentFiles.ToList();
        list.Remove(filePath);
        list.Insert(0, filePath);
        if (list.Count > 20) list.RemoveRange(20, list.Count - 20);
        _settings = _settings with { RecentFiles = list };
        _settingsService.Save(_settings);
        RecentFilesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearRecentFiles()
    {
        _settings = _settings with { RecentFiles = new List<string>() };
        _settingsService.Save(_settings);
        RecentFilesChanged?.Invoke(this, EventArgs.Empty);
    }

    // ─── Draft auto-save ─────────────────────────────────────────────────────

    public string GetDraftPath(string filePath)
        => Path.ChangeExtension(filePath, ".draft");

    public void WriteDraft(string filePath, string content)
    {
        _ = Task.Run(() =>
        {
            try { File.WriteAllText(GetDraftPath(filePath), content); }
            catch { /* swallow IO exceptions */ }
        });
    }

    public void DeleteDraft(string filePath)
    {
        _ = Task.Run(() =>
        {
            try
            {
                var dp = GetDraftPath(filePath);
                if (File.Exists(dp)) File.Delete(dp);
            }
            catch { /* swallow */ }
        });
    }

    /// <summary>Open a file bypassing the draft check (used after user decides on draft prompt).</summary>
    public async Task OpenFileSkipDraftCheck(string filePath)
    {
        _skipDraftCheck = true;
        try { await OpenFile(filePath); }
        finally { _skipDraftCheck = false; }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static Window? GetMainWindow()
    {
        return (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow;
    }

    /// <summary>Shows the Save/Discard/Cancel dialog. Called by tab close logic.</summary>
    public static Task<UnsavedAction> PromptUnsavedChangesAsync()
        => ShowUnsavedChangesDialogAsync();

    private static async Task<UnsavedAction> ShowUnsavedChangesDialogAsync()
    {
        var owner = GetMainWindow();
        if (owner is null) return UnsavedAction.Cancel;

        var action = UnsavedAction.Cancel;
        var dlg = new Window
        {
            Title = "Unsaved Changes",
            Width = 400,
            Height = 180,
            CanResize = false,
            WindowDecorations = WindowDecorations.None,
            Background = new SolidColorBrush(Color.Parse("#181818")),
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var saveBtn = new Button
        {
            Content = "Save",
            Background = new SolidColorBrush(Color.Parse("#4A9EFF")),
            Foreground = Brushes.White,
            Width = 100,
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };
        var dontSaveBtn = new Button
        {
            Content = "Don't Save",
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.Parse("#C75050")),
            Width = 100,
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };
        var cancelBtn = new Button
        {
            Content = "Cancel",
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.Parse("#888888")),
            Width = 100,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };

        saveBtn.Click += (_, _) => { action = UnsavedAction.Save; dlg.Close(); };
        dontSaveBtn.Click += (_, _) => { action = UnsavedAction.Discard; dlg.Close(); };
        cancelBtn.Click += (_, _) => { action = UnsavedAction.Cancel; dlg.Close(); };

        dlg.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Children =
            {
                new TextBlock
                {
                    Text = "Do you want to save changes to the current document?",
                    Foreground = new SolidColorBrush(Color.Parse("#E8E8E8")),
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 16)
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { cancelBtn, dontSaveBtn, saveBtn }
                }
            }
        };

        await dlg.ShowDialog(owner);
        return action;
    }
}

public class DraftFoundEventArgs : EventArgs
{
    public required string FilePath { get; init; }
    public required string DraftPath { get; init; }
    public DateTime DraftTimestamp { get; init; }
}

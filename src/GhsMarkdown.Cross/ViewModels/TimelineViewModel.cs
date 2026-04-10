using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using GhsMarkdown.Cross.Models;
using GhsMarkdown.Cross.Services;

namespace GhsMarkdown.Cross.ViewModels;

public partial class TimelineViewModel : ObservableObject
{
    private readonly SnapshotService _snapshotService;
    private readonly PreviewViewModel _previewVm;
    private readonly EditorViewModel _editorVm;

    private List<Snapshot> _snapshots = new();
    private string? _currentFilePath;

    [ObservableProperty] private bool _hasSnapshots;
    [ObservableProperty] private int _snapshotCount;
    [ObservableProperty] private double _thumbPosition = 1.0;
    [ObservableProperty] private bool _isDragging;
    [ObservableProperty] private string _thumbTooltip = "Now";

    public Snapshot? PreviewingSnapshot { get; private set; }

    // Delegate wired from code-behind for restore confirm dialog (async to avoid UI deadlock)
    public Func<string, Task<bool>>? ShowConfirmFunc { get; set; }

    public TimelineViewModel(SnapshotService snapshotService, PreviewViewModel previewVm, EditorViewModel editorVm)
    {
        _snapshotService = snapshotService;
        _previewVm = previewVm;
        _editorVm = editorVm;
    }

    public void BeginDrag()
    {
        IsDragging = true;
    }

    public void UpdateDragPosition(double position)
    {
        if (!IsDragging || _snapshots.Count == 0) return;

        ThumbPosition = Math.Clamp(position, 0.0, 1.0);

        if (ThumbPosition >= 0.999)
        {
            // "Now" — clear override
            if (PreviewingSnapshot is not null)
            {
                PreviewingSnapshot = null;
                ThumbTooltip = "Now";
                _previewVm.ClearPreviewOverride();
            }
            return;
        }

        var index = (int)Math.Round((1.0 - ThumbPosition) * (_snapshots.Count - 1));
        index = Math.Clamp(index, 0, _snapshots.Count - 1);
        var snapshot = _snapshots[index];

        if (PreviewingSnapshot?.SnapshotPath == snapshot.SnapshotPath) return;

        PreviewingSnapshot = snapshot;
        ThumbTooltip = snapshot.Timestamp.ToLocalTime().ToString("MMM d, yyyy h:mm tt");

        // Load and preview async
        Task.Run(async () =>
        {
            var content = await _snapshotService.LoadSnapshotContent(snapshot);
            Dispatcher.UIThread.Post(() => _previewVm.SetPreviewOverride(content));
        });
    }

    public async Task EndDrag()
    {
        IsDragging = false;

        if (ThumbPosition >= 0.999 || PreviewingSnapshot is null)
        {
            _previewVm.ClearPreviewOverride();
            ThumbPosition = 1.0;
            ThumbTooltip = "Now";
            PreviewingSnapshot = null;
            return;
        }

        // At a past snapshot — show confirm
        var timestamp = PreviewingSnapshot.Timestamp.ToLocalTime().ToString("MMM d, yyyy h:mm tt");
        var confirmed = ShowConfirmFunc is not null
            ? await ShowConfirmFunc($"Restore version from {timestamp}?\nEditor content will be replaced.")
            : false;

        if (confirmed)
        {
            var content = await _snapshotService.LoadSnapshotContent(PreviewingSnapshot);
            _editorVm.DocumentText = content;

            // Save a new snapshot for the restored content
            if (!string.IsNullOrEmpty(_currentFilePath))
            {
                await _snapshotService.SaveSnapshot(_currentFilePath, content);
                await ReloadSnapshots(_currentFilePath);
            }
        }

        _previewVm.ClearPreviewOverride();
        ThumbPosition = 1.0;
        ThumbTooltip = "Now";
        PreviewingSnapshot = null;
    }

    public async Task ReloadSnapshots(string? filePath)
    {
        _currentFilePath = filePath;

        if (string.IsNullOrEmpty(filePath))
        {
            _snapshots = new List<Snapshot>();
            HasSnapshots = false;
            SnapshotCount = 0;
            ThumbPosition = 1.0;
            ThumbTooltip = "No snapshots yet for this file";
            return;
        }

        var list = (await _snapshotService.GetSnapshots(filePath)).ToList();
        _snapshots = list;
        HasSnapshots = list.Count > 0;
        SnapshotCount = list.Count;
        ThumbPosition = 1.0;
        ThumbTooltip = HasSnapshots ? "Now" : "No snapshots yet for this file";
    }
}

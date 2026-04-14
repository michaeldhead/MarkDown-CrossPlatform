# Task: BL-04 Phase T3 — Per-tab isolation (Topology, Outline, Snapshot, Timeline)

T1 and T2 are complete. T3 fixes the remaining singletons that are still wired to
the initial tab only: TopologyViewModel, OutlineViewModel, and the snapshot/timeline/
window-title subscriptions in MainWindowViewModel.

---

## BEFORE WRITING ANY CODE — read these files in full

1. `GhsMarkdown.Cross/src/GhsMarkdown.Cross/ViewModels/TopologyViewModel.cs`
2. `GhsMarkdown.Cross/src/GhsMarkdown.Cross/ViewModels/OutlineViewModel.cs`

Do not guess their content. Every edit must be based on what you actually read.
`MainWindowViewModel.cs` is provided in this prompt — use it as the source of truth.

---

## Overview

Four problems to fix:

1. **TopologyViewModel** subscribes to `MarkdownParsingService.ParsedDocument` at
   construction — always shows the initial tab's topology.
2. **OutlineViewModel** same issue.
3. **Snapshot/timeline/title subscriptions** in `MainWindowViewModel` constructor
   are hardcoded to `ActiveTab.FileService` (initial tab only) — snapshot-on-save,
   timeline reload, window title update, and draft-delete-on-save never fire for
   tabs created after startup.
4. **`OnFileSaved`** (gutter green dots) is wired to initial tab's `FileSaved` only.

---

## Change 1 — Add `RewireParsingService` to TopologyViewModel

After reading `TopologyViewModel.cs`, add a public method that allows the parsing
service subscription to be swapped to a new instance:

```csharp
/// <summary>
/// Re-subscribes topology updates to a different tab's parsing service.
/// Called by MainWindowViewModel when the active tab changes.
/// </summary>
public void RewireParsingService(MarkdownParsingService newService)
{
    // Unsubscribe from the current service
    // (read the existing subscription to find the handler name/lambda)
    // Subscribe to the new service using the same handler
    // Trigger an immediate rebuild from the new service's current document
}
```

The exact implementation depends on how the subscription is written in the actual
file. If the handler is an anonymous lambda, extract it to a named private method
first so it can be unsubscribed.

If `TopologyViewModel` stores a reference to its `MarkdownParsingService`, update
that reference too.

---

## Change 2 — Add `RewireParsingService` to OutlineViewModel

Apply the same pattern as Change 1 to `OutlineViewModel`.

---

## Change 3 — `MainWindowViewModel.cs`: extract constructor subscriptions to named methods

The constructor currently has four anonymous subscriptions on `ActiveTab.FileService`
that are only wired to the initial tab. Extract each to a named method and move them
into a re-wireable pattern.

### 3a — Named methods to add

```csharp
// Window title on file path change
private void OnActiveTabFilePathChanged(object? sender, PropertyChangedEventArgs e)
{
    if (e.PropertyName == nameof(FileService.CurrentFilePath))
        UpdateWindowTitle();
}

// Timeline reload on file path change
private async void OnActiveTabFilePathChangedForTimeline(
    object? sender, PropertyChangedEventArgs e)
{
    if (e.PropertyName == nameof(FileService.CurrentFilePath))
        await Timeline.ReloadSnapshots(ActiveTab.FileService.CurrentFilePath);
}

// Snapshot + timeline reload on save
private async void OnActiveTabFileSavedForSnapshot(object? sender, EventArgs e)
{
    var path    = ActiveTab.FileService.CurrentFilePath;
    var content = ActiveTab.EditorViewModel.DocumentText;
    if (!string.IsNullOrEmpty(path))
    {
        await _snapshotService.SaveSnapshot(path, content);
        await Timeline.ReloadSnapshots(path);
    }
}

// Draft delete on save
private void OnActiveTabFileSavedDeleteDraft(object? sender, EventArgs e)
{
    var p = ActiveTab.FileService.CurrentFilePath;
    if (!string.IsNullOrEmpty(p))
        ActiveTab.FileService.DeleteDraft(p);
}
```

### 3b — Replace anonymous subscriptions in constructor

Replace all four anonymous subscriptions at the bottom of the constructor with the
named methods:

```csharp
// Before (anonymous lambdas on ActiveTab.FileService):
ActiveTab.FileService.PropertyChanged += (_, e) => { ... UpdateWindowTitle ... };
ActiveTab.FileService.FileSaved += OnFileSaved;
ActiveTab.FileService.FileSaved += async (_, _) => { ... snapshot ... };
ActiveTab.FileService.PropertyChanged += async (_, pe) => { ... timeline ... };
ActiveTab.FileService.FileSaved += (_, _) => { ... DeleteDraft ... };

// After (named methods):
ActiveTab.FileService.PropertyChanged += OnActiveTabFilePathChanged;
ActiveTab.FileService.PropertyChanged += OnActiveTabFilePathChangedForTimeline;
ActiveTab.FileService.FileSaved       += OnFileSaved;
ActiveTab.FileService.FileSaved       += OnActiveTabFileSavedForSnapshot;
ActiveTab.FileService.FileSaved       += OnActiveTabFileSavedDeleteDraft;
```

### 3c — Add `NotifyTabActivated` public method

This is called by `MainWindow.axaml.cs` after `SwitchActiveTab` completes, to
rewire all ViewModel-level subscriptions:

```csharp
/// <summary>
/// Called by code-behind after a tab switch completes. Re-wires all
/// ViewModel-level per-tab subscriptions to the newly active tab.
/// </summary>
public void NotifyTabActivated(TabViewModel outgoing, TabViewModel incoming)
{
    // Unsubscribe from outgoing tab's FileService
    outgoing.FileService.PropertyChanged -= OnActiveTabFilePathChanged;
    outgoing.FileService.PropertyChanged -= OnActiveTabFilePathChangedForTimeline;
    outgoing.FileService.FileSaved       -= OnFileSaved;
    outgoing.FileService.FileSaved       -= OnActiveTabFileSavedForSnapshot;
    outgoing.FileService.FileSaved       -= OnActiveTabFileSavedDeleteDraft;

    // Subscribe to incoming tab's FileService
    incoming.FileService.PropertyChanged += OnActiveTabFilePathChanged;
    incoming.FileService.PropertyChanged += OnActiveTabFilePathChangedForTimeline;
    incoming.FileService.FileSaved       += OnFileSaved;
    incoming.FileService.FileSaved       += OnActiveTabFileSavedForSnapshot;
    incoming.FileService.FileSaved       += OnActiveTabFileSavedDeleteDraft;

    // Rewire Topology and Outline to incoming tab's parsing service
    _topologyVm.RewireParsingService(incoming.MarkdownParsingService);
    _outlineVm.RewireParsingService(incoming.MarkdownParsingService);

    // Reload timeline for the new tab's file
    _ = Timeline.ReloadSnapshots(incoming.FileService.CurrentFilePath);

    // Update window title
    UpdateWindowTitleFromTab(incoming);
}
```

---

## Change 4 — `MainWindow.axaml.cs`: call NotifyTabActivated

In `SwitchActiveTab(TabViewModel newTab)`, after step 8 (window title update),
add a call to `NotifyTabActivated`. This requires storing the outgoing tab before
the swap — use `_previousActiveTab` which is already tracked:

At the end of `SwitchActiveTab`, after all existing steps:

```csharp
// 9. Notify ViewModel to rewire its per-tab subscriptions
// outgoing is _previousActiveTab as it was BEFORE we updated it in step 0
// newTab is the incoming tab
// Note: _previousActiveTab was already updated to newTab in step 0.
// We need the outgoing tab — capture it at the top of the method.
```

**Important:** `_previousActiveTab` is updated to `newTab` at the start of
`SwitchActiveTab` (after saving). To get the outgoing tab, capture it at the very
top of the method before updating `_previousActiveTab`:

```csharp
private void SwitchActiveTab(TabViewModel newTab)
{
    // Capture outgoing tab before _previousActiveTab is updated
    var outgoingTab = _previousActiveTab;

    // 0. Save scroll state of the tab we are leaving
    if (_previousActiveTab is { } outgoing && outgoing != newTab)
    {
        // ... existing save code ...
    }
    _previousActiveTab = newTab;

    // ... all existing steps 1-8 unchanged ...

    // 9. Notify ViewModel to rewire per-tab subscriptions
    if (outgoingTab is not null && outgoingTab != newTab)
        _mainVm?.NotifyTabActivated(outgoingTab, newTab);
}
```

---

## What NOT to change

- The snapshot and draft timers in `MainWindowViewModel` — they already read
  `ActiveTab` at fire time via the property getter, which always returns the
  current active tab. No changes needed.
- `SaveSettings()` — already uses `ActiveTab.FileService.GetRecentFiles()`.
- `FileService` commands (`NewFile`, `OpenFile`, `SaveFile`, `SaveFileAs`) —
  already delegate to `ActiveTab.FileService`.
- The `_snapshotTimer` and `_draftTimer` lambda bodies — correct as-is.

---

## Acceptance criteria

- [ ] Topology view updates to show the active tab's headings when switching tabs
- [ ] Outline view updates to show the active tab's headings when switching tabs
- [ ] Saving in tab 2 triggers the gutter green dot and snapshot for tab 2
- [ ] Saving in tab 2 updates the version timeline for tab 2
- [ ] Opening a file in tab 2 reloads the timeline for tab 2's file
- [ ] Window title updates correctly when switching tabs
- [ ] Draft is deleted on save for the correct tab
- [ ] Switching back to tab 1 shows tab 1's topology and outline
- [ ] App builds without warnings

---

## Reporting

When complete, summarize:
1. How `TopologyViewModel` and `OutlineViewModel` subscriptions were structured
   (anonymous vs named) and what changes were needed to make them re-wireable
2. Whether any additional per-tab subscriptions were found in either ViewModel
   beyond the `ParsedDocument` subscription
3. The exact location where `outgoingTab` was captured in `SwitchActiveTab`
4. Any edge cases around the initial tab (outgoing is null on first activation)
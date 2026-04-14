# Task: BL-04 Phase T1 — TabViewModel + collection (data model only, no UI changes)

This is Phase 1 of a 5-phase tab implementation. This phase establishes the data
model only. The app must work identically after this phase — no UI changes, no
regressions. Later phases add the tab strip UI, per-tab isolation, singleton/IPC,
and persistence.

---

## BEFORE WRITING ANY CODE — read these files in full

1. `GhsMarkdown.Cross/src/GhsMarkdown.Cross/ViewModels/MainWindowViewModel.cs`
2. `GhsMarkdown.Cross/src/GhsMarkdown.Cross/Views/MainWindow.axaml.cs`

Do not guess their content. Every edit must be based on what you actually read.

---

## Overview

A `TabViewModel` class is introduced. It owns all per-tab services. `MainWindowViewModel`
gets a `Tabs` collection and an `ActiveTab` property. The first (and only) tab is
constructed from the services that were previously injected as singletons.

`MainWindow.axaml.cs` is NOT changed in this phase — it continues to hold its existing
`_editorVm`, `_previewVm`, `_sourceMappingService` references and they continue to work
via the initial tab. Full code-behind re-wiring on tab switch is Phase T2.

---

## Change 1 — `App.axaml.cs`: remove per-tab services from DI

Remove these five registrations from the `ServiceCollection`:

```csharp
// Remove these — they become per-tab (owned by TabViewModel):
services.AddSingleton<EditorViewModel>();
services.AddSingleton<PreviewViewModel>();
services.AddSingleton<FileService>();
services.AddSingleton<MarkdownParsingService>();
services.AddSingleton<SourceMappingService>();
```

All other registrations remain unchanged.

Also update the command-line argument handler at the bottom of
`OnFrameworkInitializationCompleted`. It currently does:
```csharp
var fs = Services.GetRequiredService<FileService>();
await fs.OpenFile(mdArg);
```

Replace with:
```csharp
var mainVm = Services.GetRequiredService<MainWindowViewModel>();
await mainVm.ActiveTab.FileService.OpenFile(mdArg);
```

Also update the JumpList wiring that references `FileService` directly:
```csharp
// Before:
var fileService = Services.GetRequiredService<FileService>();
jumpListService.UpdateJumpList(fileService.GetRecentFiles());
fileService.RecentFilesChanged += ...

// After:
var fileService = Services.GetRequiredService<MainWindowViewModel>().ActiveTab.FileService;
jumpListService.UpdateJumpList(fileService.GetRecentFiles());
fileService.RecentFilesChanged += ...
```

---

## Change 2 — New file: `ViewModels/TabViewModel.cs`

Create this file at
`GhsMarkdown.Cross/src/GhsMarkdown.Cross/ViewModels/TabViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using GhsMarkdown.Cross.Services;

namespace GhsMarkdown.Cross.ViewModels;

/// <summary>
/// Represents a single open document tab. Owns all per-document services.
/// Shared services (ThemeService, SettingsService, etc.) are passed in
/// from the singleton container and are NOT owned by this class.
/// </summary>
public partial class TabViewModel : ObservableObject
{
    // ─── Per-tab services (owned) ─────────────────────────────────────────

    public EditorViewModel       EditorViewModel      { get; }
    public PreviewViewModel      PreviewViewModel     { get; }
    public FileService           FileService          { get; }
    public MarkdownParsingService MarkdownParsingService { get; }
    public SourceMappingService  SourceMappingService  { get; }

    // ─── Tab display state ────────────────────────────────────────────────

    /// <summary>Filename without extension, or "Untitled" if no file is open.</summary>
    public string DisplayName =>
        FileService.CurrentFilePath is null
            ? "Untitled"
            : Path.GetFileNameWithoutExtension(FileService.CurrentFilePath);

    /// <summary>True when the document has unsaved changes.</summary>
    public bool IsDirty => FileService.HasUnsavedChanges;

    [ObservableProperty]
    private bool _isActive;

    // ─── Constructor ──────────────────────────────────────────────────────

    /// <summary>
    /// Construct a new tab with freshly created per-tab service instances.
    /// </summary>
    public TabViewModel(SettingsService settingsService)
    {
        EditorViewModel       = new EditorViewModel();
        MarkdownParsingService = new MarkdownParsingService();
        SourceMappingService  = new SourceMappingService(MarkdownParsingService);
        PreviewViewModel      = new PreviewViewModel(MarkdownParsingService);
        FileService           = new FileService(EditorViewModel, settingsService);

        // Keep DisplayName and IsDirty fresh
        FileService.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(IsDirty));
        };
        EditorViewModel.PropertyChanged += (_, pe) =>
        {
            if (pe.PropertyName == nameof(EditorViewModel.DocumentText))
                OnPropertyChanged(nameof(IsDirty));
        };
    }

    /// <summary>
    /// Construct a tab that wraps pre-existing service instances (used for the
    /// initial tab, where services were previously resolved from DI as singletons).
    /// </summary>
    public TabViewModel(
        EditorViewModel       editorViewModel,
        PreviewViewModel      previewViewModel,
        FileService           fileService,
        MarkdownParsingService markdownParsingService,
        SourceMappingService  sourceMappingService)
    {
        EditorViewModel        = editorViewModel;
        PreviewViewModel       = previewViewModel;
        FileService            = fileService;
        MarkdownParsingService  = markdownParsingService;
        SourceMappingService   = sourceMappingService;

        FileService.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(IsDirty));
        };
        EditorViewModel.PropertyChanged += (_, pe) =>
        {
            if (pe.PropertyName == nameof(EditorViewModel.DocumentText))
                OnPropertyChanged(nameof(IsDirty));
        };
    }
}
```

---

## Change 3 — `SettingsService.cs`: add tab persistence fields to `AppSettings`

Add these fields to the `AppSettings` record (after the existing `RightPanelOpenWidth`
field):

```csharp
// BL-04: Tab persistence
public List<string?> OpenTabPaths  { get; init; } = new();   // null = untitled
public int           ActiveTabIndex { get; init; } = 0;
public int           MaxRestoredTabs { get; init; } = 10;
```

`string?` allows null to represent an untitled tab whose draft (if any) will be
restored separately in Phase T5.

---

## Change 4 — `MainWindowViewModel.cs`: add Tabs + ActiveTab

### 4a — Remove per-tab constructor parameters

The constructor currently takes `EditorViewModel`, `PreviewViewModel`, `FileService`,
`MarkdownParsingService` (implicit via SourceMappingService), and `SourceMappingService`
as injected parameters.

After reading the actual constructor signature, remove those five parameters. The
constructor will now construct the initial `TabViewModel` directly using the second
(wrap) constructor defined in Change 2.

**Important:** Read the constructor body carefully. All code that currently references
`_editorVm`, `_fileService`, `_previewVm`, `_sourceMappingService` must be updated to
route through `ActiveTab`. See step 4c below.

### 4b — Add fields and properties

Add these alongside the existing panel slot fields:

```csharp
// ─── Tabs ────────────────────────────────────────────────────────────────

public ObservableCollection<TabViewModel> Tabs { get; } = new();

private TabViewModel _activeTab = null!;
public TabViewModel ActiveTab
{
    get => _activeTab;
    private set
    {
        if (SetProperty(ref _activeTab, value))
        {
            // Update IsActive flags
            foreach (var t in Tabs)
                t.IsActive = t == value;

            OnPropertyChanged(nameof(ActiveTab));
        }
    }
}
```

Add `using System.Collections.ObjectModel;` if not already present.

### 4c — Update constructor to create initial tab

In the constructor, after all injected services are assigned to backing fields,
construct the initial tab:

```csharp
// Construct initial tab wrapping the per-tab services
// (previously these were injected as DI singletons)
var initialTab = new TabViewModel(
    new EditorViewModel(),
    // PreviewViewModel, FileService, MarkdownParsingService, SourceMappingService
    // — construct these fresh, using the same patterns as before
    ...
);
Tabs.Add(initialTab);
ActiveTab = initialTab;
```

**Wait — read the actual constructor body first.** The constructor has extensive
wiring code referencing the injected `_editorVm`, `_fileService`, etc. All of that
wiring must still work. The approach:

1. Construct the per-tab service instances at the top of the constructor (before any
   wiring code that references them):

```csharp
// Build per-tab services for the initial tab
var editorVm         = new EditorViewModel();
var parsingService   = new MarkdownParsingService();
var mappingService   = new SourceMappingService(parsingService);
var previewVm        = new PreviewViewModel(parsingService);
var fileServiceInst  = new FileService(editorVm, settingsService);
```

2. Replace all references to the old backing fields (`_editorVm`, `_fileService`,
   `_previewVm`, `_sourceMappingService`) with local variables above, or with
   `ActiveTab.EditorViewModel`, `ActiveTab.FileService`, etc. — whichever is cleaner
   given the actual constructor code you read.

3. After all wiring is complete, wrap them in a TabViewModel:

```csharp
var initialTab = new TabViewModel(
    editorVm, previewVm, fileServiceInst, parsingService, mappingService);
Tabs.Add(initialTab);
ActiveTab = initialTab;
```

### 4d — Update FileService property

`MainWindowViewModel` currently exposes `public FileService FileService => _fileService;`
Update this to delegate to the active tab:

```csharp
public FileService FileService => ActiveTab.FileService;
```

### 4e — Add tab commands (stubs for T2)

Add these commands. They are stubs in T1 — full implementation in T2:

```csharp
[RelayCommand]
private void NewTab()
{
    var tab = new TabViewModel(_settingsService);
    Tabs.Add(tab);
    ActiveTab = tab;
}

[RelayCommand]
private async Task CloseTab(TabViewModel tab)
{
    if (tab.IsDirty)
    {
        // Full dialog in T2 — for now just close
    }
    Tabs.Remove(tab);
    if (Tabs.Count == 0)
        NewTab(); // Always keep at least one tab
    if (ActiveTab == tab || !Tabs.Contains(ActiveTab))
        ActiveTab = Tabs.Last();
}

[RelayCommand]
private void SwitchTab(TabViewModel tab)
{
    ActiveTab = tab;
    // Full editor/preview re-wiring in T2
}
```

### 4f — Update SaveSettings()

`SaveSettings()` currently constructs `AppSettings` from scratch. Add the two new
tab persistence fields (values are stubs for T1 — full persistence in T5):

```csharp
OpenTabPaths  = Tabs.Select(t => t.FileService.CurrentFilePath).ToList(),
ActiveTabIndex = Tabs.IndexOf(ActiveTab),
```

---

## Change 5 — Verify `MainWindow.axaml.cs` requires NO changes

After reading `MainWindow.axaml.cs`, confirm that:
- `_editorVm` is still resolved via `App.Services.GetService(typeof(EditorViewModel))`
- `_fileService` is still resolved via `App.Services.GetService(typeof(FileService))`
- etc.

**These resolutions will now fail** because we removed those services from DI.

Update each `App.Services.GetService(...)` resolution in `OnDataContextChanged` to
route through the ViewModel's active tab instead:

```csharp
// Before:
_editorVm = App.Services.GetService(typeof(EditorViewModel)) as EditorViewModel;

// After:
_editorVm = vm.ActiveTab.EditorViewModel;
```

Apply this pattern for all five per-tab services:
- `EditorViewModel` → `vm.ActiveTab.EditorViewModel`
- `PreviewViewModel` → `vm.ActiveTab.PreviewViewModel`
- `FileService` → `vm.ActiveTab.FileService`
- `SourceMappingService` → `vm.ActiveTab.SourceMappingService`
- `MarkdownParsingService` (if resolved directly) → `vm.ActiveTab.MarkdownParsingService`

All other `App.Services.GetService(...)` calls (for singleton services) remain
unchanged.

---

## What NOT to change

- `MainWindow.axaml` — no AXAML changes in this phase
- All singleton services — no changes to their implementations
- `TopologyViewModel`, `OutlineViewModel` — they still subscribe to the initial tab's
  `MarkdownParsingService`; re-subscription on tab switch is Phase T3
- `SnippetModeController`, `SnippetInsertionService` — stay singleton, take editor
  as method parameter, no changes needed
- The `EditorViewModel`, `PreviewViewModel`, `FileService`, `MarkdownParsingService`,
  `SourceMappingService` implementations — no changes to these classes themselves

---

## Acceptance criteria

- [ ] App builds without errors
- [ ] App launches and works identically to before — single document, all features
      functional
- [ ] `MainWindowViewModel.Tabs` contains exactly one `TabViewModel` on startup
- [ ] `MainWindowViewModel.ActiveTab` is non-null and equals `Tabs[0]`
- [ ] `ActiveTab.FileService`, `ActiveTab.EditorViewModel` etc. are the live instances
      wired to the UI
- [ ] `AppSettings` has the three new tab persistence fields
- [ ] `NewTabCommand` and `CloseTabCommand` exist (stubs, not wired to UI yet)
- [ ] No regressions: file open/save, themes, scroll sync, AI assist, snippets,
      timeline, export all work as before
- [ ] 0 build errors, pre-existing warnings only

---

## Reporting

When complete, summarize:
1. Every file changed with before/after for all non-trivial edits
2. The exact constructor signature change to `MainWindowViewModel`
3. Anywhere the actual code differed from these instructions and how you adapted
4. Any uncertainties — especially around `PreviewViewModel` and `MarkdownParsingService`
   constructor signatures, which were not provided and must be read from source
# Task: BL-04 Phase T5 — Tab persistence (restore open tabs on relaunch)

This is the final tab phase. On shutdown, open tab paths are already saved to
`AppSettings.OpenTabPaths` and `ActiveTabIndex`. T5 implements the restore side.

---

## BEFORE WRITING ANY CODE — read these files in full:

1. `GhsMarkdown.Cross/src/GhsMarkdown.Cross/App.axaml.cs`
2. `GhsMarkdown.Cross/src/GhsMarkdown.Cross/ViewModels/MainWindowViewModel.cs`

Do not guess their content.

---

## Overview

On launch, if `OpenTabPaths` contains saved paths, restore them as tabs in order
and set the active tab to `ActiveTabIndex`. If all paths are missing from disk,
fall through to the default single untitled tab. Untitled tabs (null path) are
skipped — they have no persistent content to restore.

The initial tab (constructed in `MainWindowViewModel`) always starts as untitled.
If there are paths to restore, that initial tab becomes the first restored tab;
additional paths get new tabs.

---

## Change 1 — `MainWindowViewModel.cs`: add `RestoreTabsAsync`

Add this public method:

```csharp
/// <summary>
/// Restores tabs from persisted paths. Called by App after DI container is
/// built and the window is shown. Silently skips missing files.
/// </summary>
public async Task RestoreTabsAsync(List<string?> paths, int activeIndex)
{
    // Filter to paths that exist on disk (skip nulls = untitled, skip missing)
    var validPaths = paths
        .Where(p => p is not null && File.Exists(p))
        .Select(p => p!)
        .ToList();

    if (validPaths.Count == 0)
        return; // Nothing to restore — leave the default untitled tab

    // Open the first path in the existing initial (untitled) tab
    await ActiveTab.FileService.OpenFile(validPaths[0]);

    // Open remaining paths each in a new tab
    for (int i = 1; i < validPaths.Count; i++)
    {
        // Enforce MaxRestoredTabs limit
        var settings = _settingsService.Load();
        if (Tabs.Count >= settings.MaxRestoredTabs) break;

        NewTabCommand.Execute(null);
        await ActiveTab.FileService.OpenFile(validPaths[i]);
    }

    // Restore active tab index (clamped to valid range)
    // Map original index to restored tabs by matching paths
    var targetPath = activeIndex >= 0 && activeIndex < paths.Count
        ? paths[activeIndex]
        : null;

    if (targetPath is not null)
    {
        var targetTab = Tabs.FirstOrDefault(
            t => t.FileService.CurrentFilePath == targetPath);
        if (targetTab is not null)
            SwitchTabCommand.Execute(targetTab);
    }
    else if (Tabs.Count > 0)
    {
        // Fallback: activate the first tab
        SwitchTabCommand.Execute(Tabs[0]);
    }
}
```

---

## Change 2 — `App.axaml.cs`: call RestoreTabsAsync after window is shown

After reading `App.axaml.cs`, find the block at the bottom of
`OnFrameworkInitializationCompleted` that handles the command-line `.md` argument
(the `Dispatcher.UIThread.Post` that calls `fs.OpenFile(mdArg)`).

Add tab restore before that block, at `DispatcherPriority.Loaded` so the window
is visible before any file I/O begins:

```csharp
// Restore tabs from previous session (T5)
var settingsForRestore = Services.GetRequiredService<SettingsService>().Load();
if (settingsForRestore.OpenTabPaths.Count > 0)
{
    Dispatcher.UIThread.Post(async () =>
    {
        var mainVm = Services.GetRequiredService<MainWindowViewModel>();
        await mainVm.RestoreTabsAsync(
            settingsForRestore.OpenTabPaths,
            settingsForRestore.ActiveTabIndex);
    }, DispatcherPriority.Loaded);
}
```

**Important:** the existing command-line argument handler already opens a file via
`vm.ActiveTab.FileService.OpenFile(mdArg)`. If tab restore runs at `Loaded`
priority and the CLI handler also runs at `Loaded`, they may race. Wrap the CLI
handler in a check so it only fires if no tab restore is happening:

```csharp
// Open .md file passed as command-line argument
// Skip if tab restore will run (restore takes precedence over CLI arg on relaunch)
var mdArg = args.Skip(1).FirstOrDefault(a =>
    a.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && File.Exists(a));

if (mdArg is not null && settingsForRestore.OpenTabPaths.Count == 0)
{
    Dispatcher.UIThread.Post(async () =>
    {
        await vm.ActiveTab.FileService.OpenFile(mdArg);
    }, DispatcherPriority.Loaded);
}
else if (mdArg is not null)
{
    // A session restore is happening — the pipe server will handle this arg
    // if launched as a second instance, or it can be ignored on cold launch
    // since the session restore already opens the user's previous work.
    // If you want CLI args to always win over restore, swap this condition.
}
```

**Note:** Read the actual `App.axaml.cs` carefully before applying this. The
existing CLI handler may reference `fs` or `vm` variables defined earlier in the
method — make sure `settingsForRestore` reuses the same loaded settings object
rather than loading settings twice if possible.

---

## What NOT to change

- `SaveSettings()` in `MainWindowViewModel` — already saves `OpenTabPaths` and
  `ActiveTabIndex` on every relevant action (file open, save, tab switch, view
  mode change). No changes needed.
- `AppSettings` record — `OpenTabPaths`, `ActiveTabIndex`, `MaxRestoredTabs` were
  added in T1. No changes needed.
- `Program.cs` — no changes.
- All tab switching code (T2/T3/T4) — no changes.

---

## Acceptance criteria

- [ ] Open 3 tabs with different files, close the app, relaunch — all 3 tabs
      restore with correct content
- [ ] The previously active tab is restored as the active tab
- [ ] Untitled (unsaved) tabs are not restored (no empty tabs appear)
- [ ] If all saved paths are missing from disk, the app launches with a single
      untitled tab (default behavior)
- [ ] If some paths are missing and some exist, only the existing ones restore
- [ ] MaxRestoredTabs (default 10) is respected
- [ ] Launching with a `.md` CLI arg on a fresh install (no saved session) still
      opens that file correctly
- [ ] App builds without warnings

---

## Reporting

When complete, summarize:
1. Exact location in `App.axaml.cs` where `RestoreTabsAsync` is called
2. How the CLI arg / restore race condition was resolved
3. Any edge cases found in the path-matching logic for active tab index
4. Any uncertainties
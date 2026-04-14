# Task: BL-04 Phase T2 — Tab strip UI and tab switching

Phase T1 established the TabViewModel data model. This phase adds the tab strip UI,
implements tab switching with full editor/preview re-wiring, close button with unsaved
changes dialog, new tab button, and middle-click close.

---

## BEFORE WRITING ANY CODE — read these files in full

1. `GhsMarkdown.Cross/src/GhsMarkdown.Cross/Views/MainWindow.axaml`
2. `GhsMarkdown.Cross/src/GhsMarkdown.Cross/Views/MainWindow.axaml.cs`
3. `GhsMarkdown.Cross/src/GhsMarkdown.Cross/ViewModels/MainWindowViewModel.cs`
4. `GhsMarkdown.Cross/src/GhsMarkdown.Cross/Services/FileService.cs`

Do not guess content. Every edit must be based on what you actually read.

---

## Overview

The titlebar is restructured to hold the tab strip between the app name and the
existing right-side controls. Switching tabs swaps the editor document, re-wires
per-tab event subscriptions, and triggers a preview reload. Closing a tab with
unsaved changes shows a Save/Discard/Cancel dialog.

---

## Change 1 — `FileService.cs`: expose unsaved changes dialog

Add this public method to `FileService`. It delegates to the existing private static
`ShowUnsavedChangesDialogAsync()`:

```csharp
/// <summary>Shows the Save/Discard/Cancel dialog. Called by tab close logic.</summary>
public static Task<UnsavedAction> PromptUnsavedChangesAsync()
    => ShowUnsavedChangesDialogAsync();
```

---

## Change 2 — `Window.Styles` in `MainWindow.axaml`: add tab styles

Add these styles to the existing `Window.Styles` block, after the pull-tab style:

```xml
<!-- Tab item -->
<Style Selector="Border.tab-item">
  <Setter Property="MinWidth" Value="80" />
  <Setter Property="MaxWidth" Value="200" />
  <Setter Property="Height" Value="38" />
  <Setter Property="Padding" Value="0" />
  <Setter Property="Background" Value="{DynamicResource bg-panel}" />
  <Setter Property="BorderThickness" Value="0,0,1,0" />
  <Setter Property="BorderBrush" Value="{DynamicResource border}" />
  <Setter Property="Cursor" Value="Hand" />
</Style>

<Style Selector="Border.tab-item.tab-active">
  <Setter Property="Background" Value="{DynamicResource bg-editor}" />
</Style>

<Style Selector="Border.tab-item:pointerover">
  <Setter Property="Background" Value="{DynamicResource bg-shell}" />
</Style>
```

---

## Change 3 — Titlebar restructure in `MainWindow.axaml`

Replace the entire titlebar `Border` block (the `DockPanel.Dock="Top"` 38px border)
with this new version. The key change: `Grid ColumnDefinitions="*,Auto,*"` becomes
`ColumnDefinitions="Auto,*,Auto"`, the window title becomes a short app label in Col 0,
the tab strip fills Col 1, and the existing controls move to Col 2.

```xml
<!-- ═══ TITLEBAR (38px) ═══ -->
<Border DockPanel.Dock="Top"
        Height="38"
        Background="{DynamicResource bg-toolbar}"
        BorderBrush="{DynamicResource border}"
        BorderThickness="0,0,0,1">
  <Grid ColumnDefinitions="Auto,*,Auto">

    <!-- Col 0: App name -->
    <StackPanel Grid.Column="0"
                Orientation="Horizontal"
                VerticalAlignment="Center"
                Margin="12,0,0,0"
                Spacing="0">
      <TextBlock Text="GHS Markdown"
                 FontSize="12"
                 FontWeight="Medium"
                 VerticalAlignment="Center"
                 Foreground="{DynamicResource text-secondary}" />
      <Border Width="1" Height="16"
              Background="{DynamicResource border}"
              Margin="10,0,0,0" />
    </StackPanel>

    <!-- Col 1: Tab strip -->
    <ScrollViewer Grid.Column="1"
                  HorizontalScrollBarVisibility="Hidden"
                  VerticalScrollBarVisibility="Disabled"
                  VerticalAlignment="Stretch">
      <StackPanel Orientation="Horizontal"
                  VerticalAlignment="Stretch"
                  Spacing="0">

        <!-- Tab items -->
        <ItemsControl x:Name="TabStrip"
                      ItemsSource="{Binding Tabs}">
          <ItemsControl.ItemsPanel>
            <ItemsPanelTemplate>
              <StackPanel Orientation="Horizontal" />
            </ItemsPanelTemplate>
          </ItemsControl.ItemsPanel>
          <ItemsControl.ItemTemplate>
            <DataTemplate DataType="{x:Type vm:TabViewModel}">
              <Border Classes="tab-item"
                      Classes.tab-active="{Binding IsActive}"
                      x:Name="TabItemBorder">
                <Panel>
                  <!-- Active accent underline -->
                  <Border Height="2"
                          VerticalAlignment="Bottom"
                          Background="{DynamicResource accent}"
                          IsVisible="{Binding IsActive}" />

                  <!-- Tab content -->
                  <StackPanel Orientation="Horizontal"
                              VerticalAlignment="Center"
                              Margin="10,0,6,0"
                              Spacing="4">

                    <!-- Dirty dot -->
                    <Ellipse Width="6" Height="6"
                             Fill="{DynamicResource accent}"
                             IsVisible="{Binding IsDirty}"
                             VerticalAlignment="Center" />

                    <!-- Filename -->
                    <TextBlock Text="{Binding DisplayName}"
                               FontSize="11"
                               VerticalAlignment="Center"
                               Foreground="{DynamicResource text-primary}" />

                    <!-- Close button -->
                    <Button Classes="icon-rail-btn"
                            Width="16" Height="16"
                            Padding="0"
                            VerticalAlignment="Center"
                            ToolTip.Tip="Close tab"
                            Tag="{Binding}">
                      <TextBlock Text="✕"
                                 FontSize="9"
                                 HorizontalAlignment="Center"
                                 VerticalAlignment="Center"
                                 Foreground="{DynamicResource text-hint}" />
                    </Button>

                  </StackPanel>
                </Panel>
              </Border>
            </DataTemplate>
          </ItemsControl.ItemTemplate>
        </ItemsControl>

        <!-- New tab button -->
        <Button Classes="icon-rail-btn"
                x:Name="NewTabButton"
                Width="28" Height="28"
                Padding="0"
                VerticalAlignment="Center"
                Margin="2,0,0,0"
                ToolTip.Tip="New Tab (Ctrl+T)">
          <TextBlock Text="+"
                     FontSize="16"
                     HorizontalAlignment="Center"
                     VerticalAlignment="Center"
                     Foreground="{DynamicResource text-hint}" />
        </Button>

      </StackPanel>
    </ScrollViewer>

    <!-- Col 2: Right controls (unchanged from before) -->
    <StackPanel Grid.Column="2"
                Orientation="Horizontal"
                HorizontalAlignment="Right"
                VerticalAlignment="Center"
                Margin="0,0,12,0"
                Spacing="2">
      <Button Classes="icon-rail-btn"
              Command="{Binding ToggleFormattingToolbarCommand}"
              Width="NaN" Height="26" Padding="8,0"
              ToolTip.Tip="Toggle Formatting Toolbar (Ctrl+Shift+B)">
        <TextBlock Text="&#x2261;" FontSize="14"
                   Foreground="{DynamicResource accent}" />
      </Button>
      <Border Width="1" Height="16"
              Background="{DynamicResource border}" Margin="6,0" />
      <Button Classes="icon-rail-btn"
              Classes.active="{Binding IsEditMode}"
              Command="{Binding SetViewModeEditCommand}"
              Width="NaN" Height="26" Padding="8,0"
              ToolTip.Tip="Edit Only (Ctrl+Shift+E)">
        <TextBlock Text="Edit" FontSize="11" />
      </Button>
      <Button Classes="icon-rail-btn"
              Classes.active="{Binding IsSplitMode}"
              Command="{Binding SetViewModeSplitCommand}"
              Width="NaN" Height="26" Padding="8,0"
              ToolTip.Tip="Split (Ctrl+Shift+T)">
        <TextBlock Text="Split" FontSize="11" />
      </Button>
      <Button Classes="icon-rail-btn"
              Classes.active="{Binding IsPreviewMode}"
              Command="{Binding SetViewModePreviewCommand}"
              Width="NaN" Height="26" Padding="8,0"
              ToolTip.Tip="Preview Only (Ctrl+Shift+W)">
        <TextBlock Text="Preview" FontSize="11" />
      </Button>
      <Button Classes="icon-rail-btn"
              Command="{Binding DetachPreviewCommand}"
              Width="NaN" Height="26" Padding="8,0"
              ToolTip.Tip="Detach Preview Window (Ctrl+D)">
        <TextBlock Text="&#x29C9;" FontSize="13"
                   Foreground="{DynamicResource accent}" />
      </Button>
      <Border Width="1" Height="16" Background="{DynamicResource border}" Margin="6,0" />
      <Button Classes="icon-rail-btn"
              Command="{Binding OpenExportCommand}"
              Width="NaN" Height="26" Padding="8,0"
              ToolTip.Tip="Export">
        <TextBlock Text="Export…" FontSize="11" Foreground="{DynamicResource accent}" />
      </Button>
      <Button Classes="icon-rail-btn"
              Command="{Binding PrintCommand}"
              Width="NaN" Height="26" Padding="8,0"
              ToolTip.Tip="Print document (Ctrl+Shift+P)"
              Margin="4,0,0,0">
        <TextBlock Text="Print" FontSize="11" Foreground="{DynamicResource accent}" />
      </Button>
      <Border Width="1" Height="16" Background="{DynamicResource border}" Margin="8,0" />
      <ToggleButton Classes="focus-toggle"
                    IsChecked="{Binding IsFocusModeActive, Mode=OneWay}"
                    Command="{Binding ToggleFocusModeCommand}"
                    ToolTip.Tip="Toggle Focus Mode (Ctrl+Shift+F)">
        <TextBlock Text="Focus" FontSize="11" Foreground="{DynamicResource accent}" />
      </ToggleButton>
    </StackPanel>

  </Grid>
</Border>
```

---

## Change 4 — `Window.KeyBindings` in `MainWindow.axaml`

Add these three key bindings to the existing `Window.KeyBindings` block:

```xml
<KeyBinding Gesture="Ctrl+T" Command="{Binding NewTabCommand}" />
<KeyBinding Gesture="Ctrl+W" Command="{Binding CloseActiveTabCommand}" />
<KeyBinding Gesture="Ctrl+Tab" Command="{Binding NextTabCommand}" />
```

---

## Change 5 — `MainWindowViewModel.cs`: implement tab commands

### 5a — Add `CloseActiveTabCommand`

```csharp
[RelayCommand]
private async Task CloseActiveTab() => await CloseTab(ActiveTab);
```

### 5b — Implement `CloseTabCommand` fully

Replace the T1 stub with:

```csharp
[RelayCommand]
private async Task CloseTab(TabViewModel tab)
{
    if (tab.IsDirty)
    {
        var result = await FileService.PromptUnsavedChangesAsync();
        if (result == UnsavedAction.Cancel) return;
        if (result == UnsavedAction.Save)
            await tab.FileService.SaveFile();
    }

    var idx = Tabs.IndexOf(tab);
    Tabs.Remove(tab);

    if (Tabs.Count == 0)
    {
        NewTab();
        return;
    }

    // Activate the tab to the left, or the new last tab
    var newIdx = Math.Max(0, Math.Min(idx, Tabs.Count - 1));
    ActiveTab = Tabs[newIdx];
}
```

### 5c — Implement `NextTabCommand`

```csharp
[RelayCommand]
private void NextTab()
{
    if (Tabs.Count < 2) return;
    var idx = Tabs.IndexOf(ActiveTab);
    ActiveTab = Tabs[(idx + 1) % Tabs.Count];
}
```

### 5d — Implement `SwitchTabCommand` fully

Replace the T1 stub with:

```csharp
[RelayCommand]
private void SwitchTab(TabViewModel tab)
{
    if (tab == ActiveTab) return;
    ActiveTab = tab;
}
```

### 5e — Implement `NewTabCommand` fully

The T1 stub should already work. Confirm it creates a `TabViewModel` with
`_settingsService` and `_themeService` and adds it to `Tabs`, then sets `ActiveTab`.
If `_themeService` is not a backing field in `MainWindowViewModel`, read the constructor
and add `private readonly ThemeService _themeService;` assignment.

---

## Change 6 — `MainWindow.axaml.cs`: tab switching re-wiring

This is the most significant change. The goal: when `ActiveTab` changes, swap the
per-tab service references and re-wire the minimal set of event handlers that are
per-tab.

### 6a — Identify and name the per-tab event handlers

After reading `MainWindow.axaml.cs`, find these anonymous lambda subscriptions and
convert them to named private methods so they can be unsubscribed:

**`_editorVm.PropertyChanged`** — the handler that syncs `_editor.Document.Text`
when `EditorViewModel.DocumentText` changes externally (file open, new file). Extract
to a named method:

```csharp
private void OnEditorVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
{
    if (e.PropertyName == nameof(EditorViewModel.DocumentText)
        && _editor is not null
        && _editor.Document.Text != _editorVm?.DocumentText)
    {
        _editor.Document.Text = _editorVm?.DocumentText ?? string.Empty;
    }
}
```

**`_previewVm.PropertyChanged`** — the handler that calls `NavigateWebView` when
`PreviewHtml` changes. Extract to:

```csharp
private void OnPreviewVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
{
    if (e.PropertyName == nameof(PreviewViewModel.PreviewHtml) && _previewVm is not null)
        NavigateWebView(_previewVm.PreviewHtml);
}
```

**`_fileService.PropertyChanged`** — the handler that updates window title and
triggers git diff on `CurrentFilePath` change. Extract to:

```csharp
private void OnFileServicePropertyChanged(object? sender, PropertyChangedEventArgs e)
{
    if (e.PropertyName == nameof(FileService.CurrentFilePath))
    {
        UpdateWindowTitle();
        RefreshGitDiff(_fileService?.CurrentFilePath);
    }
}
```

Where these lambdas are currently subscribed in `OnDataContextChanged`, replace them
with the named method references. Example:

```csharp
// Before:
_editorVm.PropertyChanged += (_, pe) => { ... };

// After:
_editorVm.PropertyChanged += OnEditorVmPropertyChanged;
```

**Note:** Read the actual code carefully. There may be additional per-tab subscriptions.
Apply the named-method pattern to any subscription on `_editorVm`, `_previewVm`,
or `_fileService` that needs to be unsubscribed on tab switch. Subscriptions on
singleton services (ThemeService, SnapshotService, etc.) do NOT need this treatment.

### 6b — Add `SwitchActiveTab` method

Add this private method:

```csharp
private void SwitchActiveTab(TabViewModel newTab)
{
    // 1. Unsubscribe from current per-tab services
    if (_editorVm is not null)
        _editorVm.PropertyChanged -= OnEditorVmPropertyChanged;
    if (_previewVm is not null)
        _previewVm.PropertyChanged -= OnPreviewVmPropertyChanged;
    if (_fileService is not null)
    {
        _fileService.PropertyChanged -= OnFileServicePropertyChanged;
        _fileService.FileSaved       -= OnFileSaved;
    }

    // 2. Swap references
    _editorVm             = newTab.EditorViewModel;
    _previewVm            = newTab.PreviewViewModel;
    _sourceMappingService = newTab.SourceMappingService;
    _fileService          = newTab.FileService;

    // 3. Re-subscribe to new tab's services
    _editorVm.PropertyChanged  += OnEditorVmPropertyChanged;
    _previewVm.PropertyChanged += OnPreviewVmPropertyChanged;
    _fileService.PropertyChanged += OnFileServicePropertyChanged;
    _fileService.FileSaved       += OnFileSaved;

    // 4. Load new tab's document into editor
    if (_editor is not null && _editor.Document.Text != _editorVm.DocumentText)
        _editor.Document.Text = _editorVm.DocumentText ?? string.Empty;

    // 5. Trigger preview reload
    if (_webViewReady && _previewVm is not null)
        NavigateWebView(_previewVm.PreviewHtml);

    // 6. Reset scroll sync state
    _lastSyncedAnchorLine = -1;
    _savedScrollY = 0;
    _lastActiveSelector = null;

    // 7. Refresh git diff for new file
    RefreshGitDiff(_fileService.CurrentFilePath);

    // 8. Update window title via ViewModel
    _mainVm?.UpdateWindowTitleFromTab(newTab);
}
```

**Note:** `OnFileSaved` — find the current `_fileService.FileSaved` handler in the
existing code. If it is an anonymous lambda, extract it to a named method using the
same pattern as above.

### 6c — Subscribe to ActiveTab changes

In `OnDataContextChanged`, after all the existing wiring, add:

```csharp
// Subscribe to tab switches
vm.PropertyChanged += (_, pe) =>
{
    if (pe.PropertyName == nameof(MainWindowViewModel.ActiveTab)
        && _mainVm?.ActiveTab is { } newTab)
    {
        SwitchActiveTab(newTab);
    }
};
```

### 6d — Wire tab strip click and middle-click

After finding the `TabStrip` and `NewTabButton` controls in `OnDataContextChanged`:

```csharp
var tabStrip = this.FindControl<ItemsControl>("TabStrip");
var newTabBtn = this.FindControl<Button>("NewTabButton");

if (newTabBtn is not null)
    newTabBtn.Click += (_, _) => _mainVm?.NewTabCommand.Execute(null);

// Wire tab item clicks and middle-click via visual tree traversal
// This runs after layout so tabs are in the visual tree
Dispatcher.UIThread.Post(() => WireTabStripInteraction(), DispatcherPriority.Loaded);
```

Add this method:

```csharp
private void WireTabStripInteraction()
{
    var tabStrip = this.FindControl<ItemsControl>("TabStrip");
    if (tabStrip is null || _mainVm is null) return;

    // Re-wire whenever the Tabs collection changes
    _mainVm.Tabs.CollectionChanged += (_, _) =>
        Dispatcher.UIThread.Post(WireTabItemHandlers, DispatcherPriority.Loaded);

    WireTabItemHandlers();
}

private void WireTabItemHandlers()
{
    var tabStrip = this.FindControl<ItemsControl>("TabStrip");
    if (tabStrip is null || _mainVm is null) return;

    foreach (var tabVm in _mainVm.Tabs)
    {
        // Find the Border container for this tab via visual tree
        var container = tabStrip.GetVisualDescendants()
            .OfType<Border>()
            .FirstOrDefault(b => b.Classes.Contains("tab-item") && b.DataContext == tabVm);

        if (container is null) continue;

        // Avoid double-wiring
        container.PointerPressed -= OnTabPointerPressed;
        container.PointerPressed += OnTabPointerPressed;

        // Wire close button
        var closeBtn = container.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(b => b.Tag is TabViewModel);
        if (closeBtn is not null)
        {
            closeBtn.Click -= OnTabCloseClick;
            closeBtn.Click += OnTabCloseClick;
        }
    }
}

private void OnTabPointerPressed(object? sender, PointerPressedEventArgs e)
{
    if (sender is not Border border || border.DataContext is not TabViewModel tab) return;

    var props = e.GetCurrentPoint(null).Properties;

    if (props.IsMiddleButtonPressed)
    {
        // Middle-click closes tab
        _ = _mainVm?.CloseTabCommand.ExecuteAsync(tab);
        e.Handled = true;
        return;
    }

    if (props.IsLeftButtonPressed)
    {
        _mainVm?.SwitchTabCommand.Execute(tab);
    }
}

private void OnTabCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
{
    if (sender is Button btn && btn.Tag is TabViewModel tab)
    {
        _ = _mainVm?.CloseTabCommand.ExecuteAsync(tab);
        e.Handled = true; // prevent click from bubbling to tab border
    }
}
```

---

## Change 7 — `MainWindowViewModel.cs`: expose window title update

The current `UpdateWindowTitle()` is private and reads from `_fileService`. After tab
switching, the ViewModel needs to update based on the newly active tab. Add this public
method:

```csharp
public void UpdateWindowTitleFromTab(TabViewModel tab)
{
    var path = tab.FileService.CurrentFilePath;
    var name = path is null ? "Untitled" : Path.GetFileName(path);
    WindowTitle = $"GHS Markdown Editor — {name}";
}
```

---

## What NOT to change in this phase

- `TopologyViewModel` and `OutlineViewModel` subscriptions — they still reference the
  initial tab's `MarkdownParsingService`. Re-subscription on tab switch is Phase T3.
- `SnapshotService` and `TimelineViewModel` wiring — per-tab snapshot handling is T3.
- Snapshot/draft timers in `MainWindowViewModel` — T3.
- Tab persistence (save/restore open tabs) — Phase T5.
- Singleton IPC — Phase T4.

---

## Acceptance criteria

- [ ] Tab strip renders in the titlebar with correct GHS Dark/Light styling
- [ ] Initial tab shows filename (or "Untitled") with no dirty dot
- [ ] Clicking the + button opens a new blank Untitled tab and switches to it
- [ ] Clicking a tab switches the editor and preview to that tab's content
- [ ] Each tab independently edits its own document
- [ ] Dirty dot appears on a tab when it has unsaved changes
- [ ] Clicking × on a clean tab closes it
- [ ] Clicking × on a dirty tab shows Save/Discard/Cancel dialog
      — Save: saves then closes; Discard: closes without saving; Cancel: does nothing
- [ ] Middle-click on a tab closes it (with dirty check)
- [ ] Closing the last tab opens a new Untitled tab
- [ ] Ctrl+T opens a new tab
- [ ] Ctrl+W closes the active tab (with dirty check)
- [ ] Ctrl+Tab cycles to the next tab
- [ ] Theme switching (Dark/Light/Custom) applies correctly to all tabs
- [ ] All existing features work on each tab: scroll sync, git diff, AI assist,
      snippets, export, timeline
- [ ] 0 build errors, pre-existing warnings only

---

## Reporting

When complete, summarize:
1. Every file changed with before/after for all non-trivial edits
2. Which anonymous lambdas were converted to named methods and what their names are
3. Whether `_themeService` was already a backing field in `MainWindowViewModel` or
   needed to be added
4. Any adaptations from the actual code that differed from these instructions
5. Any uncertainties, especially around the `OnFileSaved` handler extraction
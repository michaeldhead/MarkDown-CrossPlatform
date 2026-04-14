## Fix: prompt for unsaved changes on app close

Two files: `App.axaml.cs` and `MainWindowViewModel.cs`.

### Root cause

The window `Closing` handler only calls `ForceSaveSettings()`. It does not check
for dirty tabs. Untitled tabs with unsaved content are silently discarded.
Named-file tabs with unsaved changes are also silently discarded.

---

### Change 1 — `MainWindowViewModel.cs`: add `HasUnsavedTabs` and `GetDirtyTabs`

```csharp
public bool HasUnsavedTabs => Tabs.Any(t => t.IsDirty);

public IReadOnlyList<TabViewModel> GetDirtyTabs() =>
    Tabs.Where(t => t.IsDirty).ToList();
```

### Change 2 — `App.axaml.cs`: replace the Closing handler

Replace the current `Closing` handler with an async-safe version using
`CancelEventArgs`. Avalonia supports async closing via `e.Cancel = true` +
re-close after the dialog:

```csharp
desktop.MainWindow.Closing += async (sender, e) =>
{
    var mainVm = Services.GetRequiredService<MainWindowViewModel>();

    if (mainVm.HasUnsavedTabs)
    {
        // Cancel the close immediately — we'll re-trigger it after dialogs
        e.Cancel = true;

        var dirty = mainVm.GetDirtyTabs();
        foreach (var tab in dirty)
        {
            // Switch to the tab so the user can see what they're saving
            mainVm.SwitchTabCommand.Execute(tab);

            var label = tab.FileService.CurrentFilePath is null
                ? "Untitled"
                : Path.GetFileName(tab.FileService.CurrentFilePath);

            var result = await ShowCloseUnsavedDialog(label, desktop.MainWindow!);

            if (result == UnsavedAction.Cancel)
                return; // User cancelled — abort the close entirely

            if (result == UnsavedAction.Save)
                await tab.FileService.SaveFile();
            // Discard — fall through, tab content is lost (intentional)
        }

        // All dirty tabs handled — save settings and close for real
        mainVm.ForceSaveSettings();
        desktop.MainWindow!.Close();
    }
    else
    {
        mainVm.ForceSaveSettings();
    }
};
```

Add this static helper in `App.axaml.cs`:

```csharp
private static async Task<UnsavedAction> ShowCloseUnsavedDialog(
    string fileName, Window owner)
{
    var action = UnsavedAction.Cancel;
    var dlg = new Window
    {
        Title = "Unsaved Changes",
        Width = 420, Height = 180,
        CanResize = false,
        WindowDecorations = Avalonia.Controls.WindowDecorations.None,
        Background = new Avalonia.Media.SolidColorBrush(
            Avalonia.Media.Color.Parse("#181818")),
        WindowStartupLocation = WindowStartupLocation.CenterOwner
    };

    var saveBtn = new Button
    {
        Content = "Save",
        Background = new Avalonia.Media.SolidColorBrush(
            Avalonia.Media.Color.Parse("#4A9EFF")),
        Foreground = Avalonia.Media.Brushes.White,
        Width = 100,
        Margin = new Avalonia.Thickness(0, 0, 8, 0),
        Cursor = new Avalonia.Input.Cursor(
            Avalonia.Input.StandardCursorType.Hand)
    };
    var discardBtn = new Button
    {
        Content = "Don't Save",
        Background = Avalonia.Media.Brushes.Transparent,
        Foreground = new Avalonia.Media.SolidColorBrush(
            Avalonia.Media.Color.Parse("#C75050")),
        Width = 100,
        Margin = new Avalonia.Thickness(0, 0, 8, 0),
        Cursor = new Avalonia.Input.Cursor(
            Avalonia.Input.StandardCursorType.Hand)
    };
    var cancelBtn = new Button
    {
        Content = "Cancel",
        Background = Avalonia.Media.Brushes.Transparent,
        Foreground = new Avalonia.Media.SolidColorBrush(
            Avalonia.Media.Color.Parse("#888888")),
        Width = 100,
        Cursor = new Avalonia.Input.Cursor(
            Avalonia.Input.StandardCursorType.Hand)
    };

    saveBtn.Click    += (_, _) => { action = UnsavedAction.Save;    dlg.Close(); };
    discardBtn.Click += (_, _) => { action = UnsavedAction.Discard; dlg.Close(); };
    cancelBtn.Click  += (_, _) => { action = UnsavedAction.Cancel;  dlg.Close(); };

    dlg.Content = new StackPanel
    {
        Margin = new Avalonia.Thickness(20),
        Children =
        {
            new TextBlock
            {
                Text = $"\"{fileName}\" has unsaved changes.\nSave before closing?",
                Foreground = new Avalonia.Media.SolidColorBrush(
                    Avalonia.Media.Color.Parse("#E8E8E8")),
                FontSize = 13,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Margin = new Avalonia.Thickness(0, 0, 0, 16)
            },
            new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                Children = { cancelBtn, discardBtn, saveBtn }
            }
        }
    };

    await dlg.ShowDialog(owner);
    return action;
}
```

---

### Note on UnsavedAction

`UnsavedAction` is defined in `FileService.cs`. If it is not accessible from
`App.axaml.cs`, add the appropriate `using GhsMarkdown.Cross.Services;` directive.

---

### Acceptance criteria
- [ ] Closing the app with one dirty named-file tab shows the Save/Don't Save/Cancel
      dialog for that tab
- [ ] Closing with one dirty untitled tab shows the dialog with "Untitled"
- [ ] Closing with multiple dirty tabs shows a dialog for each in sequence
- [ ] Clicking Cancel on any dialog aborts the close — app stays open
- [ ] Clicking Don't Save discards that tab's content and moves to the next dialog
- [ ] Clicking Save saves the file (triggering Save As for untitled) then moves on
- [ ] After handling all dirty tabs the app closes cleanly
- [ ] Closing with no dirty tabs closes immediately with no dialog
- [ ] App builds without warnings

### Reporting
Confirm where `ShowCloseUnsavedDialog` was placed and whether `UnsavedAction`
required a new using directive.
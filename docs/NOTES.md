# GHS Markdown Editor — Implementation Notes

## Phase 1 Compatibility Notes

### Solution File Format

`dotnet new sln` with SDK 10.0.201 creates `GhsMarkdown.Cross.slnx` (the new XML-based solution format introduced in .NET 9+) instead of `GhsMarkdown.Cross.sln`. This is functionally equivalent and supported by VS 2022 17.13+.

---

### Avalonia + FluentAvalonia Version Resolution

**Issue:** The spec targets Avalonia 11.1.x + FluentAvalonia 2.x. These are incompatible.

- FluentAvaloniaUI 2.5.1 (latest 2.x) requires **Avalonia ≥ 11.3.12**
- Avalonia 11.1.x versions (11.1.0–11.1.5) are too old for FluentAvalonia 2.x

**Resolution:** Using **Avalonia 11.3.12** with FluentAvaloniaUI 2.5.1.

### .NET SDK + Target Framework

- Installed SDK: 10.0.201 (.NET 10)
- Target framework: **net10.0** (FluentAvaloniaUI 2.5.1 requires net10.0; net8.0 not supported by FluentAvalonia 2.5.1)

### Package Versions Used (Phase 1)

| Package | Version | Notes |
|---|---|---|
| Avalonia | 11.3.12 | Upgraded from 11.1.x for FluentAvalonia 2.x compat |
| Avalonia.Desktop | 11.3.12 | |
| Avalonia.Themes.Fluent | 11.3.12 | |
| FluentAvaloniaUI | 2.5.1 | Requires Avalonia ≥ 11.3.12 AND net10.0 |
| CommunityToolkit.Mvvm | 8.4.2 | |
| Microsoft.Extensions.DependencyInjection | 8.0.1 | |
| Avalonia.Controls.WebView | 11.4.0 | WebView spike — see below |

---

## WebView Spike — Phase 1

**PASS — Avalonia.Controls.WebView 11.4.0**

- Package: `Avalonia.Controls.WebView` version 11.4.0 (official Avalonia package, MIT license)
- Control used: `NativeWebView` (`Avalonia.Controls.NativeWebView`)
- Platform: Windows (WebView2 backend)
- Static HTML rendered in preview pane using `NavigateToString()`
- Requires: Windows application manifest with `<supportedOS>` for Windows 10/11
- Runtime note: `Failed to unregister class Chrome_WidgetWin_0. Error = 1412` on exit — this is a normal non-fatal WebView2/Chromium cleanup message, not an error.

---

## Phase 2a Package Versions

| Package | Version | Notes |
|---|---|---|
| Avalonia.AvaloniaEdit | 11.4.1 | Latest 11.x stable; compatible with Avalonia 11.3.12 |
| Markdig | 0.44.0 | Latest 0.x stable; 1.x skipped to avoid potential API churn |

### AvaloniaEdit xshd — syntax highlighting note
`Assets/MarkdownSyntax.xshd` uses the GHS Dark theme color tokens hardcoded as hex values.
AvaloniaEdit's highlighting engine does not support CSS variables, so theme-switching does not
affect editor syntax colors. The preview pane (WebView) is theme-aware via `ThemeService.GetThemeCss()`.

---

## Avalonia 12.0.0 Upgrade (post Phase 2c)

### Final package versions

| Package | Version | Notes |
|---|---|---|
| Avalonia | 12.0.0 | |
| Avalonia.Desktop | 12.0.0 | |
| Avalonia.Themes.Fluent | 12.0.0 | |
| Avalonia.AvaloniaEdit | 12.0.0 | |
| Avalonia.Controls.WebView | 12.0.0 | |
| Avalonia.Diagnostics | 11.3.13 | No 12.0.0 release published yet; left at 11.3.13 (Debug-only) |
| FluentAvaloniaUI | 2.5.1 | Compatible with Avalonia 12.0.0 |
| HarfBuzzSharp (transitive) | 8.3.1.3 | Updated by Avalonia 12 dependency resolution |
| SkiaSharp (transitive) | 3.119.3-preview.1.1 | Updated by Avalonia 12 dependency resolution |
| Tmds.DBus.Protocol (transitive) | 0.90.3 | Updated from 0.21.2 by Avalonia 12 resolution |

### Breaking changes encountered

**`Window.SystemDecorations` renamed to `Window.WindowDecorations`** (`InlineEditWindow.cs`)
- Old: `SystemDecorations = SystemDecorations.None`
- New: `WindowDecorations = Avalonia.Controls.WindowDecorations.None`
- The property and type both renamed; fully-qualified type name required in constructor body to resolve the ambiguity between the property name and the type name.
- Fix: replaced with `WindowDecorations = Avalonia.Controls.WindowDecorations.None;`

### Tmds.DBus.Protocol vulnerability
- Transitive dependency updated from 0.21.2 → 0.90.3 as part of the Avalonia 12.0.0 resolution.
- The advisory GHSA-xrw6-gwf8-vvr9 still reports against 0.90.3. This is a Linux DBus protocol library; it is not used on Windows at runtime. No further action needed for a Windows-primary app.

### No other breaking changes observed
- NativeWebView API: no changes to `InvokeScript`, `NavigateToString`, `WebMessageReceived`, `NavigationCompleted`, `AdapterCreated`.
- AvaloniaEdit `TextEditor` API: no changes to the used surface.
- AXAML namespace and style keys: no changes required.
- All Phase 2 features verified to build cleanly after upgrade.

---

## Phase 2c Notes

### NativeWebView.NavigationCompleted
- Event exists on NativeWebView; handler signature: `EventHandler` (plain `EventArgs`, no typed args).
- Fires after every `NavigateToString()` call, including re-navigation on theme change.
- Used to inject all three JS listeners (scroll, click-sync, dblclick inline-edit) via a single `InvokeScript` call.

### Scroll JS moved from HTML to NavigationCompleted
- Previously: scroll JS was embedded in every HTML page via `PreviewViewModel.BuildDocument()` (`<script>` in `<head>`).
- Phase 2c: removed from HTML; now injected via `NavigationCompleted` alongside click/dblclick listeners.
- `PreviewViewModel.BuildDocument()` no longer contains any `<script>` block.

### WebMessage dispatcher
- `OnWebViewMessage` now parses all messages as JSON.
- Dispatch on `msg.Type`: `"scroll"` → scroll sync, `"click-sync"` → editor cursor move, `"inline-edit"` → overlay.
- The old scroll message format (`{type:'scroll', y:..., h:...}`) is preserved; no regression.

### InlineEditWindow
- Implemented as programmatic `Window` (no AXAML file) in `Views/InlineEditWindow.cs`.
- Uses `AddHandler(KeyDownEvent, ..., RoutingStrategies.Tunnel)` to intercept Enter/Escape before TextBox.
- `Show(owner)` pattern (non-blocking); result delivered via `Committed` event.
- Static factory: `InlineEditWindow.TryOpen(owner, tag, markdown, onCommit)` — returns false for unsupported tags.
- Hard-coded GHS Dark colors per spec (`#181818` bg, `#333333` border, `#E8E8E8` text).

### Line ending normalization
- `FileService.OpenFile()` now normalizes `\r\n` and `\r` to `\n` on load.
- Ensures `ReplaceLines()` (which splits on `\n`) works consistently across platforms.

---

## macOS Build — Phase 1

**PASS**

Command:
```
dotnet publish src/GhsMarkdown.Cross/GhsMarkdown.Cross.csproj -r osx-arm64 -c Release --self-contained
```

Output: `bin/Release/net10.0/osx-arm64/publish/`

- Cross-compilation from Windows succeeded without errors.
- Runtime verification on macOS hardware deferred to Phase 10 (no macOS hardware available).
- Note: `Avalonia.Controls.WebView` on macOS will use the WKWebView backend (standard macOS WebKit).

---

## Post-Phase-7 Bug Fix Sessions (Sessions 1–8)

### Editor not interactive — root causes and fixes (all four required)

**1. Border Background absorbing pointer events**
Any Avalonia element with `Background` set (including solid colors)
becomes a hit-test target and blocks all pointer events from reaching
child controls. A wrapper Border around the TextEditor had
`Background="#141414"` added during a color fix — silently blocking
all clicks from reaching the editor.
Fix: remove Background from wrapper Border; set it directly on the
TextEditor element instead.
File: `Views/MainWindow.axaml`

**2. Command palette overlay IsHitTestVisible binding failure**
The palette ContentControl (ZIndex=100, full-window overlay) had
`IsHitTestVisible="{Binding CommandPalette.IsOpen}"`. During window
construction, before DataContext resolves, this binding fails and
IsHitTestVisible defaults to true — leaving a permanent invisible
full-window pointer blocker active.
Fix: hardcode `IsHitTestVisible="False"` on the wrapper ContentControl.
Add `IsHitTestVisible="True"` explicitly on CommandPaletteView so it
re-enables hit-testing for its own subtree when visible.
Files: `Views/MainWindow.axaml`, `Views/CommandPaletteView.axaml`

**3. NativeWebView (WebView2) native OS window overlay**
NativeWebView is a native OS HWND. Setting `IsVisible=false` on a
parent Border does NOT destroy the native OS window — the OS compositor
keeps it hit-testable, physically covering the editor column.
Fix: collapse preview and gutter columns to `GridLength(0)` in Edit
mode. Bind `IsVisible` directly on the NativeWebView element itself,
not on a parent wrapper.
File: `Views/MainWindow.axaml.cs`

View mode column widths (required for native WebView containment):
| Mode    | Editor Col | Gutter Col | Preview Col | WebView IsVisible |
|---------|-----------|-----------|------------|-------------------|
| Split   | ratio*    | 28px      | (1-ratio)* | true              |
| Edit    | 1*        | 0px       | 0px        | false             |
| Preview | 0px       | 0px       | 1*         | true              |

**4. AvaloniaEdit TextArea focus not activating on click**
In Avalonia 12 hosting configurations with no Background on parent
elements, AvaloniaEdit's TextArea does not automatically capture
focus on PointerPressed — no caret appears and no keyboard input
is processed.
Fix: explicitly call Focus() on pointer press:
`_editor.PointerPressed += (_, _) => _editor.Focus();`
File: `Views/MainWindow.axaml.cs`

---

### MarkdownSyntax.xshd — zero-length match exception (AvaloniaEdit 12.0.0 breaking change)

AvaloniaEdit 12.0.0 throws `InvalidOperationException: 'A highlighting
rule matched 0 characters'` when any regex pattern in an .xshd file
can match an empty string. This was silently ignored in 11.x.

The exception fires on first keystroke, killing all keyboard input.
All Rule patterns (H1–H6, Blockquote, Lists, HR) were rewritten to
require at least one non-whitespace character using `\s+\S.*`.

As a further mitigation, syntax highlighting loading was disabled
entirely (deferred to Phase 10). The `.xshd` file is retained but
not loaded at runtime.
Fix: comment out `HighlightingManager` registration in
`Views/MainWindow.axaml.cs`.
File: `Assets/MarkdownSyntax.xshd`, `Views/MainWindow.axaml.cs`

This is an AvaloniaEdit 12.0.0 breaking change — any .xshd migrated
from 11.x must be audited for zero-length match patterns before use.

Resolution (BL-12): All heading Rules replaced with Rule elements
that omit the ^ anchor and use `.+` to consume the rest of the line
(e.g. `######\s.+` instead of `^######\s+\S+`). Span-based patterns
with `<End>$</End>` or `<End>\n</End>` also failed — AvaloniaEdit 12
rejects zero-width End patterns including `$` and the implicit `$`
from `stopateol="true"`. The working approach: Rules without ^ anchors
where `.+` ensures at least one character is always consumed.
Blockquote, list, and HR patterns also converted to anchor-free Rules.

Final finding: Rule patterns with .+ pass character-by-character
typing but fail on paste and Document.Replace operations (used by
Ctrl+1-6 heading commands). Span patterns with \n End fail
immediately on load because AvaloniaEdit 12 injects $ as a fallback
into all End patterns, and $ is zero-width. Both approaches are
blocked by AvaloniaEdit 12 internal limitations. A custom
IHighlighter implementation is required. Deferred to v1.2.
Syntax highlighting remains disabled.

---

### AvaloniaEdit requires StyleInclude in App.axaml (Avalonia 12.0.0)

Without this StyleInclude, AvaloniaEdit's TextEditor has zero visual
children and a zero-size TextArea — editor appears blank and accepts
no input.

Required in `App.axaml` inside `<Application.Styles>`:
```xml
<StyleInclude Source="avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml" />
```

This was confirmed via a minimal isolation test project.

---

### Editor theme colors — programmatic override required

AvaloniaEdit's StyleInclude sets its own internal TextArea foreground
tuned for dark themes. Setting Background/Foreground attributes in AXAML
is insufficient — they must be set programmatically on theme change,
targeting both `_editor.Foreground` and `_editor.TextArea.Foreground`.

`ApplyEditorTheme(string themeName)` method added to
`MainWindow.axaml.cs`. Called on startup and on ThemeChanged event.

| Theme    | Background | Foreground |
|----------|-----------|-----------|
| GHS Ink  | #F9F6F0   | #1A1A1A   |
| All others | #141414 | #E8E8E8   |

---

### Preview scroll position lost on NavigateToString

Every parse cycle calls NavigateToString(), resetting WebView scroll
to top. Fixed by:
1. Capturing scroll position from incoming JS scroll sync messages
   (WebMessageReceived) into `_savedScrollY`.
2. Injecting an inline `<script>` into the HTML before </body> that
   calls `window.scrollTo()` on load to restore position.
File: `Views/MainWindow.axaml.cs`

---

### Command palette — root Panel wrapper breaks editor layout

Phase 5 wrapped the root DockPanel in a Panel for palette overlay
stacking. Avalonia's Panel does not enforce finite layout constraints,
causing the DockPanel to collapse to zero height.
Fix: changed root `<Panel>` to `<Grid>`. A single-cell Grid properly
constrains the DockPanel while supporting ZIndex overlay.
File: `Views/MainWindow.axaml`

---

### SnippetEditDialog deadlock (.Wait() on async ShowDialog)

Avalonia does not use nested dispatcher loops for modal dialogs —
it is purely async. Calling `.Wait()` on the UI thread deadlocks.
Fix: all dialog delegates changed to `async Task<T>` and awaited.
Files: `ViewModels/SnippetStudioViewModel.cs`,
       `ViewModels/TimelineViewModel.cs`,
       `Views/MainWindow.axaml.cs`

The `.Wait()` pattern must never be used for Avalonia modal dialogs.
Always use `await ShowDialog<T>()` or `await dialog.Open()`.

---

## BL-02 Known Issue — Typewriter Scroll

Typewriter scrolling (active line vertically centered during typing)
is not working. AvaloniaEdit's internal BringCaretToView fires after
all dispatcher-based and timer-based overrides, resetting scroll to
its own position. Setting MinimumDistanceToViewBorder=9999 also had
no effect. Root cause: AvaloniaEdit 12.0.0 may fire scroll correction
at a layout pass level that is not interceptable via public API.

Prose dimming (preview opacity on non-active blocks) works correctly.
Focus Mode is considered partially complete — dimming ships, typewriter
scroll deferred.

Possible future approach: subclass TextArea or TextView and override
the MakeVisible/BringIntoView virtual method directly.

---

## BL-05 Known Requirement — Jump List needs installer

Windows Jump List via SHAddToRecentDocs requires:
1. The .md file type associated with GhsMarkdown.Cross.exe
   (registered by the Inno Setup installer).
2. The app launched via that file association at least once.

Without the installer, SHAddToRecentDocs succeeds silently but
Windows does not surface files under the app's Jump List.
ICustomDestinationList (explicit COM Jump List) requires a
registered AppUserModelID — also set by the installer.

BL-05 is implemented correctly. Verify after running the Inno
Setup installer on a clean machine.

---

## Security — Tmds.DBus.Protocol transitive vulnerability

Avalonia.X11 12.0.0 → Avalonia.FreeDesktop → Tmds.DBus.Protocol 0.90.3
has a known vulnerability flagged by NuGet. This is a Linux-only
dependency (D-Bus IPC protocol used on FreeDesktop/X11 platforms).

Risk assessment: LOW for current distribution.
- App currently targets Windows only (win-x64 publish)
- Tmds.DBus.Protocol code path never executes on Windows
- Vulnerability only exploitable on Linux deployments

Resolution: Monitor Avalonia 12.x releases for a dependency update.
When BL-11 (Linux support) is implemented, revisit and force-upgrade
Tmds.DBus.Protocol to a patched version via direct PackageReference
before any Linux distribution.

---

## Build & Publish Commands

Run all commands from:
`C:\Users\mike\source\repos\MarkDown-CrossPlatform\GhsMarkdown.Cross\`

### Windows publish (self-contained)
```bash
dotnet publish GhsMarkdown.Cross/src/GhsMarkdown.Cross/GhsMarkdown.Cross.csproj -r win-x64 -c Release --self-contained
```
Output: `GhsMarkdown.Cross/src/GhsMarkdown.Cross/bin/Release/net10.0/win-x64/publish/`

### macOS publish (self-contained)
```bash
dotnet publish GhsMarkdown.Cross/src/GhsMarkdown.Cross/GhsMarkdown.Cross.csproj -r osx-arm64 -c Release --self-contained
```

### Inno Setup installer (Windows)
Open `installer/windows/setup.iss` in Inno Setup IDE and press Ctrl+F9.
Output: `installer/windows/output/GHSMarkdownEditor-Setup-1.0.0.exe`

---

## BL-20 — Editor Right-Click Context Menu

AvaloniaEdit's `TextArea` supports a `ContextMenu` property directly.
Assigned via `_editor.TextArea.ContextMenu = menu` in `AttachEditorContextMenu()`
called at the end of `InitializeEditor()`.

### Toggle items (Word Wrap, Show Line Numbers, Highlight Current Line)
- Require `ToggleType = MenuItemToggleType.CheckMark` to render a checkmark.
  Without this, `IsChecked` is set correctly but no visual indicator appears.
- `IsChecked` updated in `menu.Opening` event handler on each open.
- State persisted via three new `AppSettings` fields: `WordWrap` (default false),
  `ShowLineNumbers` (default true), `HighlightCurrentLine` (default true).
- Partial methods `OnWordWrapChanged`, `OnShowLineNumbersChanged`,
  `OnHighlightCurrentLineChanged` handle persistence using the `s with { }` pattern.

### Edit operations (Cut, Copy, Paste)
- Use `TopLevel.GetTopLevel(_editor)?.Clipboard` for clipboard access — do not
  use `Application.Current.Clipboard` which is deprecated in Avalonia 12.

### Inline Code vs Code Block
- The `</>` toolbar button was replaced with `{}` (see BL-17b).
- Inline code (Ctrl+`) is still registered in `CommandRegistry` and works via keyboard.

---

## BL-07 — Git Diff Margin

### GitDiffService
- Shells out to `git` CLI via `Process.Start` — no NuGet package required.
- `FindGit()` searches `PATH` environment variable first, then falls back to
  common Windows install locations (`C:\Program Files\Git\cmd\git.exe` etc.).
- Returns `null` silently if git is not found — the margin stays at 0px width.
- Arguments passed via `ProcessStartInfo.ArgumentList` (not a single arg string)
  to avoid quoting/escaping issues with file paths on Windows.
- Working directory set to the file's parent folder; filename passed without
  full path to avoid issues with `git diff HEAD -- <absolute-path>` on Windows.
- All exceptions silently caught — app is completely unaffected if git is
  unavailable or the file is outside a repo.

### GitDiffMargin
- Subclasses `AbstractMargin` from AvaloniaEdit.
- `MeasureOverride` returns `Size(0,0)` when diff dictionary is empty —
  the margin is invisible for clean files and non-repo files.
- Returns `Size(MarginWidth, 0)` (5px) only when diff data is present.
- Added to `_editor.TextArea.LeftMargins` in `InitializeEditor()`.
- `UpdateDiff()` + `InvalidateMeasure()` called on background task completion
  via `Dispatcher.UIThread.Post`.

### Diff state encoding
- Positive keys: 1-based line numbers → `Added` or `Modified`
- Negative keys: `-N` means deletion marker appears above line `N+1`
- Colors: green `#4E9E4E` = added, amber `#E0A030` = modified,
  red `#CC4444` = deleted (downward triangle)

### Refresh triggers
- `FileService.PropertyChanged` (CurrentFilePath) — on file open
- `FileService.FileSaved` — on manual save
- Initial call in `InitializeEditor()` for files open at startup

### Use case note
This feature is designed for writers and developers who version their documents
in git. A markdown file in a git repo will show line-level change indicators
identical to VS Code's gutter. Files outside a git repo show no margin — silent
and non-intrusive.

---

## BL-17 — InlineEditWindow Extended (Code Blocks + Tables)

Tags added to supported list: `pre`, `code`, `table`, `tr`, `td`, `th`.
`FriendlyElementType` extended to return "code block" and "table" for these tags.
`SizeToContent = SizeToContent.Height` applied to window — eliminates empty
space below pills and auto-fits to content.

Constructor accepts optional `tag` parameter to increase `MinHeight`/`MaxHeight`
for code block and table editing (wider content needs more vertical space).

`TryOpen` static factory updated to allow the new tags through.

---

## BL-17b — Code Language Picker (CodeLanguagePickerWindow)

New programmatic `Window` in `Views/CodeLanguagePickerWindow.cs`.
Matches the language picker design in the GHS Markdown Web and WPF companion apps.

### Design
- Search `TextBox` at top filters pills in real time via `TextChanged`.
- Language pills rendered as `Button` elements in a `WrapPanel` with
  `CornerRadius = 20` (pill shape) and `BorderBrush = #4A9EFF`.
- Selected language fires `LanguageSelected` event then closes.
- Escape key closes without selection.
- `SizeToContent = SizeToContent.Height` — window height auto-fits to pill rows.

### Language list (28 languages)
plaintext, csharp, javascript, typescript, python, html, css, sql, bash,
json, xml, yaml, markdown, powershell, rust, go, java, cpp, c, ruby, php,
swift, kotlin, r, scala, dockerfile, toml, ini, graphql

### Integration points
- `{}` toolbar button (replaced `</>`) → opens picker → calls `InsertCodeBlock(lang)`
- Right-click context menu → "Code Block..." → same picker
- Right-click context menu → "Change Language..." → same picker, calls
  `ChangeCodeLanguage(lang)` — only visible when caret is on a ` ``` ` fence line
- Double-click on a ` ```language ` line in editor → directly opens picker

### InsertCodeBlock / ChangeCodeLanguage
Both implemented as private methods in `MainWindow.axaml.cs`.
`InsertCodeBlock` positions caret inside the new fence after insertion.
`ChangeCodeLanguage` replaces the language token on the current fence line only.

---

## BL-21 — Light Theme Italic Visibility Fix

`MarkdownColorizingTransformer` constructor extended with `bool isLightTheme`
parameter. `ItalicBrush` uses `#555566` for light theme, `#D0D0D0` for dark.
Transformer is recreated on theme change via `ApplyMarkdownHighlighting()`.

---

## BL-22 — Pull Tab Chevron

Pull tab width increased from 10px to 16px.
Content replaced with a `Panel` containing two `TextBlock` elements:
- `‹` visible when `IsRightPanelOpen = true`
- `›` visible when `IsRightPanelOpen = false`
Both use accent color and `FontSize=16 FontWeight=Bold`.

---

## BL-23 — Custom Theme Reset to Light

`ResetCustomThemeToLight` RelayCommand added to `MainWindowViewModel`.
Uses GHS Light color values as baseline (cream/off-white palette).
Follows the same pattern as the existing `ResetCustomTheme` (reset to dark).
`CustomColorsReset` event raised to trigger ColorPicker refresh in SettingsView.

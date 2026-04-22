# SPEC.md — GHS Markdown Editor (Cross-Platform)
> Living specification for the GHS Markdown Editor Avalonia app.
> All phases must be fully verified against acceptance criteria before the next phase begins.
> This document is the single source of truth. CC prompts are derived from it.
> Version: 4.9 — BL-44 complete, v1.1.2.


## Project Identity

| Property | Value |
|---|---|
| App name | GHS Markdown Editor |
| Codename | GHS-MD-Cross |
| Platform | Windows, macOS (via Avalonia UI). Linux deferred to post-v1.0. |
| Companion apps | GHS Markdown Web (md.theheadfamily.com), GHS Markdown WPF |
| Version target | v1.1.2 |
| Repository | https://github.com/michaeldhead/MarkDown-CrossPlatform |

---

## Tech Stack

| Layer | Choice | Notes |
|---|---|---|
| Language | C# / .NET 10 | Forced upgrade from .NET 8 — FluentAvaloniaUI 2.5.1 requires net10.0. .NET 10 LTS expected late 2025. |
| UI Framework | Avalonia 12.0.0 | Upgraded from 11.3.12 post Phase 2c. Full ecosystem aligned on 12.0.0. |
| MVVM | CommunityToolkit.Mvvm 8.4.2 | Consistent with WPF companion app |
| Editor engine | Avalonia.AvaloniaEdit 12.0.0 | Upgraded from 11.4.1 post Phase 2c. Note: .xshd syntax highlighting uses hardcoded hex values — CSS variables not supported by the highlighting engine. Editor syntax colors are fixed to GHS Dark tokens regardless of active theme. See Architecture Notes. |
| Markdown parser | Markdig 0.44.0 | 0.x stable. 1.x skipped to avoid API churn. |
| Preview renderer | Avalonia.Controls.WebView 12.0.0 | Upgraded from 11.4.0 post Phase 2c. WebView2 backend on Windows, WKWebView on macOS. Control: `NativeWebView`. API: `NavigateToString()`, `InvokeScript()`, `WebMessageReceived`, `NavigationCompleted`. |
| Theming | Avalonia Styles + FluentAvaloniaUI 2.5.1 | Compatible with Avalonia 12.0.0. |
| DI container | Microsoft.Extensions.DependencyInjection | Standard .NET DI |
| Logging | Microsoft.Extensions.Logging + Serilog | File sink for diagnostics |
| Installer — Windows | Inno Setup | Consistent with WPF companion app |
| Installer — macOS | .app bundle + DMG via dotnet publish | Native macOS packaging |
| Installer — Linux | — | Deferred to post-v1.0. See backlog BL-11. |
| Living spec | This file (SPEC.md) | Updated before each phase begins |

---

## Design System

### Themes (shipped)

| Name | Base | Description |
|---|---|---|
| **GHS Dark** | Dark Chrome | Default. Near-black shell (#141414), GHS blue accent (#4A9EFF), subtle panel chrome. |
| **GHS Light** | Editorial Light | Cream/off-white (#F9F6F0), generous line spacing. Renamed from GHS Ink in BL-08. |

Auto mode maps GHS Dark ↔ GHS Light based on OS Appearance setting.

### Color Tokens (GHS Dark reference)

| Token | Value | Usage |
|---|---|---|
| `--bg-shell` | #141414 | App background |
| `--bg-panel` | #181818 | Left/right panels |
| `--bg-toolbar` | #1A1A1A | Toolbar, titlebar |
| `--bg-gutter` | #161616 | Smart Gutter, icon rail |
| `--bg-editor` | #141414 | Editor pane |
| `--bg-preview` | #161616 | Preview pane |
| `--accent` | #4A9EFF | Active states, highlights |
| `--accent-dim` | #1E3A5F | Active backgrounds |
| `--border` | #222222 | Panel borders |
| `--border-subtle` | #1E1E1E | Inner borders |
| `--text-primary` | #E8E8E8 | Body text |
| `--text-secondary` | #888888 | Muted text |
| `--text-hint` | #444444 | Hints, inactive |
| `--syntax-h1` | #4A9EFF | Heading 1 |
| `--syntax-h2` | #5AB865 | Heading 2 |
| `--syntax-h3` | #B8954A | Heading 3 |
| `--syntax-h4` | #888888 | Heading 4 (de-emphasized, same as text-secondary) |
| `--syntax-h5` | #666666 | Heading 5 |
| `--syntax-h6` | #555555 | Heading 6 |
| `--syntax-code` | #C792EA | Inline code, code blocks |
| `--active-block` | #0D1F35 | Active block background (editor + preview) |
| `--active-border` | #2A4A6A | Active block outline |

---

## Layout Architecture (Model C — Hybrid Slot-Based)

```
┌─────────────────────────────────────────────────────────────┐
│  Titlebar (38px) — App name | Tab strip (scrollable) | Controls  │
├────┬──────────────┬────────────────────────┬──────────┤ tab │
│Icon│  Left Panel  │  Editor │ Gutter │ Prev │ Right    │ ‹   │
│Rail│  (slot)      │  Pane   │ (28px) │ Pane │ Panel    │     │
│44px│  220px       │  flex-1 │        │ flex-1│ (slot)   │     │
│    │              │         │        │       │ 200px    │     │
├────┴──────────────┴────────────────────────┴──────────┴─────┤
│  Status Bar (26px) + Version Timeline scrubber              │
└─────────────────────────────────────────────────────────────┘
```

### Icon Rail (44px, always visible)
- Narrow vertical strip on far left.
- Icons: Topology, Files, Snippets, Search | (spacer) | Settings.
- Active icon expands the Left Panel slot.
- Clicking an active icon collapses the Left Panel.

### Tab Strip (in titlebar, always visible)
- Horizontally scrollable strip between the app name label and the view mode buttons.
- Each tab: filename label + dirty dot (blue = unsaved) + close button (×).
- Active tab has a 3px accent-color underline.
- New tab (+) button at the right end of the strip.
- Keyboard: Ctrl+T (new tab), Ctrl+W (close active), Ctrl+Tab (next tab).
- Middle-click on a tab closes it.
- Closing a tab with unsaved changes shows a Save / Don't Save / Cancel dialog.

### Left Panel Slot (220px, collapsible)
Default slot occupant: **Document Topology View**
Swappable to: File Browser, Snippet Studio, Recent Files, Search Results.
Slides in/out with a smooth 200ms ease animation, pushing the editor pane.

### Smart Gutter (28px, always visible in Split mode)
The visual and functional seam between editor and preview.
- Resize handle (drag to adjust editor/preview width ratio).
- Sync thread: vertical chain of dots indicating scroll sync state.
- Live word count for the active section.
- Color-coded: blue = synced, amber = drifted, green = saved.

### Right Panel Slot (200px, collapsible)
Default slot occupant: **Document Outline**
Swappable to: Snippet Studio, AI Assist, References, Settings.
Triggered by: slim pull tab on the right edge of the preview pane (10px × 44px, rounded left corners).
Slides in from the right, pushing the preview pane.

### Status Bar (26px, always visible)
Left to right: cursor position, word count, filename, theme badge, | (spacer) | Version Timeline scrubber.

---

## Core Features (all phases)

### F-01 — Editor (AvaloniaEdit)
- Full Markdown syntax highlighting (.xshd definition).
- Line numbers in gutter.
- Active line highlight.
- Soft wrap toggle.
- Tab = 2 spaces.
- Keyboard shortcuts: Bold (Ctrl+B), Italic (Ctrl+I), Code (Ctrl+`), Heading H1–H6 (Ctrl+1–6), Link (Ctrl+K), HR (Ctrl+Shift+H), View: Edit Only (Ctrl+Shift+E), View: Split (Ctrl+Shift+T), View: Preview (Ctrl+Shift+W), Save As (Ctrl+Shift+S).

### F-02 — Preview (WebViewControl)
- Renders Markdig-parsed HTML with injected GHS CSS theme.
- Active block highlight: blue outline border on the element matching the editor cursor's section.
- Single-click preview → syncs editor cursor to that block.
- Double-click preview → enters inline edit mode for paragraphs, headings, list items, blockquotes (Phase 2c).
  Mechanics: double-click fires a JS message from WebView identifying the element's `data-source-line` attribute.
  SourceMappingService resolves the source line range. Editor scrolls to and selects that range.
  A floating edit overlay (Avalonia Popup) appears over the preview element showing the raw Markdown.
  On commit (Enter/blur), the overlay content replaces the source range, editor updates, preview re-renders.
  On cancel (Escape), overlay dismisses with no changes.
- Theme CSS hot-swapped on theme change (no flicker).

### F-03 — Split View
- Side-by-side editor and preview with Smart Gutter between.
- Synchronized scroll: editor scrolling moves preview proportionally and vice versa.
- Draggable gutter to adjust split ratio (persisted to user settings).
- Three view modes: Edit only, Split (default), Preview only.
- Each tab maintains its own independent editor scroll position, preview scroll position, caret line, and active block highlight. Switching tabs restores each tab's last position.

### F-04 — Smart Gutter
- 28px strip between editor and preview in Split mode.
- Drag to resize split ratio.
- Vertical sync thread: dots indicating locked scroll positions.
- Word count for the active section (updates on cursor move).
- Visual state: blue (synced), amber (manual scroll drift), green (recently saved).

### F-05 — Document Topology View
- Left panel slot occupant.
- Tree of all headings (H1–H6) rendered as a navigable node list.
- Color-coded by level: H1 blue, H2 green, H3 amber, H4–H6 progressively de-emphasized gray.
- Active section highlighted and auto-scrolled into view.
- Click a node → jumps editor cursor + preview scroll to that section.
- Section balance mini-chart at the bottom: bar per top-level section proportional to its word count.

### F-06 — Document Outline (Right Panel)
- Right panel slot occupant.
- Flat list of all headings, indented by level.
- Active heading highlighted blue.
- Click → jumps editor and preview to that heading.
- Slot switcher at the bottom of the panel: Outline / Snippets / AI Assist.

### F-07 — Contextual Command Palette (Ctrl+P)
- Full-screen overlay with fuzzy search input.
- Command categories: Navigation, Actions, Editor, Settings.
- Keyboard navigation: arrow keys + Enter. Escape closes.
- Every app action is registered as a command via CommandRegistry.
- Recent commands (last 10) shown when input is empty.

### F-08 — Snippet Studio
- Left or right panel slot occupant.
- Snippets are parameterized templates with named tab-stop fields.
- Insert snippet → fields highlighted in editor, Tab cycles through them.
- Snippet management: add, edit, delete, categorize.
- Snippets persisted to user data directory (JSON).

### F-09 — Version Timeline
- Status bar scrubber (horizontal track + thumb).
- Auto-save creates a snapshot every 2 minutes and on every explicit save.
- Drag timeline thumb left → previews that version in the preview pane (editor unchanged).
- Release thumb → optionally restore that version (confirmation dialog).
- Snapshots retained for 7 days, max 200 per file.
- Stored in user data directory alongside the file.

### F-10 — Live Export Preview Panel
- Triggered from toolbar or Command Palette: "Export…"
- A third panel slides in from the right (full height), pushing the right panel if open.
- Shows a rendered preview of the export output.
- Format selector: PDF, DOCX, HTML Styled, HTML Clean, Plain Text.
- Export button commits the export to disk with a save dialog.
- Panel dismisses after export or via Escape.

### F-11 — Export Formats
- PDF (via headless WebView print)
- DOCX (via Open XML SDK or DocX library)
- HTML Styled (injected GHS CSS)
- HTML Clean (no styles)
- Plain Text (strip all Markdown syntax)

### F-12 — Auto-Save + Draft Restore
- Auto-save every 60 seconds to a `.draft` sidecar file (FileService). Configurable interval.
- On open, if a draft exists newer than the saved file, prompt to restore or discard.
- Draft discarded on explicit save.
- Note: separate system from Version Timeline snapshots — see Architecture Notes.

### F-13 — Settings
- Theme: GHS Dark / GHS Light / GHS Custom / Auto.
- Font: editor font family + size.
- Split ratio default.
- Auto-save interval.
- Snippet library path.
- Keyboard shortcut reference (read-only display).
- Persisted to user data directory (JSON).

### F-14 — File Operations
- New file, Open file, Save, Save As — standard dialogs.
- Recent files list (persisted, max 20).
- Default `.md` file type association on install (Windows + macOS only).
- Drag and drop `.md` files onto the app window to open.

---

## Backlog (Post v1.0.0)

| ID | Feature | Notes | Status |
|---|---|---|---|
| BL-01 | AI Assist panel (right slot) | Claude-powered writing assistant. Anthropic API with user-supplied API key. Streaming responses. | ✅ Complete |
| BL-02 | Typewriter + Focus Mode | Active line centered, prose fades to 20% opacity | 🔄 Partial — prose dimming done, typewriter scroll deferred v1.2 |
| BL-03 | Dual monitor detached preview | Preview as a separate detachable window | ✅ Complete |
| BL-04 | Tab-based multi-file editing | Multiple open documents in tab bar | ✅ Complete |
| BL-05 | Jump List recent files (Windows) | Windows taskbar Jump List integration | 🔄 Partial — requires Inno Setup installer |
| BL-06 | Print to printer | Native OS print dialog | ✅ Complete |
| BL-07 | Git diff view (gutter) | Show unsaved changes vs last commit in editor gutter | ✅ Complete |
| BL-08 | GHS Glass theme refinement | Full blur/depth pass once base platform is stable | ✅ Complete |
| BL-09 | Custom theme editor | User-defined color tokens | ✅ Complete |
| BL-10 | Web app feature parity audit | Systematic comparison against md.theheadfamily.com | 📋 Planned |
| BL-11 | Linux support | Deferred from v1.0. AppImage distribution. File association via .desktop file (GNOME/KDE). Re-evaluate for v1.1. | 📋 Planned — spike required |
| BL-12 | Per-theme editor syntax colors | AvaloniaEdit .xshd uses hardcoded GHS Dark hex values. Post-v1.0: load different .xshd per active theme so editor syntax colors match GHS Ink / GHS Glass. | ✅ Complete |
| BL-13 | Tab-based multi-file editing | Multiple .md files open simultaneously with a tab bar above the editor. | ✅ Complete — see BL-04 |
| BL-14 | Security: upgrade System.IO.Packaging | Upgrade DocumentFormat.OpenXml to patched version. | ✅ Complete |
| BL-15 | Formatting toolbar | Icon-based formatting toolbar below main toolbar. Show/hide toggle (Ctrl+Shift+B). | ✅ Complete |
| BL-16 | Preview selection → editor formatting | Ctrl+B/I etc. on preview selection syncs editor cursor first. | ✅ Complete |
| BL-17 | Preview WYSIWYG editing | Inline edit overlay extended toward full bidirectional editing. | ✅ Complete |
| BL-17b | Code block language picker | {} toolbar button and right-click menu open searchable language picker (28 languages). | ✅ Complete |
| BL-18 | User Guide (Word document) | Comprehensive user guide, .docx format, image placeholders. | ✅ Complete |
| BL-19 | GHS Dark theme refinement | Lighten toolbar, icon rail, panel backgrounds for more visual depth. | ✅ Complete |
| BL-20 | Editor right-click context menu | Formatting, editor toggles, edit, file save. WordWrap/ShowLineNumbers/HighlightCurrentLine persisted. | ✅ Complete |
| BL-21 | Light theme italic visibility | Italic foreground theme-aware in MarkdownColorizingTransformer. | ✅ Complete |
| BL-22 | Right panel pull tab | Chevron ‹/› indicating open/close direction, accent color. | ✅ Complete |
| BL-23 | Custom theme — Reset to Light Mode button | Second reset button for GHS Light baseline values. | ✅ Complete |
| BL-24 | Custom theme — Reset to Light bug fix | All 10 UI chrome tokens correctly reset to GHS Light values. Bundle with BL-25. | ✅ Complete |
| BL-25 | Custom theme — user-configurable Markdown syntax colors | Syntax color pickers (H1–H6, Bold/Italic, Code, Blockquote) in GHS Custom theme editor. Bundle with BL-24. | ✅ Complete |
| BL-26 | Click-to-sync broken inside tables | `SourceLineRenderer` injects `data-source-line` on `<table>` elements. JS click handler walks up past td/th/tr. | ✅ Complete |
| BL-27 | Draggable right panel divider | 6px drag handle on left edge of right panel. 150px min, half-window max. `RightPanelOpenWidth` persisted. | ✅ Complete |
| BL-28 | Icon rail — first two icons wrong color | Formatting toolbar New/Open buttons used color-bitmap emoji glyphs. Replaced with monochrome BMP characters. | ✅ Complete |
| BL-29 | Scroll sync drift on long/complex documents | Anchor-based scroll sync replaces proportional. Falls back to proportional when no anchor exists. | ✅ Complete |
| BL-30 | About dialog | Command Palette "About GHS Markdown Editor" entry. Programmatic borderless Window. Version from assembly. "A Head & CC Production · Mike & The Machine · 2026". | ✅ Complete |
| BL-31 | Find + Replace bar | Inline Find/Replace bar below main toolbar. Ctrl+F / Ctrl+H. Case-sensitive (Aa), whole-word (ab), regex (.*) toggles, Replace/All buttons, prev/next arrows, × close, "No matches found" state. Implemented in prior development session — verified working. | ✅ Complete |
| BL-32 | Command Palette clipped by NativeWebView | `CollapsePreviewForPalette()` collapses gutter + preview columns on palette open. `UpdateCenterLayout()` restores on close. | ✅ Complete |
| BL-33 | Dirty dot persists after save | `HasUnsavedChanges` converted to flag-based `[ObservableProperty]`. `_suppressDirtyTracking` guard on file load. | ✅ Complete |
| BL-34 | File association opens duplicate tab | `OpenFileInTab(path)` helper checks existing tabs before creating new. IPC + CLI handlers route through it. | ✅ Complete |
| BL-35 | Command Palette unreadable in GHS Light theme | TextBox four template-part overrides. `palette-backdrop` split: Dark 55%, Light 35%. | ✅ Complete |
| BL-36 | App stays in background / minimized on file double-click | `BringToForeground()` helper: restore WindowState, Activate(), Topmost toggle. Applied to IPC + CLI handlers. | ✅ Complete |
| BL-37 | Tab close button hidden on long filenames | Tab content `StackPanel` → `Grid ColumnDefinitions="Auto,*,Auto"`. `TextTrimming="CharacterEllipsis"`. Tooltip shows full name. | ✅ Complete |
| BL-38 | Save As discoverability | Ctrl+Shift+S and Command Palette both work. Added "Save As…" to editor right-click context menu alongside existing Save item. | ✅ Complete |
| BL-39 | Draft file created when no changes made | Draft timer guard: `&& ActiveTab.FileService.HasUnsavedChanges` added to `WriteDraft()` condition. | ✅ Complete |
| BL-40 | Export panel uses stale tab reference | Export always uses first tab's content. `ExportPanelViewModel` captured initial tab's `EditorViewModel`/`FileService` at construction. Fix: converted `_parsingService` and `_editorVm` from `readonly` to mutable. Added `RewireDocumentSource(EditorViewModel, MarkdownParsingService)` with named `OnParsingServicePropertyChanged` handler (unsub old, sub new). Called from `MainWindowViewModel.NotifyTabActivated()` alongside topology/outline rewiring. No constructor-level call needed — initial DI injection already correct. | ✅ Complete |
| BL-43 | DOCX export missing tables | Tables in Markdown were silently skipped — `WriteBlock()` had no case for `Markdig.Extensions.Tables.Table`. Fix: added table case to `WriteBlock()` in `ExportService.cs`. Builds OpenXml `Table` with full borders, header row shading (blue `2F5496` fill, white bold text), body cell content via `WriteInlines()`, and a trailing empty `Paragraph` to prevent Word merge issues. Pipeline already included table support via `UseAdvancedExtensions()` — no change needed. **Namespace note:** `TableRow` and `TableCell` exist in both `Markdig.Extensions.Tables` and `DocumentFormat.OpenXml.Wordprocessing` — fully qualify both pattern matches and constructors to avoid CS0104 ambiguity. | ✅ Complete |
| BL-44 | Export suggested filename uses first tab instead of active tab | `ShowSaveDialogFunc` in `OnDataContextChanged` captured `_fileService` and `_editorVm` as locals at wiring time. On tab switch the live fields update but the captured locals do not. Fix: removed captured locals `fileSvcRef` and `editorVmRef2`; `GetSuggestedExportName()` now called with `_fileService` and `_editorVm` directly. | ✅ Complete |
| BL-41 | Global key commands bleed across all tabs | Single shared `CenteringTextEditor`. Three related bugs fixed across three sessions: (1) **Selection bleed** — `Ctrl+A` selection remaps through `Document.Text` replacement covering entire new document. Fix: `_editor.TextArea.ClearSelection()` after caret restore in `SwitchActiveTab()`. (2) **Scroll bleed** — switching from longer to shorter tab clamps scroll to end of shorter document. Fix: deferred scroll/caret/selection restore via `Dispatcher.UIThread.Post(..., DispatcherPriority.Loaded)` so `DocumentHeight` reflects new content before offset is applied. `SuppressMakeVisible = true` set synchronously before deferred callback to block intermediate layout interference. `_isSyncingScroll` stays true until deferred callback completes. (3) **Undo bleed** — `UndoStack.ClearAll()` on tab switch (see BL-42 for undo history loss follow-up). | ✅ Complete (undo history loss tracked in BL-42) |
| BL-42 | Undo history lost on tab switch | `UndoStack.ClearAll()` (BL-41 fix) severed undo history at tab boundaries. Fix: each `TabViewModel` owns its own `TextDocument` instance. `SwitchActiveTab()` swaps `_editor.Document` to the incoming tab's document rather than setting `Document.Text`. `UndoStack.ClearAll()` removed — each document carries its own stack. `OnEditorDocumentTextChanged` extracted from anonymous lambda to named method for unsub/resub during document swap. Initial tab document wiring required: `_editor.Document = initialTab.Document` added at end of `InitializeEditor()` before text load and handler subscription — without it, AvaloniaEdit's default `TextDocument` would own the initial tab's undo history and lose it on first switch. | ✅ Complete |

---

## Known Issues / Constraints

**Syntax highlighting disabled (Phase 10 item)**
AvaloniaEdit 12.0.0 throws InvalidOperationException on .xshd patterns
that can match zero characters. `MarkdownSyntax.xshd` loading is
disabled at runtime pending a full pattern audit in Phase 10.
The editor operates as plain text with no syntax colors until then.
Tracked: BL-12 (extended scope — xshd fix + per-theme colors).

**AvaloniaEdit StyleInclude required**
`App.axaml` must include:
`<StyleInclude Source="avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml" />`
Without it, the TextEditor has no visual children and accepts no input.

**Editor theme colors set programmatically**
AvaloniaEdit's internal styles override AXAML Background/Foreground.
Editor colors must be applied via `ApplyEditorTheme()` in
`MainWindow.axaml.cs` on startup and ThemeChanged.

**NativeWebView column collapsing required**
IsVisible=false on a parent element does not hide the native WebView2
OS window. Columns must be set to GridLength(0) in Edit and Preview
modes. See NOTES.md for the full column width table.

**NativeWebView column isolation requirement**
Any layout containing a NativeWebView alongside other Avalonia controls must place the NativeWebView in its own Grid column or row that shares no screen space with those controls. NativeWebView is a native OS HWND — it renders above all managed Avalonia UI at the OS compositor level regardless of ZIndex, DockPanel order, or IsVisible state on parent elements. This applies to the main preview pane (editor/preview split) and the Export Panel (format buttons/WebView split). Use `GridLength(0)` column collapsing, not `IsVisible=false`, to remove a NativeWebView from screen space.

**ContentDialog not usable**
`FluentAvalonia.UI.Controls.ContentDialog` throws `TypeLoadException` at runtime in Avalonia 12.0.0. Use plain programmatic Avalonia `Window` instances for all modal dialogs.

**`TextBox.PlaceholderText`** (not `.Watermark`) — Avalonia 12 API.

**`Window.WindowDecorations`** (not `.SystemDecorations`) — Avalonia 12 breaking change.

---

## Architecture Notes

### Project Structure
```
GhsMarkdown.Cross/
├── GhsMarkdown.Cross.slnx
├── src/
│   ├── GhsMarkdown.Cross/
│   │   ├── App.axaml
│   │   ├── App.axaml.cs
│   │   ├── Assets/
│   │   ├── Controls/
│   │   ├── Models/
│   │   ├── Services/
│   │   ├── ViewModels/
│   │   ├── Views/
│   │   └── Themes/                  # GhsDark.axaml, GhsLight.axaml, GhsCustom.axaml
├── installer/
│   ├── windows/
│   └── macos/
└── docs/
    └── SPEC.md
```

### Key Service Contracts

**ThemeService**
- `CurrentTheme` (observable property)
- `SetTheme(GhsTheme theme)`
- `GetThemeCss() → string`
- Raises `ThemeChanged` event
- No dependency on any WebView type

**MarkdownParsingService**
- `ParsedDocument` (observable — `MarkdownDocument`)
- `RawText` (settable — triggers re-parse with 300ms debounce)
- `Parse(string markdown) → MarkdownDocument`
- Parsing on background thread; `ParsedDocument` updated on dispatcher
- Do not inline Markdig calls in ViewModels

**SourceMappingService**
- `Rebuild(MarkdownDocument doc)`
- `GetElementSelector(int sourceLine) → string?`
- `GetSourceRange(string elementSelector) → (int start, int end)?`

**FileService**
- `CurrentFilePath` (observable — nullable string)
- `HasUnsavedChanges` (observable — flag-based, not computed)
- `NewFile()`, `OpenFile()`, `SaveFile()`, `SaveFileAs()`
- `GetRecentFiles()`, `AddToRecent()`
- `GetDraftPath()`, `WriteDraft()`, `DeleteDraft()`
- `FileSaved` event, `DraftFound` event, `RecentFilesChanged` event
- `_suppressDirtyTracking` guard on programmatic document loads

**CommandRegistry**
- `Register(CommandDescriptor cmd)`
- `Search(string query) → IEnumerable<CommandDescriptor>`
- `Execute(string commandId)`
- `GetRecent(int count) → IEnumerable<CommandDescriptor>`
- `CommandDescriptor.Title` (not `Label`) — renamed Phase 5
- `CommandDescriptor.DisplayHint => Hint ?? KeyboardShortcut` — added BL-30

**SnapshotService**
- `SaveSnapshot()`, `GetSnapshots()`, `Prune()`, `RestoreSnapshot()`
- All I/O on background thread via `Task.Run`

**SettingsService**
- `Load() → AppSettings`, `Save(AppSettings settings)`
- `%AppData%/GHSMarkdownEditor/settings.json` (Windows)
- `~/.config/GHSMarkdownEditor/settings.json` (macOS)

**PanelSlotViewModel**
- `ActiveOccupant` (observable — `ViewModelBase`)
- `SwapOccupant(ViewModelBase occupant)`
- Two instances: `LeftPanelSlot` and `RightPanelSlot` as direct properties on `MainWindowViewModel`
- Do NOT register as DI singleton

### AppSettings Fields (current)

```csharp
SplitRatio, ViewMode, Theme, EditorFontFamily, EditorFontSize,
AutoSaveIntervalSeconds, SnippetLibraryPath, FocusMode, RecentFiles,
LeftPanelOpen, ActiveIcon, ShowFormattingToolbar, AnthropicApiKey,
CustomThemeColors (Dictionary<string,string>), DetachedPreviewX,
DetachedPreviewY, DetachedPreviewWidth, DetachedPreviewHeight,
WordWrap, ShowLineNumbers, HighlightCurrentLine,
OpenTabPaths (List<string?>), ActiveTabIndex, MaxRestoredTabs,
RightPanelOpenWidth
```

### Tab Architecture (BL-04/13)

```
MainWindowViewModel
  ├── ObservableCollection<TabViewModel> Tabs
  ├── TabViewModel ActiveTab
  └── all editor/preview bindings route through ActiveTab

TabViewModel (per-tab, owns):
  ├── EditorViewModel
  ├── PreviewViewModel
  ├── FileService
  ├── MarkdownParsingService
  └── SourceMappingService

Shared singletons (unchanged):
  ThemeService, SettingsService, SnippetService, CommandRegistry,
  SnapshotService, AiAssistViewModel, TopologyViewModel, OutlineViewModel
```

**TabViewModel key properties:**
- `DisplayName` (computed) — filename without extension, or "Untitled"
- `IsDirty` (computed) — delegates to `FileService.HasUnsavedChanges`
- `IsActive` — true when this tab is the active tab
- Per-tab scroll state: `SavedScrollY`, `SavedAnchorLine`, `SavedEditorScrollY`, `SavedCaretLine`

**DI pre-construction pattern:** Per-tab services pre-constructed in `App.axaml.cs` before DI container built. New tabs construct fresh instances outside DI entirely.

**Critical tab-switch findings (T2B):**
- `ScrollToVerticalOffset()` has no effect after `Document.Text` replacement — use `((IScrollable)textView).Offset = new Vector(x, y)`
- Setting caret outside viewport triggers `BringCaretToView` which overrides scroll
- `_tabSwitchInProgress` flag (500ms) blocks scroll sync during tab transitions
- `SuppressMakeVisible` on `CenteringTextView` blocks AvaloniaEdit's internal `MakeVisible` during document replacement

### FileService Dirty Tracking (BL-33)

`HasUnsavedChanges` is flag-based (`[ObservableProperty]`), not computed from string comparison.
- Set `true`: any `EditorViewModel.DocumentText` change
- Set `false`: after successful save; after file load
- `_suppressDirtyTracking` wraps programmatic `_editor.DocumentText = …` during load

### Source Mapping Strategy

`SourceLineRenderer` (Markdig extension) injects `data-source-line` and `data-source-end` on all block-level HTML elements. `SourceMappingService` maps these to/from editor line numbers.

Used by: active block highlight, click-to-sync, inline edit, topology navigation.

### Auto-Save: Two Distinct Systems

| System | Service | Interval | Storage | Purpose |
|---|---|---|---|---|
| Draft sidecar | FileService | 60s (configurable) | `{filename}.draft` | Crash recovery — discarded on explicit save |
| Version snapshot | SnapshotService | 120s + on manual save | User data directory | Timeline history — 7 days / max 200 |

Draft timer guard (BL-39): only writes draft when `ActiveTab.FileService.HasUnsavedChanges == true`.

### Singleton Enforcement and IPC

Named mutex `Global\GHSMarkdownEditor` enforces single instance on Windows. Second launch sends file paths over named pipe `\\.\pipe\GHSMarkdownEditor` then exits. Running instance opens each path in a new tab via `OpenFileInTab(path)` helper (BL-34). macOS IPC deferred to BL-11.

### WebView JS Message Channel

All JS→C# messages via `window.chrome.webview.postMessage()` → `WebMessageReceived`. Dispatcher handles:
- `{"type":"scroll", ...}` → scroll sync
- `{"type":"click-sync", "sourceLine":N}` → editor cursor
- `{"type":"inline-edit", ...}` → inline edit overlay
- `{"type":"shortcut", ...}` → formatting commands from preview

### Command Palette — NativeWebView Isolation (BL-32)

`CollapsePreviewForPalette()` collapses gutter + preview columns to `GridLength(0)` and hides WebView when palette opens. `UpdateCenterLayout()` restores on close. Separate method from `UpdateCenterLayout()` — palette collapse does not touch `CurrentViewMode`.

### Threading Model
- All UI updates on Avalonia dispatcher thread.
- Markdig parsing on background thread, result marshalled to dispatcher.
- WebView HTML injection on dispatcher.
- Snapshot I/O on background thread via `Task.Run`.
- Auto-save timers fire on thread pool, marshal to dispatcher for content capture.
- SourceMappingService rebuild on background thread after parse.

---

## Definition of Done (per phase)

A phase is done when:
1. All acceptance criteria are checked off.
2. App builds without warnings on the primary development platform.
3. No regressions in previously completed phases.
4. SPEC.md updated with any spec changes discovered during implementation.
5. CC session closed; next phase prompt drafted before new CC session opened.

---

*Version: 4.9*
*Phase 1 through Phase 10 — ✅ COMPLETE. v1.0.0, v1.1.0, v1.1.1, and v1.1.2 shipped.*
*Last updated: BL-44 complete — export suggested filename now uses active tab. v1.1.2 released.*
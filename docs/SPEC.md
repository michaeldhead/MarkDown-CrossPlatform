# SPEC.md — GHS Markdown Editor (Cross-Platform)
> Living specification for the GHS Markdown Editor Avalonia app.
> All phases must be fully verified against acceptance criteria before the next phase begins.
> This document is the single source of truth. CC prompts are derived from it.
> Version: 2.9 — BL-24 through BL-29 completed. All planned backlog items complete.

mike


## Project Identity

| Property | Value |
|---|---|
| App name | GHS Markdown Editor |
| Codename | GHS-MD-Cross |
| Platform | Windows, macOS (via Avalonia UI). Linux deferred to post-v1.0. |
| Companion apps | GHS Markdown Web (md.theheadfamily.com), GHS Markdown WPF |
| Version target | v1.0.0 |
| Repository | TBD — create on GitHub before Phase 1 |

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
│  Titlebar (38px)                                            │
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
- Left edge has a 6px drag handle for width resizing. Width persisted to settings
  (default 200px, minimum 150px, maximum half window width).

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
- Synchronized scroll: anchor-based sync — editor scroll finds the nearest mapped
  block and calls scrollIntoView on the preview; preview scroll reports the topmost
  visible data-source-line element as an anchor and scrolls the editor to that line.
  Proportional fallback applies when no anchors exist in the visible area.
- Draggable gutter to adjust split ratio (persisted to user settings).
- Three view modes: Edit only, Split (default), Preview only.

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
- PDF (via headless WebView print — requires WebView spike validation in Phase 1)
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
- Theme: GHS Dark / GHS Ink / GHS Glass / Auto.
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

## Phased Delivery Plan

---

### Phase 1 — Project Scaffold + Shell

**Entry State:** Empty repository. No code exists.

**Goal:** Runnable app with correct layout, theming infrastructure, WebView spike, and empty panel slots.

**Scope:**
- Avalonia 11.3.12 + .NET 10 + FluentAvaloniaUI 2.5.1 scaffold. Versions locked — do not change.
- MainWindow with full layout: titlebar, icon rail, left panel slot, center (editor placeholder), smart gutter placeholder, preview placeholder, right panel (pull tab), status bar.
- ThemeService: GHS Dark, GHS Ink, GHS Glass, Auto. `GetThemeCss() → string`. Raises `ThemeChanged` event. No WebView dependency.
- Icon rail: icons render, active state, left panel slot toggle animation (200ms ease).
- Right panel pull tab: renders, click toggles right panel open/close (200ms ease).
- Status bar: static placeholder text.
- Settings panel (basic): theme selector only.
- CommandRegistry: scaffolded in DI with registration API. No palette UI yet.
- FileService: registered in DI as stub. No implementation yet.
- **WebView spike:** COMPLETE — PASS. `Avalonia.Controls.WebView 11.4.0`, control `NativeWebView`, `NavigateToString()` API, WebView2 backend on Windows. Result documented in `docs/NOTES.md`.
- App builds and runs on Windows.

**Acceptance Criteria:**
- [ ] App launches without errors on Windows.
- [ ] All three themes apply correctly and hot-swap without restart.
- [ ] Auto theme follows OS appearance setting.
- [ ] Left panel opens/closes via icon rail with 200ms ease animation.
- [ ] Right panel opens/closes via pull tab with 200ms ease animation.
- [ ] Smart Gutter placeholder renders at 28px between center panes.
- [ ] Status bar renders across the full bottom.
- [ ] CommandRegistry is registered in DI and accepts command registrations without error.
- [ ] WebView spike: static HTML renders in preview pane placeholder on Windows.
- [ ] WebView spike result documented (pass/fail + package version used).
- [ ] App builds for macOS via `dotnet publish` without errors. Runtime verification deferred to Phase 10 if macOS hardware unavailable.

**Exit State:** ✅ COMPLETE
- `GhsMarkdown.Cross.slnx` with correct project structure.
- MainWindow renders full layout shell.
- ThemeService functional with all three themes.
- CommandRegistry registered in DI (stub).
- FileService registered in DI (stub).
- WebView spike: PASS — `Avalonia.Controls.WebView 11.4.0`. Documented in `docs/NOTES.md`.
- App builds and runs on Windows. macOS `dotnet publish` PASS (runtime deferred to Phase 10).

---

### Phase 2a — Editor + Markdown Parsing + Static Preview

**Entry State:** Phase 1 complete. Shell renders. ThemeService, CommandRegistry (stub), FileService (stub) in DI. `Avalonia.Controls.WebView 11.4.0` confirmed — `NativeWebView` control, `NavigateToString()` API. No editor or preview content.

**Goal:** Working editor with syntax highlighting, Markdig parsing service, and static WebView preview. Full editor→parse→render pipeline validated on Windows.

**Scope:**
- AvaloniaEdit integrated with Markdown .xshd syntax highlighting (H1–H6, bold, italic, inline code, code blocks, lists, blockquotes, links, images).
- Line numbers, active line highlight, soft wrap toggle.
- MarkdownParsingService: debounced parse (300ms), observable `ParsedDocument`. Parsing on background thread, result marshalled to dispatcher.
- SourceLineRenderer (Markdig extension): injects `data-source-line="{startLine}"` and `data-source-end="{endLine}"` on all block-level HTML elements using Markdig `SourceSpan`.
- WebView integrated in preview pane. Renders `ParsedDocument` HTML with injected GHS CSS for all three themes.
- ThemeService wired: `GetThemeCss()` injected into WebView on `ThemeChanged` — no flicker, no reload.
- FileService fully implemented: New, Open, Save, Save As with native dialogs on Windows.
- View mode toggle: Edit / Split / Preview (toolbar — no scroll sync yet).
- Register file operation commands in CommandRegistry.

**Acceptance Criteria:**
- [ ] AvaloniaEdit renders Markdown syntax highlighting: H1–H6, bold, italic, inline code, code blocks, lists, blockquotes, links.
- [ ] MarkdownParsingService re-parses within 300ms of editor change.
- [ ] `data-source-line` and `data-source-end` attributes present on all block elements in rendered HTML (verify via WebView devtools or instrumented test).
- [ ] WebView renders Markdig HTML output correctly on Windows.
- [ ] WebView renders Markdig HTML output correctly on macOS (if hardware available).
- [ ] Theme change injects new CSS into WebView without full reload or flicker.
- [ ] New, Open, Save, Save As work with native dialogs on Windows.
- [ ] View mode toggle switches between Edit, Split, Preview correctly.

**Exit State:** ✅ COMPLETE
- AvaloniaEdit 11.4.1 integrated with Markdown .xshd syntax highlighting.
- MarkdownParsingService operational. SourceLineRenderer injecting `data-source-line` / `data-source-end`.
- NativeWebView rendering live Markdig 0.44.0 preview with GHS CSS.
- Theme change updates WebView CSS without flicker.
- FileService fully implemented for New, Open, Save, Save As.
- View mode toggle functional with keyboard shortcuts (Ctrl+1/2/3).
- Window title reflects current file state.

---

### Phase 2b — Split View, Smart Gutter, Synchronized Scroll

**Entry State:** Phase 2a complete. Editor, parsing, and preview pipeline functional. View modes work. No scroll sync or gutter behavior yet.

**Goal:** Fully functional split view with Smart Gutter and bidirectional scroll sync.

**Scope:**
- Smart Gutter: drag to resize split ratio (persisted to user settings JSON).
- Synchronized scroll: editor scroll → preview scroll proportionally, and vice versa. No feedback loops.
- Sync thread dots in gutter: blue (synced), amber (drift), green (saved).
- Live word count for active section in gutter (updates on cursor move).
- Split ratio persists across sessions.

**Acceptance Criteria:**
- [ ] Dragging gutter adjusts editor/preview split ratio.
- [ ] Split ratio persists across app restarts.
- [ ] Scrolling editor to bottom scrolls preview to bottom; scrolling preview to top scrolls editor to top.
- [ ] No scroll feedback loop — scrolling one pane does not cause oscillation in the other.
- [ ] Gutter sync dots update: blue when synced, amber when manually drifted, green after save.
- [ ] Word count in gutter reflects active section and updates on cursor move.

**Exit State:** ✅ COMPLETE
- Smart Gutter fully functional: drag resize (200px minimums), sync dots (blue/amber/green), rotated word count label.
- Bidirectional scroll sync operational. Feedback loop suppressed via `_isSyncingScroll` (C#) + `window._ghsScrolling` (JS).
- WebView scroll reporting via `window.chrome.webview.postMessage()` → `WebMessageReceived`.
- SettingsService singleton persisting `splitRatio`, `viewMode`, `theme` to `%AppData%/GHSMarkdownEditor/settings.json`.
- Split ratio and view mode restored on startup.

---

### Phase 2c — Active Block Highlight, Click-to-Sync, Inline Edit

**Entry State:** Phase 2b complete. Split view, Smart Gutter, and scroll sync functional. `data-source-line` attributes available in rendered HTML from Phase 2a.

**Goal:** Bidirectional block-level cursor sync and inline editing on the preview pane.

**Scope:**
- SourceMappingService: `Dictionary<int, string>` mapping source line numbers to HTML element selectors. Rebuilt on each `ParsedDocument` update.
- Active block highlight: cursor line → SourceMappingService lookup → JS injects/removes `ghs-active` CSS class (blue outline border, `--active-block` background).
- Single-click preview → JS message → SourceMappingService → editor cursor sync + scroll.
- Double-click preview → inline edit overlay (Avalonia Popup) showing raw Markdown for that source range.
- Inline edit commit: overlay content replaces source range → re-parse → re-render.
- Inline edit cancel: Escape dismisses with no changes.
- Supported elements: paragraphs, headings, list items, blockquotes.

**Acceptance Criteria:**
- [ ] Active block in preview has blue outline border matching editor cursor's section.
- [ ] Active block highlight updates within 100ms of cursor move.
- [ ] Single-click preview element syncs editor cursor to correct source line.
- [ ] Double-click opens inline edit overlay with correct raw Markdown content for that block.
- [ ] Committing inline edit updates editor source and re-renders preview correctly.
- [ ] Escape cancels inline edit with no changes to source.
- [ ] All four element types (paragraph, heading, list item, blockquote) support inline edit.

**Exit State:** ✅ COMPLETE
- SourceMappingService operational as singleton in DI.
- Active block highlight: editor cursor → preview blue `.ghs-active` outline, debounced 80ms.
- Click-to-sync: preview single-click → editor cursor + scroll via JSON `WebMessageReceived` dispatcher.
- Inline edit: `InlineEditWindow` (programmatic borderless Window) — double-click → overlay with raw Markdown → commit replaces source range via `ReplaceLines()`.
- WebMessageReceived dispatches on JSON `type`: `"scroll"`, `"click-sync"`, `"inline-edit"`.
- All JS listeners consolidated under single `NavigationCompleted` `InvokeScript` call.
- Line endings normalized to `\n` on file load.
- Avalonia 12.0.0 upgrade complete. Breaking change resolved: `Window.SystemDecorations` → `Window.WindowDecorations`.
- Phase 2 feature set complete.

---

### Phase 3 — Left Panel: Document Topology View

**Entry State:** Phase 2c complete. Full editor/preview pipeline with source mapping operational. PanelSlotViewModel exists from Phase 1 shell.

**Goal:** Topology View fully functional as the default left panel slot occupant. Panel slot swap infrastructure proven.

**Scope:**
- Heading tree rendered from live `MarkdownParsingService.ParsedDocument` AST — no re-parsing.
- Color-coded by level: H1 blue, H2 green, H3 amber, H4–H6 progressively de-emphasized gray.
- Active section auto-highlighted and scrolled into view in topology panel.
- Click node → editor cursor + preview both jump to that heading.
- Section balance chart at panel bottom: one bar per top-level section, width proportional to word count.
- PanelSlotViewModel: `ActiveOccupant` (ViewModelBase, observable), `SwapOccupant(PanelOccupant)`. Left slot wired. Swap verified with placeholder second occupant.

**Acceptance Criteria:**
- [ ] Topology tree updates within 500ms of editor changes.
- [ ] All heading levels H1–H6 render with correct color coding per design system tokens.
- [ ] Active section highlighted and visible without manual scroll in topology panel.
- [ ] Clicking a node jumps both editor cursor and preview scroll to that heading.
- [ ] Section balance chart reflects live word counts per top-level section.
- [ ] PanelSlotViewModel swap works: left slot swaps between Topology View and a placeholder without errors.

**Exit State:** ✅ COMPLETE

**New files (8):**
- `ViewModels/ViewModelBase.cs` — abstract base for panel occupant ViewModels
- `ViewModels/PanelSlotViewModel.cs` — manages left panel active occupant (`ActiveOccupant`, `SwapOccupant()`)
- `ViewModels/TopologyViewModel.cs` — heading tree from ParsedDocument AST, active node tracking, section balance bars
- `ViewModels/PlaceholderPanelViewModel.cs` — slot swap verification placeholder
- `Views/TopologyView.axaml` + `.cs` — panel header, scrollable node list (color-coded H1–H6, indented, active node highlighted), balance chart
- `Views/PlaceholderPanelView.axaml` + `.cs` — centered "Placeholder Panel" text
- `Views/HeadingLevelToBrushConverter.cs` — heading level → color converter + fraction → width converter

**Modified files (5):**
- `ViewModels/EditorViewModel.cs` — added `CaretLine` observable property
- `App.axaml` — DataTemplates for `TopologyViewModel` and `PlaceholderPanelViewModel`
- `App.axaml.cs` — DI registration for 3 new singletons + default left slot occupant set on startup
- `Views/MainWindow.axaml` — replaced static topology placeholder with `ContentControl` bound to `PanelSlotViewModel.ActiveOccupant`
- `Views/MainWindow.axaml.cs` — caret tracking wired, node click navigation, Ctrl+Shift+D debug slot swap

**Build:** 0 errors, 0 new warnings.

---

### Phase 4 — Right Panel: Outline + Slot System

**Entry State:** Phase 3 complete. Left panel slot system operational. Right panel renders but slot system not yet wired.

**Goal:** Right panel fully functional with Document Outline as default. Both slot systems proven.

**Scope:**
- Document Outline panel: flat heading list, indented by level, active heading highlighted blue.
- Click outline item → editor cursor + preview scroll jump.
- Outline updates from `MarkdownParsingService.ParsedDocument` — no re-parsing.
- Right panel PanelSlotViewModel wired: supports Outline, Snippets placeholder, AI Assist placeholder.
- Slot switcher UI at bottom of right panel.
- Pull tab tooltip shows current slot name.

**Acceptance Criteria:**
- [ ] Outline updates within 500ms of editor changes.
- [ ] Active heading highlighted in outline matches editor cursor section.
- [ ] Click outline item jumps editor cursor and preview scroll to correct heading.
- [ ] Slot switcher renders Outline / Snippets / AI Assist options.
- [ ] Swapping slots renders placeholder panels without errors.
- [ ] Pull tab tooltip displays current slot name correctly.

**Exit State:** ✅ COMPLETE

**New files (10):**
- `ViewModels/OutlineViewModel.cs` — heading tree from ParsedDocument AST, active node tracking (150ms debounce), node click navigation
- `ViewModels/SnippetsPlaceholderViewModel.cs` — `PanelName => "Snippets"`
- `ViewModels/AiAssistPlaceholderViewModel.cs` — `PanelName => "AI Assist"`
- `Views/OutlineView.axaml` + `.cs` — panel header, scrollable heading list with level colors/indent, auto-scroll to active node
- `Views/SnippetsPlaceholderView.axaml` + `.cs` — centered placeholder text
- `Views/AiAssistPlaceholderView.axaml` + `.cs` — centered placeholder text

**Modified files (5):**
- `App.axaml.cs` — removed `PanelSlotViewModel` + `PlaceholderPanelViewModel` singletons and startup occupant block; added `OutlineViewModel`, `SnippetsPlaceholderViewModel`, `AiAssistPlaceholderViewModel` singletons
- `App.axaml` — added 3 DataTemplates: `OutlineViewModel` → `OutlineView`, `SnippetsPlaceholderViewModel` → `SnippetsPlaceholderView`, `AiAssistPlaceholderViewModel` → `AiAssistPlaceholderView`
- `MainWindowViewModel.cs` — added `LeftPanelSlot`/`RightPanelSlot` direct properties, injected 4 VMs, added slot switcher computed bools + swap commands + `RightPanelSlotName`
- `MainWindow.axaml` — replaced right panel placeholder with DockPanel containing `ContentControl` (slot) + 3-button slot switcher; pull tab tooltip bound to `RightPanelSlotName`
- `MainWindow.axaml.cs` — removed `_panelSlotVm`/`_placeholderVm` fields; added `_outlineVm`; refactored to use `_mainVm.LeftPanelSlot`; added `InitializeRightPanelSlot()`; shared `OnNodeClicked` handler; removed `Ctrl+Shift+D`

**Avalonia note:** Slot switcher buttons require `Width="NaN"` (not `Width="Auto"`) to override the `.icon-rail-btn` style's `Width=44` and stretch equally inside Grid columns. Avalonia does not accept `"Auto"` as a string literal for `Width` in this context.

**Build:** 0 errors, 0 new warnings.

---

### Phase 5 — Contextual Command Palette

**Entry State:** Phase 4 complete. All Phase 1–4 services have registered commands in CommandRegistry. No palette UI exists yet.

**Goal:** Command Palette (Ctrl+P) fully functional. Every app action reachable from keyboard.

**Scope:**
- Full-screen overlay with fuzzy search input (Ctrl+P from any focus state).
- CommandRegistry search: `Search(string query) → IEnumerable<CommandDescriptor>`.
- Command categories: Navigation, Actions, Editor, Settings.
- All Phase 1–4 actions registered and searchable.
- Keyboard navigation: arrow keys + Enter executes, Escape closes.
- Recent commands (last 10) shown when input is empty.
- Palette dismisses after command execution.

**Acceptance Criteria:**
- [ ] Ctrl+P opens palette from any focus state.
- [ ] Fuzzy search filters commands — partial matches surface relevant results.
- [ ] Arrow key navigation moves selection. Enter executes selected command.
- [ ] Escape closes palette without side effects.
- [ ] All actions from Phases 1–4 reachable and executable via palette.
- [ ] Recent commands (up to 10) appear when input is empty.
- [ ] Palette dismisses after successful command execution.

**Exit State:** ✅ COMPLETE

**New files (4):**
- `ViewModels/CommandPaletteViewModel.cs` — `IsOpen`, `SearchText` (80ms debounce), `Results`, keyboard navigation (`MoveUp`/`MoveDown`/`ExecuteSelected`), `Open()`/`Close()`
- `Views/CommandPaletteView.axaml` + `.cs` — full-window overlay: semi-transparent backdrop, 480px centered card, search input with auto-focus, results list with category badge / title / shortcut columns, Escape/ArrowUp/ArrowDown/Enter handling, row click, empty state

**Modified files (6):**
- `Services/CommandRegistry.cs` — `CommandDescriptor` field renamed `Label` → `Title`; added `KeyboardShortcut` field; fuzzy subsequence search with scoring (prefix match = 2, contiguous = 1, subsequence = 0); max 10 recent commands
- `App.axaml.cs` — removed old file command registration block; added `CommandPaletteViewModel` singleton
- `App.axaml` — added `DataTemplate` for `CommandPaletteViewModel` → `CommandPaletteView`
- `ViewModels/MainWindowViewModel.cs` — injected `CommandRegistry` + `CommandPaletteViewModel`; added `CommandPalette` property and `OpenPaletteCommand`; `RegisterCommands()` registers 15 commands (Actions, Navigation, Settings categories)
- `Views/MainWindow.axaml` — added `Ctrl+P` `KeyBinding`; wrapped root `DockPanel` in `Panel` for overlay stacking; added `ContentControl` for palette overlay (`ZIndex=100`, `IsVisible` bound to `CommandPalette.IsOpen`)
- `Views/MainWindow.axaml.cs` — added `RegisterEditorCommands()` registering 11 editor formatting commands (bold, italic, inline code, link, HR, H1–H6) via `TextEditor` selection access

**Adaptations:**
- Editor commands registered from `MainWindow.axaml.cs` code-behind (not `EditorViewModel`) because they require direct `TextEditor` access for selection wrapping.
- `TextBox.Watermark` → `TextBox.PlaceholderText` (Avalonia 12 deprecation — applies to all future phases using TextBox placeholder text).

**Build:** 0 errors, 0 new warnings.

---

### Phase 6 — Snippet Studio

**Entry State:** Phase 5 complete. Command Palette operational. Right panel slot system ready to accept new occupants.

**Goal:** Snippet Studio fully functional as a swappable slot occupant.

**Scope:**
- Snippet list panel (assignable to left or right slot via slot switcher).
- Insert snippet: click in panel or via Command Palette → inserts at editor cursor with tab-stop field placeholders.
- Tab-stop navigation: Tab cycles through fields, Enter/Escape exits snippet mode.
- Snippet CRUD: add, edit, delete.
- Categorization: folder/tag grouping in panel.
- Snippets persisted to user data directory (JSON).
- Register snippet commands in CommandRegistry.

**Acceptance Criteria:**
- [ ] Snippets load from user data directory on startup.
- [ ] Inserting a snippet populates editor at cursor with tab-stop field markers.
- [ ] Tab key cycles through all tab-stop fields in order.
- [ ] Enter or Escape exits snippet field mode cleanly.
- [ ] Add, edit, delete operations persist correctly across restarts.
- [ ] Snippets accessible and executable from Command Palette.
- [ ] Snippet Studio assignable to both left and right panel slots.

**Exit State:** ✅ COMPLETE

**New files (8):**
- `Models/Snippet.cs` — data model with `$N` tab-stop marker format
- `Services/SnippetService.cs` — CRUD, JSON persistence to user data dir, 10 seed snippets, `SnippetsChanged` event
- `Services/SnippetModeController.cs` — `TextAnchor`-based tab-stop session management
- `Services/SnippetInsertionService.cs` — `$N` token parsing, clean text insertion, session activation
- `ViewModels/SnippetStudioViewModel.cs` — grouped categories, insert/add/edit/delete commands, dynamic CommandRegistry registration per snippet
- `Views/SnippetStudioView.axaml` + `.cs` — panel header with `[+]`, grouped category list, action bar, single-click select, double-click insert
- `Views/SnippetEditDialog.cs` — programmatic modal `Window` (Title / Category / Body fields, `Ctrl+Enter` to save, `Escape` to cancel)

**Modified files (4):**
- `App.axaml.cs` — registered `SnippetService`, `SnippetModeController`, `SnippetInsertionService`, `SnippetStudioViewModel` as singletons; `SnippetService.LoadAsync()` called on startup
- `App.axaml` — added `DataTemplate` for `SnippetStudioViewModel` → `SnippetStudioView`
- `ViewModels/MainWindowViewModel.cs` — replaced `_snippetsVm` → `_snippetStudioVm`; `IsSnippetsSlotActive` checks for `SnippetStudioViewModel` type; icon rail Topology/Snippets clicks now swap left panel occupant; `snippets.open` command registered
- `Views/MainWindow.axaml.cs` — added `_snippetModeController` / `_snippetInsertionService` fields; Tab/Escape/Enter tunnel interception for snippet mode; `InsertSnippetIntoEditor()` method; `SnippetStudioViewModel` delegates wired (insert, edit dialog, confirm dialog); snippet mode exits on file change

**Adaptations:**
- `SnippetEditDialog.ShowDialog()` renamed to `SnippetEditDialog.Open()` to avoid name conflict with Avalonia's `Window.ShowDialog<T>()`.
- Dialog delegates use `.Wait()` for blocking modal behavior. This is acceptable for v1.0 at desktop speeds but blocks the UI thread. Flagged for review in Phase 10 polish.

**Build:** 0 errors, 0 new warnings.

---

### Phase 7 — Version Timeline

**Entry State:** Phase 6 complete. FileService operational. Status bar renders with placeholder scrubber.

**Goal:** Version Timeline scrubber functional with auto-save snapshots and version restore.

**Scope:**
- SnapshotService: `SaveSnapshot()`, `GetSnapshots()`, `Prune()`, `RestoreSnapshot()`.
- Auto-save snapshot every 2 minutes (thread pool timer) + on every manual save.
- Timeline scrubber in status bar: horizontal track + draggable thumb.
- Drag thumb left → previews past version in preview pane (editor unchanged during drag).
- Release thumb → confirmation dialog → restore updates editor content.
- Snapshot retention: prune snapshots older than 7 days and beyond 200 per file on startup.
- Snapshot storage: user data directory, per-file subfolder.

**Acceptance Criteria:**
- [ ] Auto-save fires every 2 minutes — verify by inspecting snapshot count after 4+ minutes.
- [ ] Manual save (Ctrl+S) creates a snapshot immediately.
- [ ] Dragging timeline thumb left shows a past version in preview; editor content unchanged.
- [ ] Releasing thumb and confirming restore updates editor content to that version.
- [ ] Snapshots older than 7 days pruned on next app startup.
- [ ] Snapshots beyond 200 per file pruned (oldest first) on next app startup.
- [ ] Snapshot I/O does not block the UI thread.

**Exit State:** ✅ COMPLETE

Core functionality operational. Several editor interaction bugs
discovered and resolved during verification (Sessions 1–8) — see
NOTES.md Post-Phase-7 Bug Fix Sessions for full details.

---

### Phase 8 — Export System + Live Export Preview

**Entry State:** Phase 7 complete. WebView operational. Command Palette available.

**Goal:** All five export formats functional with Live Export Preview Panel.

**Scope:**
- Export: PDF (headless WebView print), DOCX (Open XML SDK or DocX), HTML Styled, HTML Clean, Plain Text.
- Live Export Preview Panel: slides in from right (full height), pushes right panel if open.
- Format selector: switches preview in real time.
- Export button → native save dialog → writes file.
- Panel dismisses after export or on Escape.
- Export accessible from toolbar and Command Palette.
- Register export commands in CommandRegistry.

**Acceptance Criteria:**
- [ ] PDF export produces correctly rendered PDF matching preview output.
- [ ] DOCX export includes correct heading styles (H1–H6), bold, italic, code blocks.
- [ ] HTML Styled export includes injected GHS CSS.
- [ ] HTML Clean export produces valid HTML with no style injection.
- [ ] Plain Text export strips all Markdown syntax correctly.
- [ ] Live Export Preview Panel opens and shows correct format preview.
- [ ] Format selector switches preview content in real time.
- [ ] Export button triggers native save dialog and writes file.
- [ ] Panel dismisses after successful export and on Escape.
- [ ] All export formats accessible from Command Palette.

**Exit State:** ✅ COMPLETE

**New package:**
- `DocumentFormat.OpenXml 3.1.0` — used for DOCX generation

**New files (5):**
- `Models/ExportFormat.cs` — `ExportFormat` enum (PdfStyled, Docx, HtmlStyled, HtmlClean, PlainText) + `ExportResult` record
- `Services/ExportService.cs` — `GeneratePreviewHtml()` and `ExportAsync()` for all 5 formats. DOCX via DocumentFormat.OpenXml AST walk. PDF via `NativeWebView.PrintToPdfStreamAsync()` — renders styled HTML in export WebView then prints to PDF stream.
- `ViewModels/ExportPanelViewModel.cs` — `IsOpen`, `SelectedFormat`, `PreviewHtml`, format selection, export command, auto-switch to Edit mode on open, restore view mode on close
- `Views/ExportPanelView.axaml` + `.cs` — two-column Grid layout: format buttons in column 0 (110px), NativeWebView in column 1 (*), header spanning both columns, Export button spanning both columns. Code-behind wires `ExportWithWebViewFunc` delegate for PDF export via `PrintToPdfStreamAsync`.

**Modified files (4):**
- `App.axaml.cs` — registered `ExportService` and `ExportPanelViewModel` as singletons
- `ViewModels/MainWindowViewModel.cs` — added `ExportPanel` property, `OpenExportCommand`, 6 export commands registered in CommandRegistry, auto-switch to Edit mode on export open/close via `Opened`/`Closed` events
- `Views/MainWindow.axaml` — Export button added to toolbar; ExportPanelView added as right-side overlay (Width=380, ZIndex=50) wrapped in Border with left border `#222222`
- `Views/MainWindow.axaml.cs` — ExportPanel delegates wired (save dialog + PDF export via WebView)

**Adaptations:**
- NativeWebView layout: format buttons and WebView must be in separate Grid columns (not DockPanel stacked) — NativeWebView is a native OS window and renders above all Avalonia controls in the same screen area regardless of z-order or DockPanel position. This is the same constraint as the main preview WebView.
- PDF export: uses `NativeWebView.PrintToPdfStreamAsync()` from `Avalonia.Controls.WebView 12.0.0`. The export panel's WebView navigates to styled HTML, waits for `NavigationCompleted`, then calls `PrintToPdfStreamAsync()` which returns a `Stream` written to the user's chosen file path. No external PDF library needed.
- HTML Clean and Plain Text previews: explicit white background and dark text forced in preview HTML — without this, the Export Panel's WebView inherits the app dark theme and content is unreadable.
- Export Panel auto-switches to Edit mode on open (collapses main NativeWebView) and restores previous view mode on close.

**Build:** 0 errors, 0 new warnings.

---

### Phase 9 — File Operations Completion + Auto-Save + Settings

**Entry State:** Phase 8 complete. Basic file ops (New/Open/Save/Save As) exist from Phase 2a. No recent files, drag-and-drop, draft sidecar, or full settings panel yet.

**Goal:** Complete file management, crash-recovery auto-save, draft restore, and full settings panel.

**Scope:**
- Recent files list (max 20, persisted). Shown in File menu and left panel File Browser slot.
- Drag and drop `.md` files onto app window to open.
- `.md` file type association on install — Windows and macOS only.
- Auto-save every 60 seconds to `.draft` sidecar (FileService). Configurable interval.
- Draft restore prompt on open: if `.draft` is newer than saved file, prompt restore or discard.
- Full settings panel: theme, editor font family + size, split ratio default, auto-save interval, snippet library path, keyboard shortcut reference (read-only).
- All settings persisted to user data directory (JSON).

**Acceptance Criteria:**
- [ ] Recent files list (max 20) persists across restarts and displays in File menu.
- [ ] Dragging and dropping a `.md` file onto the app window opens it.
- [ ] File association opens app on double-click `.md` on Windows.
- [ ] File association opens app on double-click `.md` on macOS.
- [ ] Auto-save fires at configured interval — verify via `.draft` file timestamp.
- [ ] Draft restore prompt appears when `.draft` is newer than saved source file.
- [ ] Accepting restore loads draft content into editor.
- [ ] Discarding draft deletes the `.draft` file.
- [ ] All settings persist correctly across app restarts.
- [ ] Keyboard shortcut reference displays all registered shortcuts.

**Exit State:** ✅ COMPLETE

**New files (4):**
- `ViewModels/FileBrowserViewModel.cs` — recent files list, open command, clear recent command, subscribes to FileService.RecentFilesChanged
- `Views/FileBrowserView.axaml` + `.cs` — recent file rows (filename + truncated directory), empty state, action bar
- `Models/DraftFoundEventArgs.cs` — event args for draft restore prompt

**Modified files (8):**
- `Models/AppSettings.cs` — added `EditorFontFamily`, `EditorFontSize`, `AutoSaveIntervalSeconds`, `SnippetLibraryPath`, `RecentFiles` fields
- RightPanelOpenWidth (double, default 200.0) — persisted drag width of right panel
- LeftPanelOpen default changed to false — panel closed on first run
- `Services/FileService.cs` — implemented `GetRecentFiles()`, `AddToRecent()` (max 20, deduplicated), `WriteDraft()`, `DeleteDraft()`, `GetDraftPath()`; added `RecentFilesChanged` event; added `DraftFound` event with `_skipDraftCheck` guard flag; added `FileSaved` event call on SaveFile/SaveFileAs
- `ViewModels/MainWindowViewModel.cs` — added `EditorFontFamily`, `EditorFontSize`, `AutoSaveIntervalSeconds`, `SnippetLibraryPath` properties with immediate apply + persist; added `ShortcutCommands` for keyboard reference; draft timer wired; DraftFound handler wired; updated view mode command shortcuts
- `App.axaml` — added DataTemplate for `FileBrowserViewModel`
- `App.axaml.cs` — FileBrowserViewModel registered as singleton; draft timer disposal on exit; command-line .md argument handled on startup via Dispatcher.Post
- `Views/MainWindow.axaml` — DragDrop.AllowDrop="True" on Window; updated KeyBindings: removed Ctrl+1/2/3 view mode bindings; added Ctrl+Shift+E (Edit), Ctrl+Shift+T (Split), Ctrl+Shift+W (Preview)
- `Views/MainWindow.axaml.cs` — DragDrop handlers wired; draft restore confirm dialog implemented as plain Avalonia Window (not ContentDialog); unsaved changes dialog likewise; font apply delegate added
- `Views/SettingsView.axaml` — expanded with Editor Font, Auto-Save, Snippet Library Path, and Keyboard Shortcuts reference sections

**Adaptations:**
- `ContentDialog` (FluentAvalonia) caused `TypeLoadException` in Avalonia 12.0.0 (`PseudoClassesExtensions` not available). All confirm dialogs replaced with plain programmatic Avalonia `Window` instances — same pattern as `InlineEditWindow` and `SnippetEditDialog`. Do not use `ContentDialog` anywhere in this project.
- Editor formatting shortcuts (Ctrl+B/I/`/K/Shift+H, Ctrl+1–6) were only registered as `CommandDescriptor` metadata — no key handler dispatched them. Fixed by extending `OnEditorKeyDown` tunnel handler to call `_commandRegistry.Execute(cmdId)` for each shortcut.
- View mode shortcuts reassigned to avoid conflicts: Ctrl+1/2/3 now exclusively apply H1/H2/H3 headings. Ctrl+Shift+E = Edit Only, Ctrl+Shift+T = Split, Ctrl+Shift+W = Preview. Ctrl+Shift+S remains Save As.

**Build:** 0 errors, 0 new warnings.

---

### Phase 10 — Polish, Installer, Cross-Platform QA

**Entry State:** Phase 9 complete. All features implemented. App runs on Windows.

**Goal:** Production-ready v1.0.0 release on Windows and macOS.

**Scope:**
- Inno Setup installer for Windows (file association registered, clean uninstall).
- `.app` bundle + DMG for macOS (signed or notarized for Gatekeeper).
- Cross-platform QA: all acceptance criteria verified on Windows and macOS.
- Bundle size audit — remove unused dependencies.
- Startup time target: < 2 seconds cold on a mid-range 2020+ machine (Intel Core i5 / Apple M1, 8GB RAM, SSD).
- README.md with screenshots, feature list, cross-references to GHS Markdown Web and GHS Markdown WPF.
- GitHub Release v1.0.0 with Windows installer and macOS DMG attached.

**Acceptance Criteria:**
- [ ] Windows installer installs app, registers `.md` file association, uninstalls cleanly.
- [ ] macOS DMG mounts, app launches, Gatekeeper passes (signed or notarized).
- [ ] All Phase 1–9 acceptance criteria pass on Windows.
- [ ] All Phase 1–9 acceptance criteria pass on macOS.
- [ ] Cold startup < 2 seconds on a mid-range 2020+ machine (Intel Core i5 / Apple M1, 8GB RAM, SSD).
- [ ] README.md complete with screenshots and companion app links.
- [ ] GitHub Release v1.0.0 published with both installers attached.

**Exit State:**
- v1.0.0 released on GitHub.
- Windows and macOS installers published.
- README complete.

---

## Backlog (Post v1.0.0)

| ID | Feature | Notes | Status |
|---|---|---|---|
| BL-01 | AI Assist panel (right slot) | Claude-powered writing assistant in the right panel slot. User types a prompt (e.g. "summarize this document", "rewrite this paragraph more concisely", "suggest a title"); the current document or selected section is sent as context. Response streams into the panel. **Implementation Path A (primary):** Anthropic API (`api.anthropic.com/v1/messages`) with user-supplied API key stored in Settings. Requires separate Anthropic account at console.anthropic.com — not included in Claude Pro/Max subscriptions. Settings panel needs: API key field, Test Connection button, link to console.anthropic.com. **Implementation Path B (experimental, future):** Shell out to Claude Code CLI (`claude` subprocess via `Process.Start()`) if installed on user's machine — passes document content and prompt, captures output, applies back to editor. Fragile, not suitable for general distribution, but viable for power users who have CC installed. | ✅ Complete |
| BL-02 | Typewriter + Focus Mode | Active line centered, prose fades to 20% opacity | 🔄 Partial — prose dimming done, typewriter scroll deferred v1.2 |
| BL-03 | Dual monitor detached preview | Preview as a separate detachable window | ✅ Complete |
| BL-04 | Tab-based multi-file editing | Multiple open documents in tab bar | 📋 Planned |
| BL-05 | Jump List recent files (Windows) | Windows taskbar Jump List integration | 🔄 Partial — requires Inno Setup installer |
| BL-06 | Print to printer | Native OS print dialog | ✅ Complete |
| BL-07 | Git diff view (gutter) | Show unsaved changes vs last commit in editor gutter | ✅ Complete |
| BL-08 | GHS Glass theme refinement | Full blur/depth pass once base platform is stable | ✅ Complete |
| BL-09 | Custom theme editor | User-defined color tokens | ✅ Complete |
| BL-10 | Web app feature parity audit | Systematic comparison against md.theheadfamily.com | 📋 Planned |
| BL-11 | Linux support | Deferred from v1.0. AppImage distribution. File association via .desktop file (GNOME/KDE). Re-evaluate for v1.1. | 📋 Planned — spike required |
| BL-12 | Per-theme editor syntax colors | AvaloniaEdit .xshd uses hardcoded GHS Dark hex values. Post-v1.0: load different .xshd per active theme so editor syntax colors match GHS Ink / GHS Glass. | ✅ Complete |
| BL-13 | Tab-based multi-file editing | Multiple .md files open simultaneously with a tab bar above the editor. Each tab maintains independent editor state, cursor position, scroll position, draft sidecar, and snapshot history. Modeled on the tab system implemented in GHS Markdown WPF v1.0.1. | 📋 Planned — see BL-04 |
| BL-14 | Security: upgrade System.IO.Packaging | Transitive dependency of DocumentFormat.OpenXml 3.1.0 reports a known high severity vulnerability in version 8.0.0. Upgrade DocumentFormat.OpenXml to a newer version that pulls in a patched System.IO.Packaging, or add a direct PackageReference to a safe version. Verify DOCX export still works after upgrade. | ✅ Complete |
| BL-15 | Formatting toolbar | Add an icon-based formatting toolbar (matching the GHS Markdown WPF app style) below the main toolbar. Icons: New, Open, Save, Bold, Italic, Strikethrough, Code, H1–H4, unordered list, ordered list, table, HR, link, image, Export, Print. Show/hide toggle button in the main toolbar. State persists to AppSettings (ShowFormattingToolbar boolean, default true). Keyboard shortcut to toggle: Ctrl+Shift+B. | ✅ Complete |
| BL-16 | Preview selection → editor formatting | When text is selected in the preview pane and a formatting shortcut (Ctrl+B, Ctrl+I, etc.) is pressed, sync the editor cursor to that selection's source line first, then apply the formatting. Requires JS window.getSelection() to identify the source block, SourceMappingService to find the editor range, then wrap that range with the formatting markers. | ✅ Complete |
| BL-17 | Preview WYSIWYG editing (vision item) | Allow direct text editing in the preview pane with changes reflected in the Markdown source in real time. Extends Phase 2c inline-edit overlay toward a full bidirectional editing experience. Clicking any text in the preview enters edit mode for that block; typing updates the Markdown source live. This is a flagship differentiator — no mainstream Markdown editor offers true WYSIWYG with live source sync. Research phase required before implementation. | ✅ Complete |
| BL-17b | Code block language picker | {} toolbar button and right-click menu open a searchable language picker dialog (28 languages). Matches GHS Web and WPF companion apps. Double-click on a fence line opens the picker to change language. | ✅ Complete |
| BL-18 | User Guide (Word document) | Comprehensive user guide covering all features of the GHS Markdown Editor. Written in plain English for non-technical users. Delivered as a .docx file using the existing DOCX export infrastructure (DocumentFormat.OpenXml). Structure: Introduction, Getting Started (install, first launch, opening files), Interface Overview (layout diagram, panels, toolbar, status bar), Editor (typing, shortcuts, syntax highlighting, view modes), Preview Pane (scroll sync, click-to-sync, inline edit, double-click), Document Topology & Outline (navigation, balance chart), Snippet Studio (using snippets, tab stops, creating/editing snippets), Version Timeline (auto-save, scrubber, restore), Export (all five formats, suggested filename), Focus Mode (prose dimming, typewriter scroll), Command Palette, Settings (themes, font, auto-save, shortcuts reference), File Operations (recent files, drag-and-drop, draft restore), Keyboard Shortcuts Reference (complete table). Each section includes clearly labeled image placeholders (e.g. [IMAGE: Main window in Split mode showing editor and preview]) where screenshots should be inserted by the author. Tone: friendly, clear, task-oriented. | ✅ Complete |
| BL-19 | GHS Dark theme refinement | GHS Dark theme refinement — current Dark theme is too black/flat compared to polished editors like Visual Studio. Lighten the toolbar, icon rail, panel backgrounds, and icon foreground colors to add more visual depth and warmth. Target: toolbar/titlebar slightly lighter (#222228 range), icon rail with subtle contrast from panels, icon glyphs at higher opacity (text-hint too dark at #444444 — raise to #666666 or #707070), panel backgrounds with more distinction from the editor area. Reference: VS Code Dark+ and Visual Studio Dark as calibration targets. Both themes (Dark and Light) should be reviewed for color balance. | ✅ Complete |
| BL-20 | Editor right-click context menu | Editor right-click context menu — adds a context menu to the editor pane with the following items: **Formatting** (Bold, Italic, Strikethrough, Inline Code, Heading 1, Heading 2, Heading 3, Insert Link, Insert Image); **Editor Toggles** (Word Wrap on/off, Show Line Numbers on/off, Highlight Current Line on/off); **Edit** (Copy, Cut, Paste, Select All); **File** (Save). The three editor toggle states (WordWrap, ShowLineNumbers, HighlightCurrentLine) are added to AppSettings and persist across restarts. Default values: WordWrap=false, ShowLineNumbers=true, HighlightCurrentLine=true. | ✅ Complete |
| BL-21 | Light theme italic visibility | Light theme italic visibility — italic text in the editor is too light/faint in GHS Light theme. The DocumentColorizingTransformer uses #D0D0D0 for italic foreground which is near-invisible on the light (#F9F6F0) editor background. Fix: in MarkdownColorizingTransformer.cs, make italic foreground theme-aware — use a darker color (#555555 or similar) when the light theme is active, and the existing #D0D0D0 for dark theme. | ✅ Complete |
| BL-22 | Right panel pull tab | Right panel pull tab — improve discoverability. Replace the plain 10px strip with a visible arrow indicator: show > (chevron right) when the panel is closed to suggest "expand", and < (chevron left) when open to suggest "collapse". The chevron should be centered vertically on the tab and styled with the accent color. | ✅ Complete |
| BL-23 | Custom theme — Reset to Light Mode button | Custom theme — Reset to Light Mode button. The custom theme editor currently has one "Reset to Defaults" button which resets to GHS Dark baseline values. Add a second button "Reset to Light" that resets all 10 custom color tokens to the GHS Light theme values instead. This lets users start customizing from either the dark or light baseline. | ✅ Complete |
| BL-24 | Custom theme — Reset to Light bug fix | "Reset to Light" does not reset all color tokens correctly — some tokens retain GHS Dark values after reset. Fix the token map used in the Reset to Light handler so all 10 UI chrome tokens are set to their correct GHS Light baseline values. Bundle with BL-25. | ✅ Complete |
| BL-25 | Custom theme — user-configurable Markdown syntax colors | Extend the GHS Custom theme editor (only visible when GHS Custom is active) to include a second section for Markdown syntax colors. Color pickers for: H1, H2, H3, H4, H5, H6, Bold/Italic, Inline Code, Code Block, Blockquote. These tokens feed into both `MarkdownColorizingTransformer` (editor) and `ThemeService.GetThemeCss()` (preview). "Reset to Dark" and "Reset to Light" buttons must reset syntax color tokens alongside UI chrome tokens. Persist syntax color tokens in `AppSettings.CustomThemeColors` dictionary alongside existing chrome tokens. Bundle with BL-24. | ✅ Complete |
| BL-26 | Click-to-sync broken inside tables | Clicking on table cells (td, th, tr) in the preview does not sync the editor cursor. Root cause: the JS click listener's `e.target.closest('[data-source-line]')` finds the cell/row element rather than walking up to the parent `table` which holds the `data-source-line` attribute — or table blocks were not included in `SourceLineRenderer`. Fix: ensure `SourceLineRenderer` injects `data-source-line` on `<table>` elements, and update the JS click handler to walk up the DOM past `td`/`th`/`tr` to find the nearest `[data-source-line]` ancestor. Verify that `InlineEditWindow.TryOpen` also handles `table` tag correctly for double-click inline edit. | ✅ Complete |
| BL-27 | Draggable right panel divider | The right panel slot (AI Assist, Outline, Snippets) is fixed at 200px. Add a draggable divider on the left edge of the right panel — same interaction pattern as the Smart Gutter between editor and preview. Drag left to make the panel wider (pushing the preview pane), drag right to make it narrower. Minimum panel width: 160px. Maximum: 480px. Show `SizeWestEast` cursor on hover. Persist right panel width to `AppSettings` (new field: `RightPanelWidth`, default 200). Left panel slot width remains fixed at 220px — only the right panel is resizable. | ✅ Complete |
| BL-28 | Icon rail — first two icons wrong color | The first two icons in the icon rail (New File and Open File) render at a different color/opacity than the remaining icons in both GHS Dark and GHS Light themes. Root cause: likely hardcoded color value or different brush resource on those two icons instead of the shared icon color token. Fix: audit all icon rail icon definitions and ensure every icon uses the same brush resource (theme-aware, driven by the active theme's icon foreground token). Verify in both GHS Dark and GHS Light. | ✅ Complete |
| BL-29 | Scroll sync drift on long/complex documents | Proportional scroll sync (editor scroll fraction → preview scroll fraction) drifts significantly on long or table-heavy documents. Root cause: the editor and preview have different total scroll heights — a document with many tables renders much taller in the preview than the raw Markdown height in the editor, so the same scroll fraction maps to different visual positions. Fix: replace proportional scroll sync with anchor-based sync — on editor scroll, resolve the visible cursor/viewport line via `SourceMappingService`, find the corresponding `data-source-line` element in the preview, and scroll that element into view. This is more accurate than fraction-based sync and degrades gracefully when no mapping exists (falls back to proportional). Related to BL-26 (table source mapping). | ✅ Complete |

---

## Completed Bundles

**Bundle A — BL-24 + BL-25 (complete):**
Fixed "Reset to Light" not resetting all color tokens (bg-gutter missing from
CustomColors dict and reset methods), AND extended GHS Custom theme editor with
10 Markdown syntax color pickers. Both feed into MarkdownColorizingTransformer
and ThemeService.GetCustomCss(). See NOTES.md.

**Bundle B — BL-26 + BL-29 (complete):**
Fixed click-to-sync broken inside tables (Table type added to SourceLineRenderer
and SourceMappingService pattern matches), AND replaced proportional scroll sync
with anchor-based sync. See NOTES.md.

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
│   │   ├── Assets/                  # Fonts, icons, theme CSS
│   │   ├── Controls/                # SmartGutter, PullTab, CommandPalette, InlineEditOverlay
│   │   ├── Models/                  # Document, Snippet, Snapshot, CommandDescriptor
│   │   ├── Services/                # ThemeService, FileService, MarkdownParsingService,
│   │   │                            # SourceMappingService, SnapshotService, ExportService,
│   │   │                            # CommandRegistry
│   │   ├── ViewModels/              # MainWindowViewModel, EditorViewModel, PreviewViewModel,
│   │   │                            # PanelSlotViewModel, TopologyViewModel, OutlineViewModel,
│   │   │                            # SnippetStudioViewModel, CommandPaletteViewModel
│   │   ├── Views/                   # MainWindow, panels, dialogs, overlays
│   │   └── Themes/                  # GhsDark.axaml, GhsInk.axaml, GhsGlass.axaml
├── installer/
│   ├── windows/                     # Inno Setup scripts
│   └── macos/                       # DMG packaging scripts
└── docs/
    └── SPEC.md                      # This file
```

### Key Service Contracts

**ThemeService**
- `CurrentTheme` (observable property)
- `SetTheme(GhsTheme theme)`
- `GetThemeCss() → string` — returns full CSS string for current theme
- Raises `ThemeChanged` event — consumers call `GetThemeCss()` on change and inject themselves
- No dependency on any WebView type
- Scaffolded: Phase 1

CustomColors dictionary includes bg-gutter and 10 Markdown syntax color keys
(syntax-h1 through syntax-h6, syntax-bold, syntax-italic, syntax-code,
syntax-blockquote) as of BL-24/BL-25. All are user-configurable via Settings
when GHS Custom theme is active. GetCustomCss() reads all syntax vars from
CustomThemeColors; GetDarkCss() and GetLightCss() use hardcoded values.

**MarkdownParsingService**
- `ParsedDocument` (observable — `MarkdownDocument`)
- `RawText` (settable — triggers re-parse with 300ms debounce)
- `Parse(string markdown) → MarkdownDocument` (synchronous, one-off)
- Parsing on background thread; `ParsedDocument` updated on dispatcher
- All consumers (PreviewViewModel, TopologyViewModel, OutlineViewModel) bind to `ParsedDocument`
- Do not inline Markdig calls in ViewModels
- Scaffolded: Phase 2a

**SourceMappingService**
- `Rebuild(MarkdownDocument doc)` — called on each `ParsedDocument` update
- `GetElementSelector(int sourceLine) → string?`
- `GetSourceRange(string elementSelector) → (int start, int end)?`
- Scaffolded: Phase 2c

**FileService**
- `CurrentFilePath` (observable — nullable string)
- `CurrentContent` (observable — string)
- `NewFile()`
- `OpenFile(string? path = null)` — null triggers native open dialog
- `SaveFile()`
- `SaveFileAs()`
- `GetRecentFiles() → IEnumerable<string>`
- `AddToRecent(string path)`
- `GetDraftPath(string filePath) → string`
- `WriteDraft(string filePath, string content)`
- `DeleteDraft(string filePath)`
- Stub: Phase 1. Full file ops: Phase 2a. Recent files + draft: Phase 9.

**CommandRegistry**
- `Register(CommandDescriptor cmd)`
- `Search(string query) → IEnumerable<CommandDescriptor>`
- `Execute(string commandId)`
- `GetRecent(int count) → IEnumerable<CommandDescriptor>`
- Services register commands from Phase 2a onward
- Scaffolded: Phase 1

> **Note:** `CommandDescriptor.Title` is the correct field name as of Phase 5. The Phase 1 stub used `Label` — this was renamed during implementation. All future phases must use `Title`.

**SnapshotService**
- `SaveSnapshot(string filePath, string content)`
- `GetSnapshots(string filePath) → IEnumerable<Snapshot>`
- `Prune(string filePath)` — enforces 7-day / 200-snapshot limits
- `RestoreSnapshot(Snapshot snapshot)`
- All I/O on background thread via `Task.Run`
- Scaffolded: Phase 7

**SettingsService**
- `Load() → AppSettings` — reads `settings.json`, returns defaults if missing or malformed
- `Save(AppSettings settings)` — writes JSON on background thread (fire-and-forget)
- `AppSettings` record: `SplitRatio` (double), `ViewMode` (string), `Theme` (string)
- File location: `%AppData%/GHSMarkdownEditor/settings.json` (Windows), `~/.config/GHSMarkdownEditor/settings.json` (macOS)
- Singleton in DI. Scaffolded: Phase 2b.


**PanelSlotViewModel**
- `ActiveOccupant` (observable — `ViewModelBase`)
- `SwapOccupant(ViewModelBase occupant)` — sets `ActiveOccupant`, raises `PropertyChanged`
- Occupants resolved via Avalonia `DataTemplate` selector in `App.axaml` — one `DataTemplate` per occupant ViewModel type
- **Two instances required** (left panel + right panel). Do NOT register as a DI singleton. Both instances are constructed directly as properties on `MainWindowViewModel`: `LeftPanelSlot` and `RightPanelSlot`. Phase 3 registered one singleton — Phase 4 refactors this to direct construction.

---

### Avalonia 12.0.0 Breaking Changes (resolved)

One breaking change encountered during the post-Phase-2c upgrade:

- **`Window.SystemDecorations` renamed to `Window.WindowDecorations`** — affects `InlineEditWindow`. Use fully-qualified type: `WindowDecorations = Avalonia.Controls.WindowDecorations.None`.

All other Avalonia 12.0.0 APIs used by this project (`NativeWebView`, `TextEditor`, AXAML namespaces, style keys) were unchanged.

- **`TextBox.Watermark` deprecated → `TextBox.PlaceholderText`** — applies to all `TextBox` placeholder usage from Phase 5 onward.

`Tmds.DBus.Protocol` updated to 0.90.3 — advisory GHSA-xrw6-gwf8-vvr9 still reports against this version but the library is Linux-only (DBus) and is not invoked at runtime on Windows or macOS. No action required.

- **`FluentAvalonia.UI.Controls.ContentDialog` not usable** — `ContentDialog` uses `Avalonia.Controls.PseudoClassesExtensions` which is not available in Avalonia.Base 12.0.0 — throws `TypeLoadException` at runtime. Do not use `ContentDialog` anywhere in this project. Use plain programmatic Avalonia `Window` instances for all modal dialogs (see `InlineEditWindow`, `SnippetEditDialog` patterns).

---

AvaloniaEdit's `.xshd` syntax highlighting engine does not support CSS variables or runtime color injection. The `MarkdownSyntax.xshd` file uses hardcoded GHS Dark hex values for all syntax colors. This means:

- Editor syntax colors are fixed regardless of the active theme (GHS Dark, Ink, or Glass).
- The preview pane is fully theme-aware via `ThemeService.GetThemeCss()`.
- Switching to GHS Ink (light theme) produces a light preview but a dark-syntax editor — this is accepted behavior for v1.0.

Per-theme `.xshd` switching is tracked in backlog BL-12 as a post-v1.0 polish item.

---

Multiple features require bidirectional mapping between Markdown source positions and rendered HTML elements: active block highlight, click-to-sync, inline edit, and Topology View navigation.

**Approach: `data-source-line` attributes via Markdig renderer extension**

1. A custom Markdig renderer extension (`SourceLineRenderer`) walks the AST and injects `data-source-line="{startLine}"` and `data-source-end="{endLine}"` attributes onto every block-level HTML element (`p`, `h1`–`h6`, `li`, `blockquote`, `pre`, `table`). Uses Markdig's `SourceSpan` property on all `Block` nodes.

**Markdig API note:** The table block type in Markdig 0.44.0 is
`Markdig.Extensions.Tables.Table` (not `TableBlock`). Both
`SourceLineRenderer` and `SourceMappingService` use `Table` in their
pattern matches. Attributes are injected on the `Table` element only —
not on rows or cells — so clicking any cell bubbles to the table's
`data-source-line` via JS `closest()`.

2. `SourceMappingService` maintains a `Dictionary<int, string>` mapping source line numbers to HTML element selectors. Rebuilt on each `ParsedDocument` update.

3. **Editor → Preview (highlight):** Cursor line → element selector → JS injects/removes `ghs-active` CSS class.

4. **Preview → Editor (click-to-sync):** WebView JS listener fires on click, reads `data-source-line`, sends message to host → SourceMappingService resolves to editor line → editor cursor + scroll.

5. **Inline edit:** Double-click fires `data-source-line` + `data-source-end` to host → SourceMappingService resolves source range → Avalonia Popup opens over that range.

This mirrors the GHS Markdown web app's `remarkSourceLines` plugin pattern. Markdig's `SourceSpan` is the .NET equivalent of remark's position data.

**SourceLineRenderer scaffolded Phase 2a. SourceMappingService wired Phase 2c. All 2c+ features depend on it.**

---

### Auto-Save: Two Distinct Systems

| System | Service | Interval | Storage | Purpose |
|---|---|---|---|---|
| Draft sidecar | FileService | 60s (configurable) | `{filename}.draft` alongside source file | Crash recovery — discarded on explicit save |
| Version snapshot | SnapshotService | 120s + on manual save | User data directory, per-file store | Timeline history — 7 days / max 200 |

Both run independently. A manual save triggers a version snapshot AND discards the draft.

---

### Threading Model
- All UI updates on Avalonia dispatcher thread.
- Markdig parsing on background thread (MarkdownParsingService), result marshalled to dispatcher.
- WebView HTML injection on dispatcher.
- Snapshot I/O on background thread via `Task.Run`.
- Auto-save timers (draft + snapshot) fire on thread pool, marshal to dispatcher for content capture.
- SourceMappingService rebuild on background thread after parse, result marshalled to dispatcher.

### Dialog blocking (.Wait() pattern)
`SnippetEditDialog.Open()` is invoked via `.Wait()` on the UI thread for blocking modal behavior (Phase 6). This is functional at desktop speeds but is not async-safe. If dialog complexity increases post-v1.0, refactor to `await ShowDialog<T>()` with a proper async delegate chain. Tracked for Phase 10 review.

---

## Definition of Done (per phase)

A phase is done when:
1. All acceptance criteria are checked off.
2. App builds without warnings on the primary development platform.
3. No regressions in previously completed phases.
4. SPEC.md updated with any spec changes discovered during implementation.
5. CC session closed; next phase prompt drafted before new CC session opened.

---

*Version: 2.9*
*Phase 1 through Phase 9 — ✅ COMPLETE. Phase 10 — not started.*
*Last updated: BL-24 through BL-29 complete. All planned backlog items resolved.*
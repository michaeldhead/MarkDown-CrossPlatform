# GHS Markdown Editor — Cross-Platform Dev Handoff
**Date:** April 12, 2026
**Handoff from:** Long-running development session (compacted)

---

## Project Identity

- **App:** GHS Markdown Editor Cross-Platform
- **Repo:** https://github.com/michaeldhead/MarkDown-CrossPlatform (PUBLIC)
- **Released:** v1.0.0 — tag `v1.0.0-Initial-release` — live on GitHub
- **Stack:** C#/.NET 10, Avalonia UI 12.0.0, AvaloniaEdit 12.0.0, Markdig 0.44.0, FluentAvaloniaUI 2.5.1, DocumentFormat.OpenXml 3.5.1, Avalonia.Controls.ColorPicker 12.0.0
- **Companion apps:** Web (michaeldhead/MarkDown-React-Firebase, md.theheadfamily.com), WPF (michaeldhead/MarkDown-WindowsApp)
- **Living spec:** `docs/SPEC.md` (v2.3), internal notes: `docs/NOTES.md`
- **User guide:** `docs/GHS-Markdown-Editor-User-Guide.docx`
- **User:** Mike — Software Development Manager. Refers to Claude Code as "CC". Uses Claude Max.

---

## Working Pattern

- Mike reads files from GitHub and pastes raw URLs or content directly
- Claude writes CC prompts as markdown code blocks in chat (NEVER HTML widgets)
- CC runs prompts, Mike pastes results back
- Before writing prompts for complex features, Claude fetches key source files first
- Agent mode used for iterative debugging
- **Always include a reporting instruction at the end of every CC prompt**
- CC prompts always presented as plain markdown code blocks so copy button is available

---

## GitHub Access Pattern

Since the repo is public, Claude can fetch files directly when Mike pastes the raw URL.
Key file URL pattern:
`https://raw.githubusercontent.com/michaeldhead/MarkDown-CrossPlatform/main/GhsMarkdown.Cross/src/GhsMarkdown.Cross/[path]`

---

## Architecture (Locked)

- Layout: Icon rail (44px) | Left panel slot (220px) | Editor | Gutter (28px) | Preview | Right panel slot (200px) | Pull tab (16px)
- Two NativeWebView instances: main preview + export panel — each needs own WebView2 user data folder via `EnvironmentRequested` event
- WebView2 user data folders: `WebView2Main`, `WebView2Export`, `WebView2Detached`
- `PanelSlotViewModel`: LeftPanelSlot + RightPanelSlot as direct properties on MainWindowViewModel
- Custom `CenteringTextEditor` subclass (BL-02 typewriter scroll — deferred v1.2)
- Syntax highlighting: `MarkdownColorizingTransformer` (DocumentColorizingTransformer) — theme-aware, recreated on theme change, accepts `bool isLightTheme` constructor param
- `ContentDialog` NOT usable (TypeLoadException) — use plain Avalonia Window for modals
- `TextBox.PlaceholderText` (not Watermark) for Avalonia 12

---

## Key File Locations

- `GhsMarkdown.Cross/src/GhsMarkdown.Cross/Views/MainWindow.axaml` — full layout
- `GhsMarkdown.Cross/src/GhsMarkdown.Cross/Views/MainWindow.axaml.cs` — all wiring
- `GhsMarkdown.Cross/src/GhsMarkdown.Cross/ViewModels/MainWindowViewModel.cs` — all commands
- `GhsMarkdown.Cross/src/GhsMarkdown.Cross/Services/SettingsService.cs` — AppSettings record
- `GhsMarkdown.Cross/src/GhsMarkdown.Cross/Services/ThemeService.cs` — CSS + Avalonia theme
- `GhsMarkdown.Cross/src/GhsMarkdown.Cross/Services/GitDiffService.cs` — git diff via CLI
- `GhsMarkdown.Cross/src/GhsMarkdown.Cross/Views/MarkdownColorizingTransformer.cs` — syntax highlighting
- `GhsMarkdown.Cross/src/GhsMarkdown.Cross/Views/CenteringTextEditor.cs` — custom TextEditor
- `GhsMarkdown.Cross/src/GhsMarkdown.Cross/Views/GitDiffMargin.cs` — AbstractMargin for git diff
- `GhsMarkdown.Cross/src/GhsMarkdown.Cross/Views/InlineEditWindow.cs` — inline edit overlay
- `GhsMarkdown.Cross/src/GhsMarkdown.Cross/Views/CodeLanguagePickerWindow.cs` — language picker
- `GhsMarkdown.Cross/src/GhsMarkdown.Cross/Themes/GhsDark.axaml` — dark theme brushes
- `GhsMarkdown.Cross/src/GhsMarkdown.Cross/Themes/GhsLight.axaml` — light theme brushes
- `GhsMarkdown.Cross/src/GhsMarkdown.Cross/Themes/GhsCustom.axaml` — custom theme brushes
- `installer/windows/setup.iss` — Inno Setup 6 script
- `docs/SPEC.md` — living spec v2.3
- `docs/NOTES.md` — implementation notes (force-added via git add -f, was in .gitignore)
- `docs/GHS-Markdown-Editor-User-Guide.docx` — user guide (17+ sections)

---

## AppSettings Fields (current)

```csharp
SplitRatio, ViewMode, Theme, EditorFontFamily, EditorFontSize,
AutoSaveIntervalSeconds, SnippetLibraryPath, FocusMode, RecentFiles,
LeftPanelOpen, ActiveIcon, ShowFormattingToolbar, AnthropicApiKey,
CustomThemeColors (Dictionary<string,string>), DetachedPreviewX,
DetachedPreviewY, DetachedPreviewWidth, DetachedPreviewHeight,
WordWrap, ShowLineNumbers, HighlightCurrentLine
```

---

## Themes

Three themes: **GHS Dark** (default), **GHS Light**, **GHS Custom**
- Custom theme: 10 editable tokens via color picker in Settings
- `ApplyCustomColorsToChrome()` + `ClearCustomColorsFromChrome()` in ThemeService
- `NotifyThemeChanged()` triggers WebView CSS re-injection
- `CustomColorsReset` event on MainWindowViewModel — SettingsView subscribes to refresh ColorPickers
- Reset to Dark and Reset to Light buttons in custom theme editor

---

## Build & Publish Commands

Run from: `C:\Users\mike\source\repos\MarkDown-CrossPlatform\GhsMarkdown.Cross\`

```bash
# Windows publish
dotnet publish GhsMarkdown.Cross/src/GhsMarkdown.Cross/GhsMarkdown.Cross.csproj -r win-x64 -c Release --self-contained

# Inno Setup installer — open setup.iss in Inno Setup IDE, press Ctrl+F9
# Output: installer/windows/output/GHSMarkdownEditor-Setup-1.0.0.exe
```

---

## Keyboard Shortcuts (current)

- Ctrl+N/O/S — New/Open/Save; Ctrl+Shift+S — Save As
- Ctrl+Shift+E/T/W — Edit/Split/Preview modes
- Ctrl+1–6 — H1–H6 headings; Ctrl+B/I/K — Bold/Italic/Link
- Ctrl+` — Inline code; Ctrl+Shift+H — HR
- Ctrl+Shift+F — Focus Mode; Ctrl+Shift+B — Toggle toolbar
- Ctrl+Shift+X — Strikethrough; Ctrl+Shift+U — Unordered list
- Ctrl+Shift+O — Ordered list; Ctrl+Shift+G — Table; Ctrl+Shift+I — Image
- Ctrl+P — Command Palette; Ctrl+Shift+P — Print; Ctrl+D — Detach Preview

---

## Backlog Status

### Complete ✅
- BL-01 — AI Assist panel (Anthropic API, streaming, right panel slot)
- BL-03 — Detached preview window (dual monitor, scroll sync, position memory)
- BL-06 — Print to printer via WebView window.print()
- BL-07 — Git diff margin (green/amber/red line indicators for git-tracked files)
- BL-08 — GHS Glass removed, GHS Ink → GHS Light
- BL-09 — Custom theme editor (GHS Custom, color pickers, persist, reset to dark/light)
- BL-12 — Syntax highlighting via DocumentColorizingTransformer
- BL-14 — Security (System.IO.Packaging), DOCX headings fixed, export filename from H1
- BL-15 — Formatting icon toolbar with show/hide toggle (Ctrl+Shift+B)
- BL-16 — Preview selection → editor formatting shortcuts
- BL-17 — InlineEditWindow extended (code blocks pre/code, tables)
- BL-17b — Code block language picker (28 languages, matches web + WPF apps)
- BL-18 — User Guide (Word document with UI images)
- BL-19 — GHS Dark theme refinement (depth, lighter icons/text)
- BL-20 — Editor right-click context menu (formatting, toggles w/ checkmarks, clipboard, save)
- BL-21 — Light theme italic visibility fix (theme-aware MarkdownColorizingTransformer)
- BL-22 — Pull tab chevron ‹/› for right panel (16px, accent color)
- BL-23 — Custom theme Reset to Light button
- Icon — App icon wired to titlebar, exe, installer
- Keys — All shortcuts global from any focus state
- WebView — WebView2 installed app crash fixed, Jump List working post-install
- Docs — README, SPEC v2.3, NOTES.md, User Guide all updated and pushed

### Partial 🔄
- BL-02 — Focus Mode: prose dimming ✅, typewriter scroll deferred to v1.2
- BL-05 — Jump List: working post-install ✅, requires Inno Setup installer

### Pending (priority order) 📋
1. **BL-17 WYSIWYG** — research pass first; InlineEditWindow already handles most cases
2. **BL-04/13** — Tabs (major architecture — plan before any code, touches FileService/EditorViewModel/preview/detached window)
3. **BL-11** — Linux support (WebView spike needed first, outcome unknown)
4. **BL-07** — Git diff is complete; no further work needed
5. **BL-17b** — Complete; no further work needed

---

## Feature Implementation Notes

### BL-20 — Right-Click Context Menu
- `AttachEditorContextMenu()` method in `MainWindow.axaml.cs`, called at end of `InitializeEditor()`
- Assigned to `_editor.TextArea.ContextMenu`
- Toggle items require `ToggleType = MenuItemToggleType.CheckMark` to render checkmarks
- `IsChecked` set in `menu.Opening` handler each time menu opens
- Clipboard uses `TopLevel.GetTopLevel(_editor)?.Clipboard` (not deprecated `Application.Current.Clipboard`)
- Three new AppSettings: `WordWrap` (false), `ShowLineNumbers` (true), `HighlightCurrentLine` (true)
- Persistence via `partial void OnXxxChanged` hooks using `s with { }` pattern

### BL-07 — Git Diff Margin
- `GitDiffService` shells out via `Process.Start` — no NuGet package
- `FindGit()` searches PATH then common Windows install locations
- Arguments via `ProcessStartInfo.ArgumentList` (not single string) — avoids path quoting issues
- Pass filename only (not full path) to `git diff HEAD --` — full path returns empty on Windows
- `GitDiffMargin` is `AbstractMargin` subclass; added to `_editor.TextArea.LeftMargins`
- 0px wide when no diff data — completely invisible for clean/non-repo files
- Positive keys = line numbers (Added/Modified), negative keys = deletion markers
- Colors: green `#4E9E4E`, amber `#E0A030`, red `#CC4444`
- Silent fail on all errors — app unaffected if git not installed

### BL-17 / BL-17b — Inline Edit + Language Picker
- `InlineEditWindow.TryOpen` now accepts: p, h1-h6, li, blockquote, pre, code, table, tr, td, th
- `SizeToContent = SizeToContent.Height` on both InlineEditWindow and CodeLanguagePickerWindow
- `CodeLanguagePickerWindow` — 28 languages, WrapPanel pills, real-time search filter
- `{}` toolbar button replaced `</>` — opens language picker, inserts fenced block with cursor inside
- Double-click on ` ```language ` fence line opens picker via `PointerPressedEvent` tunnel handler
- "Change Language..." in context menu — only visible (`IsVisible`) when `GetFenceLanguage()` is not null

---

## Known Issues / Architectural Constraints

- **BL-02 Typewriter scroll**: AvaloniaEdit 12 BringCaretToView fires after all dispatcher overrides. Needs TextView.MakeVisible() override. Deferred v1.2.
- **BL-05 Jump List**: Requires Inno Setup installer to register .md file association.
- **BL-12 xshd**: AvaloniaEdit 12 rejects both Rule ^-anchors and Span \n End patterns. Using DocumentColorizingTransformer instead.
- **Tmds.DBus.Protocol**: Linux-only vulnerability, no action needed for Windows distribution.
- **ContentDialog**: TypeLoadException — never use, always use plain Window.
- **NativeWebView column isolation**: Must collapse columns to GridLength(0) AND set IsVisible=false.
- **WebView2 user data folders**: Set via EnvironmentRequested event, NOT env variable.
- **NOTES.md in .gitignore**: Was force-added with `git add -f`. Remove NOTES.md from .gitignore so future updates commit normally.

---

## Jira

Primary project: CLRCRT (CLR Shikhar) on catalisgov.atlassian.net
Cloud ID: be8f1e2d-b350-4cb8-9123-20ef78ea0ed9
Issue types: Initiative → Epic → Story → Bug (NO Sub-task or Task)
1 story point = 1 developer day. Target 1-3 points per story.

---

## User Preferences (Always Apply)

- US English spelling
- CC prompts always in plain markdown code blocks (never HTML widgets)
- Always include reporting instruction at end of CC task files
- BL tracking maintained as visual widget in chat
- Mike does not code — Claude writes all CC prompts
- GitHub raw URLs pasted by Mike so Claude can read files directly
- "CC" = Claude Code
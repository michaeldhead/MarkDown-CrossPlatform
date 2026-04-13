# GHS Markdown Editor — Cross-Platform | Project Handoff

## What This Is
You are Claude, continuing work on the GHS Markdown Editor cross-platform desktop app. This document is a complete handoff from another Claude session. The user will also paste the current `SPEC.md` and `NOTES.md` files — read all three before responding.

---

## Who I Am
I am Mike (Michael Head), a Software Development Manager. I do not write code. My workflow is:
- **Claude app (planning)** — architecture, spec authorship, design decisions, CC prompt generation
- **Claude Code / CC (implementation)** — executes prompts, builds the code

**Always format CC prompts as plain markdown code blocks** — never HTML widgets. Always include a reporting instruction at the end of every CC prompt.

---

## The Project
**GHS Markdown Editor — Cross-Platform**
A feature-rich Markdown editor for Windows and macOS built with Avalonia UI. Third app in the GHS ecosystem:
- **Web app** — md.theheadfamily.com (React/TypeScript/Vite, Firebase hosted)
- **Windows WPF app** — michaeldhead/MarkDown-WindowsApp, released v1.0.1
- **Cross-platform app (this project)** — michaeldhead/MarkDown-CrossPlatform, released v1.0.0

**Repo:** https://github.com/michaeldhead/MarkDown-CrossPlatform (PUBLIC)
**Raw file URL pattern:** `https://raw.githubusercontent.com/michaeldhead/MarkDown-CrossPlatform/main/GhsMarkdown.Cross/src/GhsMarkdown.Cross/[path]`
**Living spec:** `docs/SPEC.md` (v2.8 — pasted in this chat)
**Implementation notes:** `docs/NOTES.md` (pasted in this chat)

---

## Tech Stack (locked)

| Layer | Choice |
|---|---|
| Language | C# / .NET 10 |
| UI Framework | Avalonia 12.0.0 |
| MVVM | CommunityToolkit.Mvvm 8.4.2 |
| Editor engine | Avalonia.AvaloniaEdit 12.0.0 |
| Markdown parser | Markdig 0.44.0 |
| Preview renderer | Avalonia.Controls.WebView 12.0.0 (`NativeWebView`, `NavigateToString()`, `InvokeScript()`) |
| Theming | FluentAvaloniaUI 2.5.1 |
| DOCX export | DocumentFormat.OpenXml 3.5.1 |
| Solution format | `.slnx` (.NET 10+) |

---

## Current Status

**v1.0.0 shipped.** All 10 original phases complete. Substantial backlog completed post-v1.0.0.

### Backlog Summary

| ID | Feature | Status |
|---|---|---|
| BL-01 | AI Assist panel (Anthropic API, streaming) | ✅ Complete |
| BL-02 | Typewriter + Focus Mode | 🔄 Partial — prose dimming done, typewriter scroll deferred v1.2 |
| BL-03 | Detached preview window | ✅ Complete |
| BL-04/13 | Tab-based multi-file editing | 📋 Planned — major architecture, needs planning session before any code |
| BL-05 | Jump List recent files (Windows) | 🔄 Partial — requires Inno Setup installer |
| BL-06 | Print to printer | ✅ Complete |
| BL-07 | Git diff margin | ✅ Complete |
| BL-08 | GHS Glass → removed, GHS Ink → GHS Light | ✅ Complete |
| BL-09 | Custom theme editor (GHS Custom) | ✅ Complete |
| BL-10 | Web app feature parity audit | 📋 Planned |
| BL-11 | Linux support | 📋 Planned — spike required |
| BL-12 | Per-theme editor syntax colors (DocumentColorizingTransformer) | ✅ Complete |
| BL-14 | Security: System.IO.Packaging upgrade | ✅ Complete |
| BL-15 | Formatting toolbar | ✅ Complete |
| BL-16 | Preview selection → editor formatting | ✅ Complete |
| BL-17 | Preview WYSIWYG / inline edit extended | ✅ Complete |
| BL-17b | Code block language picker | ✅ Complete |
| BL-18 | User Guide (Word document) | ✅ Complete |
| BL-19 | GHS Dark theme refinement | ✅ Complete |
| BL-20 | Editor right-click context menu | ✅ Complete |
| BL-21 | Light theme italic visibility fix | ✅ Complete |
| BL-22 | Right panel pull tab chevron | ✅ Complete |
| BL-23 | Custom theme — Reset to Light button | ✅ Complete |
| BL-24 | Custom theme — Reset to Light bug fix | 📋 Planned — bundle with BL-25 |
| BL-25 | Custom theme — user-configurable Markdown syntax colors | 📋 Planned — bundle with BL-24 |
| BL-26 | Click-to-sync broken inside tables | 📋 Planned — bundle with BL-29 |
| BL-27 | Draggable right panel divider | 📋 Planned |
| BL-28 | Icon rail — first two icons wrong color | 📋 Planned |
| BL-29 | Scroll sync drift on long/complex documents | 📋 Planned — bundle with BL-26 |

---

## Key Architectural Constraints (must know before writing any CC prompt)

- **Two NativeWebView instances** — main preview + export panel. Each needs its own WebView2 user data folder set via `EnvironmentRequested` event (NOT env variable). Folders: `WebView2Main`, `WebView2Export`, `WebView2Detached`.
- **NativeWebView column isolation** — NativeWebView is a native OS HWND, renders above all Avalonia UI regardless of ZIndex or IsVisible. Use `GridLength(0)` column collapsing to hide, never `IsVisible=false` on a parent.
- **ContentDialog unusable** — throws `TypeLoadException` at runtime. Always use plain programmatic Avalonia `Window` for modals. See `InlineEditWindow`, `SnippetEditDialog` patterns.
- **TextBox.PlaceholderText** (not `.Watermark`) — Avalonia 12 API.
- **Window.WindowDecorations** (not `.SystemDecorations`) — Avalonia 12 breaking change.
- **Editor colors set programmatically** — AvaloniaEdit internal styles override AXAML. Apply via `ApplyEditorTheme()` in `MainWindow.axaml.cs` on startup and `ThemeChanged`.
- **Syntax highlighting** — uses `MarkdownColorizingTransformer` (DocumentColorizingTransformer subclass), NOT `.xshd`. Theme-aware, accepts `bool isLightTheme` constructor param, recreated on theme change.
- **AvaloniaEdit StyleInclude required** — `App.axaml` must include `<StyleInclude Source="avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml" />`.
- **CommandDescriptor.Title** (not `.Label`) — renamed during Phase 5 implementation.
- **PanelSlotViewModel** — two instances as direct properties on `MainWindowViewModel` (`LeftPanelSlot`, `RightPanelSlot`). Not DI singletons.
- **SnippetEditDialog.Open()** invoked via `.Wait()` for blocking modal — functional but not async-safe. Refactor candidate post-v1.0.

---

## Themes

- **GHS Dark** (default) — near-black shell (#141414), blue accent (#4A9EFF)
- **GHS Light** — cream/off-white (#F9F6F0)
- **GHS Custom** — 10 user-editable UI chrome tokens via color pickers in Settings. `ApplyCustomColorsToChrome()` / `ClearCustomColorsFromChrome()` in ThemeService. `CustomColorsReset` event on MainWindowViewModel. Reset to Dark + Reset to Light buttons.
- Auto mode maps Dark ↔ Light based on OS appearance.

---

## Bundled Backlog Items (action these together)

**Bundle A — BL-24 + BL-25:**
Fix "Reset to Light" not resetting all color tokens, AND extend the GHS Custom theme editor to include Markdown syntax color pickers (H1–H6, Bold/Italic, Inline Code, Code Block, Blockquote). Both feed into `MarkdownColorizingTransformer` and `ThemeService.GetThemeCss()`. Before writing the CC prompt, fetch the current `SettingsView` and `ThemeService` source files from GitHub.

**Bundle B — BL-26 + BL-29:**
Fix click-to-sync broken inside tables AND replace proportional scroll sync with anchor-based scroll sync. These are related — fixing `SourceLineRenderer` to inject `data-source-line` on `<table>` elements (BL-26) is a prerequisite for anchor-based sync to work on table content (BL-29). Before writing the CC prompt, fetch `MainWindow.axaml.cs` and the JS injection code from GitHub.

---

## How I Work With Claude

- Claude plans, architects, and generates CC prompts. CC implements.
- **CC prompts always in plain markdown code blocks** — never HTML widgets.
- Before writing prompts for complex features, fetch the relevant source files from GitHub first.
- After CC finishes a task, Mike pastes back the updated `NOTES.md`. Claude updates `SPEC.md` with actuals.
- Spec is always the source of truth. CC prompts are derived from it.
- When I say "update the spec" — apply described changes only, never regenerate from scratch.
- When I say a BL number — generate the CC prompt for that item based on the spec.
- When something is ambiguous — ask one focused question before proceeding.

---

## What To Do When I Message You

- **"BL-XX"** → fetch relevant source files from GitHub, then generate the CC prompt.
- **"Update the spec"** → apply described changes to SPEC.md only.
- **"Here are the notes"** → update SPEC.md with actuals, confirm what changed.
- **"Opus review"** → prepare a summary of what to tell Opus before the handoff.
- **Ambiguous request** → ask one focused question before proceeding.
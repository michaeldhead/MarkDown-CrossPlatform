# GHS Markdown Editor — Cross-Platform

A feature-rich Markdown editor for Windows and macOS, built with Avalonia UI.
Part of the GHS Markdown Editor ecosystem.

## What's New in v1.1.2

- **Multi-file tabs** — open multiple documents simultaneously in a tab strip built into the titlebar
- **Tab persistence** — open tabs are restored on relaunch with the same active tab and scroll positions
- **Single-instance enforcement** — opening a file from Explorer when the app is already running opens it in a new tab rather than launching a second window
- **Custom theme syntax colors** — H1–H6, Bold, Italic, Code, and Blockquote colors are individually configurable in GHS Custom theme
- **Draggable right panel** — resize the right panel by dragging its left edge
- **Anchor-based scroll sync** — scroll sync stays aligned on long and complex documents without drift
- **Click-to-sync in tables** — clicking table content in the preview now correctly syncs the editor cursor

## Features

### Editor
- Live split-pane editor and preview with anchor-based synchronized scrolling
- Multi-file tab editing — tab strip in titlebar, Ctrl+T / Ctrl+W / Ctrl+Tab
- Syntax highlighting for headings, bold, italic, code, links, blockquotes, and lists
- Right-click context menu with formatting, clipboard, and persistent toggle options
- Formatting toolbar with bold, italic, strikethrough, headings, lists, tables, links, and images
- Code block language picker — 28 languages, searchable
- Word wrap, line numbers, and current line highlight (persisted between sessions)
- Drag-and-drop file open

### Preview
- Live Markdown preview with GHS-styled CSS
- Inline editing — double-click any paragraph, heading, list item, blockquote, code block, or table to edit source directly
- Click any preview element to sync the editor cursor (including tables)
- Detached preview window for dual-monitor workflows

### Themes
- GHS Dark (default), GHS Light, and GHS Custom
- Custom theme editor with 10 UI chrome color tokens + 10 Markdown syntax color tokens
- Reset custom theme to Dark or Light baseline
- Auto mode follows OS appearance

### Tools
- AI Assist panel powered by the Anthropic API (streaming)
- Document Topology and Outline panels (per-tab)
- Snippet Studio with tab-stop insertion
- Version Timeline with auto-snapshot and manual restore
- Command Palette (Ctrl+P)
- Git diff margin — green, amber, and red line indicators vs last commit
- Focus Mode with prose dimming
- Draft auto-save and crash recovery

### Export & Print
- Export to PDF, Word (.docx), HTML (styled and clean), and plain text
- Print via system print dialog (Ctrl+Shift+P)

### Platform
- Single-instance enforcement with named pipe IPC (Windows)
- Windows Jump List integration
- Tab state persisted across restarts (up to 10 tabs restored)
- Unsaved-changes dialog on tab close and app exit

---

## GHS Markdown Ecosystem

| App | Platform | Repo | Live |
|-----|----------|------|------|
| GHS Markdown Web | Browser | [MarkDown-React-Firebase](https://github.com/michaeldhead/MarkDown-React-Firebase) | [md.theheadfamily.com](https://md.theheadfamily.com) |
| GHS Markdown for Windows | Windows (WPF) | [MarkDown-WindowsApp](https://github.com/michaeldhead/MarkDown-WindowsApp) | — |
| GHS Markdown Cross-Platform | Windows + macOS | [MarkDown-CrossPlatform](https://github.com/michaeldhead/MarkDown-CrossPlatform) | This repo |

---

## Keyboard Shortcuts

| Action | Shortcut |
|--------|----------|
| New File | Ctrl+N |
| Open File | Ctrl+O |
| Save | Ctrl+S |
| Save As | Ctrl+Shift+S |
| Print | Ctrl+Shift+P |
| **New Tab** | **Ctrl+T** |
| **Close Tab** | **Ctrl+W** |
| **Next Tab** | **Ctrl+Tab** |
| Command Palette | Ctrl+P |
| Edit Only | Ctrl+Shift+E |
| Split View | Ctrl+Shift+T |
| Preview Only | Ctrl+Shift+W |
| Focus Mode | Ctrl+Shift+F |
| Detach Preview | Ctrl+D |
| Toggle Formatting Toolbar | Ctrl+Shift+B |
| Bold | Ctrl+B |
| Italic | Ctrl+I |
| Inline Code | Ctrl+` |
| Insert Link | Ctrl+K |
| Heading 1–6 | Ctrl+1–6 |
| Strikethrough | Ctrl+Shift+X |
| Unordered List | Ctrl+Shift+U |
| Ordered List | Ctrl+Shift+O |
| Insert Table | Ctrl+Shift+G |
| Insert Image | Ctrl+Shift+I |
| Horizontal Rule | Ctrl+Shift+H |

---

## Tech Stack

- C# / .NET 10
- [Avalonia UI](https://avaloniaui.net/) 12.0.0
- [AvaloniaEdit](https://github.com/avaloniaui/AvaloniaEdit) 12.0.0
- [Markdig](https://github.com/xoofx/markdig) 0.44.0
- [FluentAvaloniaUI](https://github.com/amwx/FluentAvalonia) 2.5.1
- [DocumentFormat.OpenXml](https://github.com/dotnet/Open-XML-SDK) 3.5.1
- WebView2 (Windows) / WKWebView (macOS)

---

## Installation

### Windows

Download `GHSMarkdownEditor-Setup-1.1.0.exe` from the
[latest release](https://github.com/michaeldhead/MarkDown-CrossPlatform/releases/latest)
and run the installer.

### macOS

Download `GHSMarkdownEditor-1.1.0.dmg` from the
[latest release](https://github.com/michaeldhead/MarkDown-CrossPlatform/releases/latest),
open it, and drag the app to Applications.

---

## Building from Source

Requirements: .NET 10 SDK, Visual Studio 2022 17.13+ or JetBrains Rider.

```bash
git clone https://github.com/michaeldhead/MarkDown-CrossPlatform.git
cd MarkDown-CrossPlatform
dotnet build GhsMarkdown.Cross/src/GhsMarkdown.Cross/GhsMarkdown.Cross.csproj
dotnet run --project GhsMarkdown.Cross/src/GhsMarkdown.Cross/GhsMarkdown.Cross.csproj
```

### Windows publish (self-contained)
```bash
dotnet publish GhsMarkdown.Cross/src/GhsMarkdown.Cross/GhsMarkdown.Cross.csproj \
  -r win-x64 -c Release --self-contained
```

### macOS publish (self-contained)
```bash
dotnet publish GhsMarkdown.Cross/src/GhsMarkdown.Cross/GhsMarkdown.Cross.csproj \
  -r osx-arm64 -c Release --self-contained
```

---

## License

MIT

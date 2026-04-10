# GHS Markdown Editor — Cross-Platform

A feature-rich Markdown editor for Windows and macOS, built with Avalonia UI.
Part of the GHS Markdown Editor ecosystem.

## Features

- Live split-pane editor and preview
- Document Topology View — navigable heading tree with section balance chart
- Document Outline — right panel heading navigator
- Smart Gutter — drag resize, scroll sync indicators, word count
- Bidirectional scroll sync — editor and preview stay in sync
- Active block highlight — preview highlights the element at your cursor
- Click-to-sync — click preview to jump editor cursor
- Inline edit — double-click preview to edit that block in place
- Snippet Studio — parameterized templates with tab-stop fields
- Version Timeline — auto-save snapshots with scrubber and restore
- Export — HTML Styled, HTML Clean, Word (.docx), Plain Text, PDF (via HTML)
- Command Palette (Ctrl+P) — fuzzy search for every app action
- Three themes — GHS Dark (default), GHS Ink (light), GHS Glass
- Auto-save draft — crash recovery via .draft sidecar
- Recent files — File Browser panel with last 20 files

## GHS Markdown Ecosystem

| App | Platform | Repo | Live |
|-----|----------|------|------|
| GHS Markdown Web | Browser | [MarkDown-React-Firebase](https://github.com/michaeldhead/MarkDown-React-Firebase) | [md.theheadfamily.com](https://md.theheadfamily.com) |
| GHS Markdown for Windows | Windows (WPF) | [MarkDown-WindowsApp](https://github.com/michaeldhead/MarkDown-WindowsApp) | — |
| GHS Markdown Cross-Platform | Windows + macOS | [MarkDown-CrossPlatform](https://github.com/michaeldhead/MarkDown-CrossPlatform) | This repo |

## Keyboard Shortcuts

| Action | Shortcut |
|--------|----------|
| New File | Ctrl+N |
| Open File | Ctrl+O |
| Save | Ctrl+S |
| Save As | Ctrl+Shift+S |
| Command Palette | Ctrl+P |
| Edit Only | Ctrl+Shift+E |
| Split View | Ctrl+Shift+T |
| Preview Only | Ctrl+Shift+W |
| Bold | Ctrl+B |
| Italic | Ctrl+I |
| Inline Code | Ctrl+` |
| Insert Link | Ctrl+K |
| Heading 1–6 | Ctrl+1–6 |
| Horizontal Rule | Ctrl+Shift+H |

## Tech Stack

- C# / .NET 10
- [Avalonia UI](https://avaloniaui.net/) 12.0.0
- [AvaloniaEdit](https://github.com/avaloniaui/AvaloniaEdit) 12.0.0
- [Markdig](https://github.com/xoofx/markdig) 0.44.0
- [FluentAvaloniaUI](https://github.com/amwx/FluentAvalonia) 2.5.1
- [DocumentFormat.OpenXml](https://github.com/dotnet/Open-XML-SDK) 3.1.0
- WebView2 (Windows) / WKWebView (macOS)

## Installation

### Windows
Download `GHSMarkdownEditor-Setup-1.0.0.exe` from the
[latest release](https://github.com/michaeldhead/MarkDown-CrossPlatform/releases/latest)
and run the installer.

### macOS
Download `GHSMarkdownEditor-1.0.0.dmg` from the
[latest release](https://github.com/michaeldhead/MarkDown-CrossPlatform/releases/latest),
open it, and drag the app to Applications.

## Building from Source

Requirements: .NET 10 SDK, Visual Studio 2022 17.13+ or JetBrains Rider.

```bash
git clone https://github.com/michaeldhead/MarkDown-CrossPlatform.git
cd MarkDown-CrossPlatform
dotnet build src/GhsMarkdown.Cross/GhsMarkdown.Cross.csproj
dotnet run --project src/GhsMarkdown.Cross/GhsMarkdown.Cross.csproj
```

### Windows publish
```bash
dotnet publish src/GhsMarkdown.Cross/GhsMarkdown.Cross.csproj \
  -r win-x64 -c Release --self-contained
```

### macOS publish
```bash
dotnet publish src/GhsMarkdown.Cross/GhsMarkdown.Cross.csproj \
  -r osx-arm64 -c Release --self-contained
```

## License
MIT

# GHS Markdown Editor — Windows Installer

## Requirements
- [Inno Setup 6](https://jrsoftware.org/isinfo.php) installed on your machine.
- The app must be published first (Release build for win-x64).

## Build the publish output

From the repository root:

```bash
dotnet publish src/GhsMarkdown.Cross/GhsMarkdown.Cross.csproj \
  -r win-x64 -c Release --self-contained \
  -p:PublishSingleFile=false \
  -p:PublishTrimmed=false
```

Output lands in:
`src/GhsMarkdown.Cross/bin/Release/net10.0/win-x64/publish/`

## Compile the installer

Open a command prompt and run:

```
iscc installer\windows\setup.iss
```

Or open `setup.iss` in the Inno Setup IDE and press Compile (Ctrl+F9).

## Output

The compiled installer appears at:
`installer/windows/output/GHSMarkdownEditor-Setup-1.0.0.exe`

## File association

The installer registers `.md` files to open with GHS Markdown Editor.
This is removed cleanly on uninstall.

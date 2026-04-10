# GHS Markdown Editor — macOS DMG Builder

## Requirements

- macOS machine (arm64 or x64)
- .NET 10 SDK installed
- Xcode Command Line Tools: `xcode-select --install`

## Build the DMG

From the repository root:

```bash
chmod +x installer/macos/build-dmg.sh
./installer/macos/build-dmg.sh
```

This will:
1. Publish the app for osx-arm64 and osx-x64
2. Build a `.app` bundle from the arm64 output
3. Create a DMG at `installer/macos/GHSMarkdownEditor-1.0.0.dmg`

## Output

`installer/macos/GHSMarkdownEditor-1.0.0.dmg`

## Gatekeeper

This build is **unsigned**. On first launch, macOS will show a
Gatekeeper warning. Users can right-click the app → Open to bypass it.

## Optional: Code Signing + Notarization

Requires an Apple Developer account ($99/year).

### Sign the .app

```bash
codesign --deep --force --verify --verbose \
  --sign "Developer ID Application: Your Name (TEAMID)" \
  installer/macos/GHSMarkdownEditor.app
```

### Notarize

```bash
xcrun notarytool submit installer/macos/GHSMarkdownEditor-1.0.0.dmg \
  --apple-id "your@email.com" \
  --team-id "TEAMID" \
  --password "app-specific-password" \
  --wait

xcrun stapler staple installer/macos/GHSMarkdownEditor-1.0.0.dmg
```

Notarized builds pass Gatekeeper without any warning.

## Universal Binary (arm64 + x64)

The build script publishes both architectures separately.
To create a universal binary, use `lipo`:

```bash
lipo -create \
  installer/macos/publish/arm64/GhsMarkdown.Cross \
  installer/macos/publish/x64/GhsMarkdown.Cross \
  -output installer/macos/GHSMarkdownEditor.app/Contents/MacOS/GhsMarkdown.Cross
```

Run this after the script creates the .app bundle but before
creating the DMG. A universal binary runs natively on both
Apple Silicon and Intel Macs.

#!/bin/bash
set -e

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
APP_NAME="GHSMarkdownEditor"
VERSION="1.0.0"
BUNDLE_ID="com.ghs.markdown-editor"
EXECUTABLE="GhsMarkdown.Cross"

echo "=== GHS Markdown Editor — macOS DMG Builder ==="
echo "Repo root: $REPO_ROOT"

# ── Publish arm64 ────────────────────────────────────────────────────────────
echo ""
echo "Publishing osx-arm64..."
dotnet publish "$REPO_ROOT/src/GhsMarkdown.Cross/GhsMarkdown.Cross.csproj" \
  -r osx-arm64 -c Release --self-contained \
  -o "$REPO_ROOT/installer/macos/publish/arm64"

# ── Publish x64 ─────────────────────────────────────────────────────────────
echo ""
echo "Publishing osx-x64..."
dotnet publish "$REPO_ROOT/src/GhsMarkdown.Cross/GhsMarkdown.Cross.csproj" \
  -r osx-x64 -c Release --self-contained \
  -o "$REPO_ROOT/installer/macos/publish/x64"

# ── Build .app bundle (arm64) ─────────────────────────────────────────────────
APP_DIR="$REPO_ROOT/installer/macos/$APP_NAME.app"
echo ""
echo "Building .app bundle at $APP_DIR ..."

rm -rf "$APP_DIR"
mkdir -p "$APP_DIR/Contents/MacOS"
mkdir -p "$APP_DIR/Contents/Resources"

# Copy publish output
cp -R "$REPO_ROOT/installer/macos/publish/arm64/." "$APP_DIR/Contents/MacOS/"

# Make executable
chmod +x "$APP_DIR/Contents/MacOS/$EXECUTABLE"

# Write Info.plist
cat > "$APP_DIR/Contents/Info.plist" << EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN"
  "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>
  <string>GHS Markdown Editor</string>
  <key>CFBundleDisplayName</key>
  <string>GHS Markdown Editor</string>
  <key>CFBundleIdentifier</key>
  <string>$BUNDLE_ID</string>
  <key>CFBundleVersion</key>
  <string>$VERSION</string>
  <key>CFBundleShortVersionString</key>
  <string>$VERSION</string>
  <key>CFBundleExecutable</key>
  <string>$EXECUTABLE</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleDocumentTypes</key>
  <array>
    <dict>
      <key>CFBundleTypeExtensions</key>
      <array><string>md</string></array>
      <key>CFBundleTypeName</key>
      <string>Markdown Document</string>
      <key>CFBundleTypeRole</key>
      <string>Editor</string>
      <key>LSHandlerRank</key>
      <string>Alternate</string>
    </dict>
  </array>
  <key>NSHighResolutionCapable</key>
  <true/>
  <key>NSPrincipalClass</key>
  <string>NSApplication</string>
  <key>NSHumanReadableCopyright</key>
  <string>Copyright © 2025 GHS</string>
</dict>
</plist>
EOF

echo "Info.plist written."

# ── Create DMG ───────────────────────────────────────────────────────────────
DMG_PATH="$REPO_ROOT/installer/macos/$APP_NAME-$VERSION.dmg"
echo ""
echo "Creating DMG at $DMG_PATH ..."

hdiutil create \
  -volname "GHS Markdown Editor" \
  -srcfolder "$APP_DIR" \
  -ov \
  -format UDZO \
  "$DMG_PATH"

echo ""
echo "=== Done ==="
echo "DMG: $DMG_PATH"
echo ""
echo "NOTE: This build is unsigned. Users will see a Gatekeeper warning"
echo "on first launch. Right-click the app and choose Open to bypass it."
echo "To sign and notarize, see installer/macos/README.md."

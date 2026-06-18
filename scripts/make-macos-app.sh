#!/usr/bin/env bash
# Wraps a published self-contained macOS build into a proper "Downloader.app" bundle.
# A bare Unix binary never appears in Spotlight/Launchpad and dies when its launching
# terminal closes; a .app bundle is a real GUI app (indexed, dock icon, detached).
#
# Usage: make-macos-app.sh <publish-dir> <output-dir> <version>
#   <publish-dir>  directory containing the published "Downloader" executable
#   <output-dir>   where "Downloader.app" is created
#   <version>      e.g. 1.1.1 (used for CFBundle version strings)
set -euo pipefail

PUBLISH_DIR="$1"
OUT_DIR="$2"
VERSION="$3"

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ICNS="$SCRIPT_DIR/../src/Downloader.Desktop/Assets/downloader.icns"

APP="$OUT_DIR/Downloader.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

# All published files (single-file exe + any sidecar native libs) live next to the executable.
cp -R "$PUBLISH_DIR"/. "$APP/Contents/MacOS/"
chmod +x "$APP/Contents/MacOS/Downloader"
cp "$ICNS" "$APP/Contents/Resources/downloader.icns"

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>Downloader</string>
    <key>CFBundleDisplayName</key>
    <string>Downloader</string>
    <key>CFBundleIdentifier</key>
    <string>com.bezzad.downloader</string>
    <key>CFBundleExecutable</key>
    <string>Downloader</string>
    <key>CFBundleIconFile</key>
    <string>downloader.icns</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleInfoDictionaryVersion</key>
    <string>6.0</string>
    <key>CFBundleShortVersionString</key>
    <string>${VERSION}</string>
    <key>CFBundleVersion</key>
    <string>${VERSION}</string>
    <key>LSMinimumSystemVersion</key>
    <string>11.0</string>
    <key>LSApplicationCategoryType</key>
    <string>public.app-category.utilities</string>
    <key>NSHighResolutionCapable</key>
    <true/>
</dict>
</plist>
PLIST

echo ">> Built $APP"

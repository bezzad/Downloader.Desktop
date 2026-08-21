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

# Re-sign the assembled bundle (ad-hoc). The SDK signs the apphost as a STANDALONE Mach-O, but once
# it sits in a bundle next to an Info.plist macOS evaluates it as a BUNDLE and expects
# Contents/_CodeSignature/CodeResources. Without this, `codesign -v` on the installed app reports
# "code has no resources but signature indicates they must be present" — the bundle signature is
# invalid, so Gatekeeper/spctl can refuse it (the cask's quarantine strip is what has masked this).
# The kernel's exec check only validates the main executable, so this was not the cause of the
# v2.3.0 "won't launch" report, but a bundle that fails codesign verification is still a defect.
if command -v codesign >/dev/null 2>&1; then
  codesign --force --deep --sign - --timestamp=none "$APP"
  codesign --verify --verbose=2 "$APP"
  echo ">> Signed (ad-hoc) $APP"
else
  echo ">> WARNING: codesign not found — $APP is UNSIGNED and will not launch on Apple Silicon" >&2
fi

echo ">> Built $APP"

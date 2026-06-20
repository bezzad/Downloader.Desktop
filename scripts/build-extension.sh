#!/usr/bin/env bash
# Packages the browser extension into store-ready zips:
#   dist/downloader-extension-chrome.zip   (Chrome Web Store / Edge Add-ons — uses manifest.json)
#   dist/downloader-extension-firefox.zip  (Firefox AMO — uses manifest.firefox.json as manifest.json)
#
# Upload these in the respective developer dashboards (see src/browser-extension/PUBLISHING.md).
set -euo pipefail
cd "$(dirname "$0")/.."

SRC="src/browser-extension"
OUT="dist"
mkdir -p "$OUT"
rm -f "$OUT"/downloader-extension-*.zip

# Files shared by both targets (everything except manifests + repo docs).
COMMON=(background.js common.js popup.html popup.css popup.js icons)

echo ">> Chrome/Edge zip ..."
( cd "$SRC" && zip -qr -X "../../$OUT/downloader-extension-chrome.zip" manifest.json "${COMMON[@]}" )

echo ">> Firefox zip ..."
TMP=$(mktemp -d)
cp -r "$SRC"/. "$TMP"/
( cd "$TMP" && mv -f manifest.firefox.json manifest.json && rm -f manifest.firefox.json \
    && zip -qr -X "$OLDPWD/$OUT/downloader-extension-firefox.zip" manifest.json "${COMMON[@]}" )
rm -rf "$TMP"

echo ">> Done:"
ls -la "$OUT"/downloader-extension-*.zip

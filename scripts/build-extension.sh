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
# Keep in sync with what the manifests reference — verify_zip() below enforces that.
COMMON=(background.js common.js popup.html popup.css popup.js options.html options.css options.js icons)

# Fail loudly if a file a manifest points at never made it into the zip. A browser rejects the WHOLE
# extension when a referenced page is missing ("Could not load options page 'options.html'. Could not
# load manifest."), which is how the v2.6.0 zips shipped broken: the options page was added to the
# source and to both manifests, but not to COMMON above.
verify_zip() {
  local zip="$1" manifest="$2" listed ref missing=""
  listed=$(unzip -Z1 "$zip")
  # Every relative path the manifest references: any string ending in a packaged file extension.
  for ref in $(grep -oE '"[A-Za-z0-9_./-]+\.(html|js|css|png)"' "$manifest" | tr -d '"' | sort -u); do
    printf '%s\n' "$listed" | grep -qxF "$ref" || missing="$missing $ref"
  done
  if [ -n "$missing" ]; then
    echo "ERROR: $zip is missing files referenced by $manifest:$missing" >&2
    echo "       Add them to COMMON in scripts/build-extension.sh." >&2
    exit 1
  fi
}

echo ">> Chrome/Edge zip ..."
( cd "$SRC" && zip -qr -X "../../$OUT/downloader-extension-chrome.zip" manifest.json "${COMMON[@]}" )
verify_zip "$OUT/downloader-extension-chrome.zip" "$SRC/manifest.json"

echo ">> Firefox zip ..."
TMP=$(mktemp -d)
cp -r "$SRC"/. "$TMP"/
( cd "$TMP" && mv -f manifest.firefox.json manifest.json && rm -f manifest.firefox.json \
    && zip -qr -X "$OLDPWD/$OUT/downloader-extension-firefox.zip" manifest.json "${COMMON[@]}" )
rm -rf "$TMP"
verify_zip "$OUT/downloader-extension-firefox.zip" "$SRC/manifest.firefox.json"

echo ">> Done:"
ls -la "$OUT"/downloader-extension-*.zip

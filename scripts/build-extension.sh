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

# ---------------------------------------------------------------------------
# extension-catalog.json — what the APP reads to offer the extension for install.
#
# The static half lives in packaging/extension/targets.json; the version and sha256 are DERIVED here (from
# the target's own manifest and from the zip that was just built) so the catalog can never disagree with
# the assets it describes. Same shape and reasoning as scripts/build-plugins.sh + plugins-catalog.json.
# ---------------------------------------------------------------------------
TARGETS="packaging/extension/targets.json"
CATALOG="$OUT/extension-catalog.json"

if [ ! -f "$TARGETS" ]; then
  echo "ERROR: $TARGETS is missing — the app cannot be told which builds exist." >&2
  exit 1
fi

command -v jq >/dev/null || { echo "ERROR: jq is required to build the extension catalog." >&2; exit 1; }

sha256_of() {
  if command -v sha256sum >/dev/null; then sha256sum "$1" | awk '{print $1}'
  else shasum -a 256 "$1" | awk '{print $1}'; fi
}

echo ">> extension-catalog.json ..."
count="$(jq 'length' "$TARGETS")"
entries="[]"
for i in $(seq 0 $((count - 1))); do
  id="$(jq -r ".[$i].id" "$TARGETS")"
  family="$(jq -r ".[$i].family" "$TARGETS")"
  name="$(jq -r ".[$i].name" "$TARGETS")"
  manifest="$(jq -r ".[$i].manifest" "$TARGETS")"
  assetName="$(jq -r ".[$i].assetName" "$TARGETS")"
  minAppVersion="$(jq -r ".[$i].minAppVersion" "$TARGETS")"
  storeUrl="$(jq -r ".[$i].storeUrl // empty" "$TARGETS")"

  [ -f "$SRC/$manifest" ] || { echo "ERROR: target '$id' names a missing manifest: $SRC/$manifest" >&2; exit 1; }
  [ -f "$OUT/$assetName" ] || { echo "ERROR: target '$id' names a zip that was not built: $OUT/$assetName" >&2; exit 1; }

  version="$(jq -r '.version' "$SRC/$manifest")"
  [ -n "$version" ] && [ "$version" != "null" ] || { echo "ERROR: no version in $SRC/$manifest" >&2; exit 1; }
  sha256="$(sha256_of "$OUT/$assetName")"
  echo "   $id: version=$version asset=$assetName sha256=$sha256"

  entries="$(jq \
    --arg id "$id" --arg family "$family" --arg name "$name" \
    --arg version "$version" --arg assetName "$assetName" --arg sha256 "$sha256" \
    --arg minAppVersion "$minAppVersion" --arg storeUrl "$storeUrl" \
    '. + [{id:$id, family:$family, name:$name, version:$version, assetName:$assetName,
           sha256:$sha256, minAppVersion:$minAppVersion,
           storeUrl:(if $storeUrl == "" then null else $storeUrl end)}]' \
    <<<"$entries")"
done
echo "$entries" | jq '.' > "$CATALOG"

echo ">> Done:"
ls -la "$OUT"/downloader-extension-*.zip "$CATALOG"

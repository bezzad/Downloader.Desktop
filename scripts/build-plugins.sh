#!/usr/bin/env bash
# Build the OPTIONAL (catalog-tier) plugins into distributable zips and generate plugins-catalog.json.
#
# Optional plugins (e.g. Downloader.Desktop.Plugins.Hls) are NOT bundled in the app — they ship only as
# release assets that the app downloads on demand (see the consolidate-official-plugins change). This
# script is run by .github/workflows/release.yml on every vX.Y.Z tag, alongside the app archives, and
# attaches:
#   dist/<assembly>.zip           — one per optional plugin (its DLL + deps.json)
#   dist/plugins-catalog.json     — the manifest the app fetches to discover/install/update them
#
# The catalog entry's `sha256` is verified by the app BEFORE it loads a downloaded plugin, so the hash
# here is a security gate, not just an integrity check. `version` comes from the plugin csproj <Version>,
# which is also the single source for the plugin's runtime-reported version (HlsPlugin.Version derives it
# from the assembly) — keep those aligned so the update check doesn't loop.
#
# Usage: scripts/build-plugins.sh [output-dir]   (default: dist)
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="${1:-$ROOT/dist}"
SRC_MANIFEST="$ROOT/packaging/plugins/optional-plugins.json"
CATALOG="$OUT/plugins-catalog.json"

mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"          # absolute — the loop cd's into staging dirs before zipping
CATALOG="$OUT/plugins-catalog.json"
command -v jq >/dev/null || { echo "error: jq is required" >&2; exit 1; }

sha256_of() {
  if command -v sha256sum >/dev/null; then sha256sum "$1" | awk '{print $1}';
  else shasum -a 256 "$1" | awk '{print $1}'; fi
}

# Read <Version> from a csproj (single source for both runtime + catalog version).
csproj_version() {
  grep -oE '<Version>[^<]+</Version>' "$1" | head -1 | sed -E 's#</?Version>##g'
}

count="$(jq 'length' "$SRC_MANIFEST")"
echo "Building $count optional plugin(s) from $SRC_MANIFEST"

entries="[]"
for i in $(seq 0 $((count - 1))); do
  id="$(jq -r ".[$i].id" "$SRC_MANIFEST")"
  assembly="$(jq -r ".[$i].assembly" "$SRC_MANIFEST")"
  project="$(jq -r ".[$i].project" "$SRC_MANIFEST")"
  name="$(jq -r ".[$i].name" "$SRC_MANIFEST")"
  description="$(jq -r ".[$i].description" "$SRC_MANIFEST")"
  minAppVersion="$(jq -r ".[$i].minAppVersion" "$SRC_MANIFEST")"

  echo "==> $id ($assembly)"
  version="$(csproj_version "$ROOT/$project")"
  [ -n "$version" ] || { echo "error: no <Version> in $project" >&2; exit 1; }

  # Build (not self-contained: the plugin references only the host-provided SDK; no private managed deps).
  pubdir="$OUT/_build/$assembly"
  rm -rf "$pubdir"
  dotnet build "$ROOT/$project" -c Release -o "$pubdir" -v q -clp:NoSummary

  # The distributable = the plugin DLL + its deps.json (the ADR needs deps.json). The Abstractions SDK is
  # host-provided (Private=false) so it is not in the output; framework/runtime files are not either.
  staging="$OUT/_stage/$assembly"
  rm -rf "$staging"; mkdir -p "$staging"
  cp "$pubdir/$assembly.dll" "$staging/"
  [ -f "$pubdir/$assembly.deps.json" ] && cp "$pubdir/$assembly.deps.json" "$staging/"

  assetName="$assembly.zip"
  (cd "$staging" && zip -q -r "$OUT/$assetName" ./*)
  sha256="$(sha256_of "$OUT/$assetName")"
  echo "    version=$version  asset=$assetName  sha256=$sha256"

  entries="$(jq \
    --arg id "$id" --arg name "$name" --arg description "$description" \
    --arg version "$version" --arg assetName "$assetName" --arg sha256 "$sha256" \
    --arg minAppVersion "$minAppVersion" \
    '. + [{id:$id, name:$name, description:$description, version:$version, assetName:$assetName, sha256:$sha256, minAppVersion:$minAppVersion}]' \
    <<<"$entries")"
done

echo "$entries" | jq '.' > "$CATALOG"
rm -rf "$OUT/_build" "$OUT/_stage"
echo "Wrote $CATALOG:"
cat "$CATALOG"

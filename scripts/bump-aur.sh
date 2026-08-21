#!/usr/bin/env bash
# Rewrites packaging/aur/{PKGBUILD,.SRCINFO} for a new version + linux-x64 tarball checksum.
#
# Single source of truth for that rewrite: BOTH scripts/release.sh (which commits the in-repo
# mirror) and the `aur` job in .github/workflows/release.yml (which publishes to the AUR) call
# this, so the two can never drift.
#
# Usage: bump-aur.sh <version> <sha256>
#   <version>  e.g. 2.3.1 (no leading "v")
#   <sha256>   sha256 of the released Downloader-linux-x64.tar.gz
set -euo pipefail

VERSION="${1:?usage: bump-aur.sh <version> <sha256>}"
SHA="${2:?usage: bump-aur.sh <version> <sha256>}"

AUR_DIR="$(cd "$(dirname "$0")/.." && pwd)/packaging/aur"
[[ -d "$AUR_DIR" ]] || { echo "bump-aur: $AUR_DIR not found" >&2; exit 1; }

# GNU sed needs -i, BSD/macOS sed needs -i '' — keep this portable (see release.sh).
sedi() {
  if sed --version >/dev/null 2>&1; then sed -i "$@"; else sed -i '' "$@"; fi
}

sedi -E "s|^pkgver=.*|pkgver=$VERSION|"                      "$AUR_DIR/PKGBUILD"
sedi -E "s|^pkgrel=.*|pkgrel=1|"                             "$AUR_DIR/PKGBUILD"
sedi -E "s|^(sha256sums=\(')[a-f0-9A-F]*(')|\1$SHA\2|"       "$AUR_DIR/PKGBUILD"

sedi -E "s|pkgver = .*|pkgver = $VERSION|"                            "$AUR_DIR/.SRCINFO"
sedi -E "s|pkgrel = .*|pkgrel = 1|"                                   "$AUR_DIR/.SRCINFO"
sedi -E "s|(Downloader-)[0-9.]+(-linux-x64.tar.gz)|\1$VERSION\2|g"    "$AUR_DIR/.SRCINFO"
sedi -E "s|/v[0-9.]+/|/v$VERSION/|g"                                  "$AUR_DIR/.SRCINFO"
sedi -E "s|(LICENSE-)[0-9.]+|\1$VERSION|g; s|(downloader-)[0-9.]+(\.png)|\1$VERSION\2|g" "$AUR_DIR/.SRCINFO"
# Only the first sum is a real hash (the LICENSE/icon lines are "SKIP"), so a hex-only match is safe.
sedi -E "s|sha256sums = [a-f0-9A-F]{64}|sha256sums = $SHA|"           "$AUR_DIR/.SRCINFO"

# Fail loudly rather than publishing a half-rewritten PKGBUILD.
grep -q "^pkgver=$VERSION$" "$AUR_DIR/PKGBUILD" || { echo "bump-aur: PKGBUILD pkgver not rewritten" >&2; exit 1; }
grep -q "$SHA" "$AUR_DIR/PKGBUILD"              || { echo "bump-aur: PKGBUILD sha256 not rewritten" >&2; exit 1; }
grep -q "pkgver = $VERSION" "$AUR_DIR/.SRCINFO" || { echo "bump-aur: .SRCINFO pkgver not rewritten" >&2; exit 1; }

echo ">> AUR package rewritten for $VERSION"

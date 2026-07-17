#!/usr/bin/env bash
# Generates a signed APT repository (suite "stable", component "main", arch amd64) from one or more
# .deb files, laid out for hosting on GitHub Pages. Users add it with:
#
#   curl -fsSL https://bezzad.github.io/Downloader.Desktop/apt/pubkey.gpg \
#        | sudo gpg --dearmor -o /usr/share/keyrings/downloader.gpg
#   echo "deb [signed-by=/usr/share/keyrings/downloader.gpg] \
#        https://bezzad.github.io/Downloader.Desktop/apt stable main" \
#        | sudo tee /etc/apt/sources.list.d/downloader.list
#   sudo apt update && sudo apt install downloader
#
# Usage:  ./scripts/build-apt-repo.sh <out-dir> <deb> [deb ...]
#   The signing key comes from the GPG keyring of the calling user. Set APT_GPG_KEY_ID to pick a key;
#   otherwise the default secret key is used. Set APT_GPG_PUBKEY to a path to also copy the armored
#   public key into the repo as pubkey.gpg (else the current key is exported).
set -euo pipefail

OUT="${1:?usage: build-apt-repo.sh <out-dir> <deb> [deb ...]}"; shift
[ "$#" -ge 1 ] || { echo "!! at least one .deb is required"; exit 1; }

command -v dpkg-scanpackages >/dev/null || { echo "!! install 'dpkg-dev' (dpkg-scanpackages)"; exit 1; }
command -v gpg              >/dev/null || { echo "!! gpg is required to sign the Release"; exit 1; }

SUITE="stable"; COMP="main"; ARCH="amd64"
POOL="$OUT/pool/$COMP/d/downloader"
DIST="$OUT/dists/$SUITE"
BINDIR="$DIST/$COMP/binary-$ARCH"

rm -rf "$OUT"
mkdir -p "$POOL" "$BINDIR"

for deb in "$@"; do cp -f "$deb" "$POOL/"; done

# Packages index (paths are repo-root relative).
( cd "$OUT" && dpkg-scanpackages --arch "$ARCH" pool /dev/null > "dists/$SUITE/$COMP/binary-$ARCH/Packages" )
gzip -9c "$BINDIR/Packages" > "$BINDIR/Packages.gz"

# Release file describing the suite, with MD5Sum/SHA256 blocks over the index files (relative to $DIST).
# Built without apt-ftparchive so the only tooling deps are dpkg-scanpackages + gpg.
{
  echo "Origin: Downloader.Desktop"
  echo "Label: Downloader"
  echo "Suite: $SUITE"
  echo "Codename: $SUITE"
  echo "Architectures: $ARCH"
  echo "Components: $COMP"
  echo "Description: Downloader desktop APT repository"
  echo "Date: $(date -u '+%a, %d %b %Y %H:%M:%S UTC')"
  for algo in MD5Sum:md5sum SHA256:sha256sum; do
    field="${algo%%:*}"; cmd="${algo##*:}"
    echo "$field:"
    ( cd "$DIST" && find "$COMP" -type f | LC_ALL=C sort | while read -r f; do
        printf ' %s %16d %s\n' "$($cmd "$f" | cut -d' ' -f1)" "$(stat -c%s "$f")" "$f"
      done )
  done
} > "$DIST/Release"

# Sign: detached Release.gpg + inline InRelease.
KEYSEL=()
[ -n "${APT_GPG_KEY_ID:-}" ] && KEYSEL=(--local-user "$APT_GPG_KEY_ID")
gpg "${KEYSEL[@]}" --batch --yes --armor --detach-sign --output "$DIST/Release.gpg" "$DIST/Release"
gpg "${KEYSEL[@]}" --batch --yes --clearsign        --output "$DIST/InRelease"    "$DIST/Release"

# Publish the armored public key so users can trust the repo.
if [ -n "${APT_GPG_PUBKEY:-}" ] && [ -f "${APT_GPG_PUBKEY}" ]; then
  cp -f "$APT_GPG_PUBKEY" "$OUT/pubkey.gpg"
else
  gpg "${KEYSEL[@]}" --armor --export > "$OUT/pubkey.gpg"
fi

echo ">> APT repo written to $OUT (suite=$SUITE component=$COMP arch=$ARCH)"
ls -R "$OUT" | sed 's/^/   /'

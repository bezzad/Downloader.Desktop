#!/usr/bin/env bash
# Builds the Linux Snap package.
#
#   ./scripts/build-snap.sh            # build the .snap (requires snapcraft + LXD/multipass)
#
# Publishing (needs your Snapcraft / Ubuntu One login — one-time):
#   snapcraft login
#   snapcraft upload --release=stable downloader_<version>_amd64.snap
#
# Snaps auto-update via the Store, so the in-app updater is disabled when running as a snap
# (Services/UpdateFlow detects the SNAP env var).
set -euo pipefail
cd "$(dirname "$0")/.."

PROJ="src/Downloader.Desktop/Downloader.Desktop.csproj"

# Stamp the snap version from the csproj VersionPrefix.
VER=$(grep -oPm1 '(?<=<VersionPrefix>)[^<]+' "$PROJ")
echo "${VER:-0.0.0}" > snap/local/VERSION
echo ">> Version: ${VER:-0.0.0}"

echo ">> Publishing self-contained linux-x64 build ..."
rm -rf publish/linux-x64
dotnet publish "$PROJ" -c Release -r linux-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -p:DebugType=None -p:DebugSymbols=false \
  -o publish/linux-x64

# The published binary is "Downloader.Desktop"; the snap expects "Downloader".
[ -f publish/linux-x64/Downloader.Desktop ] && mv -f publish/linux-x64/Downloader.Desktop publish/linux-x64/Downloader
chmod +x publish/linux-x64/Downloader || true

echo ">> Packing snap ..."
snapcraft pack

echo ">> Done. Install locally with:  sudo snap install --dangerous ./downloader_*_amd64.snap"

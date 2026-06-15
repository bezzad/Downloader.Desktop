#!/usr/bin/env bash
# Builds self-contained, dependency-free executables locally for one or more runtimes.
# Usage:  ./scripts/publish.sh [rid ...]
#   e.g.  ./scripts/publish.sh linux-x64 win-x64 osx-arm64 osx-x64
# Output: dist/<rid>/  (and a .zip/.tar.gz next to it). No .NET needed on the target machine.
set -euo pipefail

cd "$(dirname "$0")/.."
PROJ="src/Downloader.Desktop/Downloader.Desktop.csproj"
RIDS=("$@")
[ ${#RIDS[@]} -eq 0 ] && RIDS=("linux-x64")

mkdir -p dist
for RID in "${RIDS[@]}"; do
  echo ">> Publishing $RID ..."
  OUT="dist/$RID"
  rm -rf "$OUT"
  dotnet publish "$PROJ" -c Release -r "$RID" --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true \
    -p:DebugType=None -p:DebugSymbols=false \
    -o "$OUT"

  # Nicer end-user executable name (avares URIs use the embedded assembly name, so renaming the file is safe).
  if [[ "$RID" == win-* ]]; then
    [ -f "$OUT/Downloader.Desktop.exe" ] && mv -f "$OUT/Downloader.Desktop.exe" "$OUT/Downloader.exe"
    (cd "$OUT" && zip -qr "../Downloader-$RID.zip" ./*)
  else
    [ -f "$OUT/Downloader.Desktop" ] && mv -f "$OUT/Downloader.Desktop" "$OUT/Downloader"
    chmod +x "$OUT/Downloader" 2>/dev/null || true
    tar -czf "dist/Downloader-$RID.tar.gz" -C "$OUT" .
  fi
  echo ">> Done: dist/Downloader-$RID.*"
done

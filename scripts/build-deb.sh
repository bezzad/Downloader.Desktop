#!/usr/bin/env bash
# Builds a Debian package (Downloader_<ver>_amd64.deb) from a linux-x64 self-contained publish.
# Layout mirrors the AUR package: the app lives under /opt/downloader, a /usr/bin/downloader symlink,
# a .desktop entry and a hicolor icon. No .NET or other runtime is required on the target machine.
#
# Usage:  VERSION=2.1.0 ./scripts/build-deb.sh [path-to-publish-dir]
#   - If a publish dir is given it is used as-is (must contain the "Downloader" executable).
#   - Otherwise the script publishes linux-x64 into dist/linux-x64 first.
# Output: dist/Downloader_<ver>_amd64.deb
set -euo pipefail

cd "$(dirname "$0")/.."
VERSION="${VERSION:-0.0.0-dev}"
# dpkg versions may not start with a letter and use ~ for pre-release ordering; keep it simple here.
DEBVER="${VERSION#v}"
ARCH="amd64"
PUBLISH_DIR="${1:-}"

if [ -z "$PUBLISH_DIR" ]; then
  echo ">> No publish dir given — building linux-x64 self-contained ..."
  VERSION="$DEBVER" ./scripts/publish.sh linux-x64
  # publish.sh tars the dir; use the loose publish output it leaves in dist/linux-x64.
  PUBLISH_DIR="dist/linux-x64"
fi

[ -f "$PUBLISH_DIR/Downloader" ] || { echo "!! '$PUBLISH_DIR/Downloader' not found"; exit 1; }

PKGROOT="dist/deb/Downloader_${DEBVER}_${ARCH}"
rm -rf "$PKGROOT"
mkdir -p "$PKGROOT/DEBIAN" \
         "$PKGROOT/opt/downloader" \
         "$PKGROOT/usr/bin" \
         "$PKGROOT/usr/share/applications" \
         "$PKGROOT/usr/share/icons/hicolor/512x512/apps" \
         "$PKGROOT/usr/share/doc/downloader"

# App payload (everything the publish produced).
cp -a "$PUBLISH_DIR/." "$PKGROOT/opt/downloader/"
chmod 755 "$PKGROOT/opt/downloader/Downloader"

# Icon (a raw ELF can't carry one; the .desktop + hicolor icon provide the menu/taskbar image).
ICON_SRC="src/Downloader.Desktop/Assets/downloader.png"
[ -f "$PUBLISH_DIR/downloader.png" ] && ICON_SRC="$PUBLISH_DIR/downloader.png"
if [ -f "$ICON_SRC" ]; then
  install -m644 "$ICON_SRC" "$PKGROOT/usr/share/icons/hicolor/512x512/apps/downloader.png"
  # Also drop it inside /opt so postinst can fall back if hicolor is absent.
  install -m644 "$ICON_SRC" "$PKGROOT/opt/downloader/downloader.png"
fi

install -m644 LICENSE "$PKGROOT/usr/share/doc/downloader/copyright" 2>/dev/null || true

# Desktop entry (StartupWMClass must match the app's X11 WmClass "Downloader").
cat > "$PKGROOT/usr/share/applications/downloader.desktop" <<'EOF'
[Desktop Entry]
Type=Application
Name=Downloader
Comment=Fast multi-connection download manager with queues and scheduler
Exec=/opt/downloader/Downloader %u
Icon=downloader
Terminal=false
Categories=Network;FileTransfer;Utility;
StartupWMClass=Downloader
EOF

# Compute installed size (KiB) for the control file.
INSTALLED_KB=$(du -ks "$PKGROOT/opt" "$PKGROOT/usr" | awk '{s+=$1} END{print s}')

cat > "$PKGROOT/DEBIAN/control" <<EOF
Package: downloader
Version: ${DEBVER}
Section: net
Priority: optional
Architecture: ${ARCH}
Maintainer: Behzad Khosravifar <behzad.khosravifar@gmail.com>
Installed-Size: ${INSTALLED_KB}
Depends: libicu-dev | libicu76 | libicu74 | libicu72 | libicu70, libssl3 | libssl3t64
Homepage: https://github.com/bezzad/Downloader.Desktop
Description: Fast multi-connection download manager
 Downloader is a cross-platform download manager built on a multipart
 download engine: pause/resume, download queues, a scheduler, speed limits
 and browser integration. Self-contained (no runtime required).
EOF

# Symlink /usr/bin/downloader -> the app (created here so it lands in the archive).
ln -sf /opt/downloader/Downloader "$PKGROOT/usr/bin/downloader"

# postinst / prerm: refresh the icon + desktop database so the launcher shows up immediately.
cat > "$PKGROOT/DEBIAN/postinst" <<'EOF'
#!/bin/sh
set -e
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
  gtk-update-icon-cache -q -f /usr/share/icons/hicolor 2>/dev/null || true
fi
if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database -q /usr/share/applications 2>/dev/null || true
fi
exit 0
EOF
cat > "$PKGROOT/DEBIAN/prerm" <<'EOF'
#!/bin/sh
set -e
exit 0
EOF
chmod 755 "$PKGROOT/DEBIAN/postinst" "$PKGROOT/DEBIAN/prerm"

OUT="dist/Downloader_${DEBVER}_${ARCH}.deb"
# --root-owner-group keeps files root:root without needing fakeroot/sudo.
dpkg-deb --root-owner-group --build "$PKGROOT" "$OUT"
echo ">> Built $OUT"
dpkg-deb --info "$OUT" | sed 's/^/   /'

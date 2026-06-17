#!/usr/bin/env bash
# Downloader Desktop — Linux installer.
# Downloads the latest self-contained release, installs it to ~/.local, and registers a desktop entry
# (with icon) so it shows up in your app menu and the taskbar uses the right icon.
#
#   curl -fsSL https://raw.githubusercontent.com/bezzad/Downloader.Desktop/main/scripts/install.sh | bash
#
# Uninstall:  rm -rf ~/.local/share/downloader ~/.local/bin/downloader \
#                    ~/.local/share/applications/downloader.desktop \
#                    ~/.local/share/icons/hicolor/512x512/apps/downloader.png
set -euo pipefail

REPO="bezzad/Downloader.Desktop"
APP_DIR="$HOME/.local/share/downloader"
BIN_DIR="$HOME/.local/bin"
DESKTOP_DIR="$HOME/.local/share/applications"
ICON_DIR="$HOME/.local/share/icons/hicolor/512x512/apps"

# Pick the asset for this CPU architecture.
case "$(uname -m)" in
  x86_64|amd64)   ASSET="Downloader-linux-x64.tar.gz" ;;
  aarch64|arm64)  ASSET="Downloader-linux-arm64.tar.gz" ;;
  *) echo "Unsupported architecture: $(uname -m)"; exit 1 ;;
esac

echo ">> Finding the latest release..."
URL=$(curl -fsSL "https://api.github.com/repos/$REPO/releases/latest" \
      | grep -o "https://[^\"]*$ASSET" | head -n1)
if [ -z "${URL:-}" ]; then
  echo "Could not find $ASSET in the latest release. It may not be published for your architecture yet."
  echo "See https://github.com/$REPO/releases"
  exit 1
fi

TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT

echo ">> Downloading $ASSET ..."
curl -fSL "$URL" -o "$TMP/app.tar.gz"

echo ">> Installing to $APP_DIR ..."
rm -rf "$APP_DIR"; mkdir -p "$APP_DIR" "$BIN_DIR" "$DESKTOP_DIR" "$ICON_DIR"
tar -xzf "$TMP/app.tar.gz" -C "$APP_DIR"

# The published binary is named "Downloader".
chmod +x "$APP_DIR/Downloader" 2>/dev/null || true
ln -sf "$APP_DIR/Downloader" "$BIN_DIR/downloader"

# Install the icon if the package ships one; otherwise the .desktop falls back to a generic icon.
for cand in "$APP_DIR/downloader.png" "$APP_DIR/Assets/downloader.png"; do
  [ -f "$cand" ] && cp -f "$cand" "$ICON_DIR/downloader.png" && break
done

# StartupWMClass must match the app's X11 WmClass ("Downloader") so the taskbar shows our icon.
cat > "$DESKTOP_DIR/downloader.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=Downloader
Comment=Fast multi-connection download manager
Exec=$APP_DIR/Downloader %u
Icon=downloader
Terminal=false
Categories=Network;FileTransfer;Utility;
StartupWMClass=Downloader
EOF

update-desktop-database "$DESKTOP_DIR" >/dev/null 2>&1 || true
gtk-update-icon-cache "$HOME/.local/share/icons/hicolor" >/dev/null 2>&1 || true

echo
echo ">> Installed. Launch it from your app menu, or run: downloader"
echo "   (make sure $BIN_DIR is on your PATH)"

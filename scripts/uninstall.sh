#!/usr/bin/env bash
# Downloader Desktop — Linux uninstaller.
# Removes everything installed by scripts/install.sh (all under ~/.local, no sudo needed).
#
#   curl -fsSL https://raw.githubusercontent.com/bezzad/Downloader.Desktop/main/scripts/uninstall.sh | bash
#   ./scripts/uninstall.sh            # remove the app (keeps your settings/downloads list)
#   ./scripts/uninstall.sh --purge    # also remove settings/config (~/.config/Downloader)
#
# Note: this only uninstalls the curl/install.sh build. If you installed the Snap, run
# instead:  sudo snap remove downloader   (add --purge to drop its data too).
set -euo pipefail

PURGE=0
[ "${1:-}" = "--purge" ] && PURGE=1

APP_DIR="$HOME/.local/share/downloader"
BIN_LINK="$HOME/.local/bin/downloader"
DESKTOP_FILE="$HOME/.local/share/applications/downloader.desktop"
ICON_ROOT="$HOME/.local/share/icons/hicolor"
PIXMAP="$HOME/.local/share/pixmaps/downloader.png"
CONFIG_DIR="$HOME/.config/Downloader"

echo ">> Removing the app ..."
rm -rf "$APP_DIR"
rm -f  "$BIN_LINK"
rm -f  "$DESKTOP_FILE"
rm -f  "$PIXMAP"
rm -f  "$ICON_ROOT"/*/apps/downloader.png

# Refresh the desktop + icon caches so it disappears from the app menu without a re-login.
update-desktop-database "$HOME/.local/share/applications" >/dev/null 2>&1 || true
gtk-update-icon-cache -f "$ICON_ROOT" >/dev/null 2>&1 || true

if [ "$PURGE" -eq 1 ]; then
  echo ">> Removing settings/config ($CONFIG_DIR) ..."
  rm -rf "$CONFIG_DIR"
else
  if [ -d "$CONFIG_DIR" ]; then
    echo ">> Kept your settings at $CONFIG_DIR (re-run with --purge to remove them)."
  fi
fi

echo ">> Done. Downloader has been uninstalled."

#!/usr/bin/env bash
# Rebuild every plugin from this working copy, install the optional ones over the running user's
# plugin folder, and start the app in Release.
#
# Why this exists: the optional plugins (HLS/DASH, site-media, website-zip) reach users through the
# release catalog, so a change to one of them cannot be tried out until it has been published. The app
# loads them from ~/.config/Downloader/plugins (PluginManager.PluginsRoot) whatever way it was started
# — including `dotnet run` from this repo — so copying a local build there is all it takes to test one.
# The BUILT-IN plugins (GitHub, Ollama) need nothing here: the app's own build stages them into its
# output folder.
#
# The plugins root is per-user, not per-install: this updates the plugins for EVERY copy of the app
# that runs as you (a dev run and a plain install share them). A snap has its own root under
# ~/snap/downloader/current/.config/Downloader — pass --root to point at it.
#
#   scripts/dev-run.sh                 build + install + run
#   scripts/dev-run.sh --no-run        build + install only
#   scripts/dev-run.sh --root <dir>    install into another plugins root (e.g. the snap's)
#   scripts/dev-run.sh -- --minimized  everything after `--` is passed to the app
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
src="$repo/src"
root="${XDG_CONFIG_HOME:-$HOME/.config}/Downloader/plugins"
run=1
app_args=()

while [[ $# -gt 0 ]]; do
  case "$1" in
    --no-run) run=0; shift ;;
    --root) root="$2"; shift 2 ;;
    --) shift; app_args=("$@"); break ;;
    *) echo "unknown option: $1" >&2; exit 2 ;;
  esac
done

# Project directory -> the plugin id it installs as (the folder name the app expects).
optional_plugins=(
  "Downloader.Desktop.Plugins.Hls:com.bezzad.hls"
  "Downloader.Desktop.Plugins.SiteMedia:com.bezzad.site-media"
  "Downloader.Desktop.Plugins.Website:com.bezzad.website-zip"
)

echo "==> Building the solution (Release)"
dotnet build "$src/Downloader.Desktop.sln" -c Release -v q --nologo

echo "==> Installing optional plugins into $root"
for entry in "${optional_plugins[@]}"; do
  project="${entry%%:*}"
  id="${entry##*:}"
  out="$src/Downloader.Desktop.Plugins/$project/bin/Release/net10.0"
  dll="$out/$project.dll"
  if [[ ! -f "$dll" ]]; then
    echo "    !! $project did not build to $dll" >&2
    exit 1
  fi

  # An assembly is cached by PATH once loaded, so the app must be restarted to pick a new copy up —
  # which is what starting it below does. Keep the destination folder name stable: it is the plugin's
  # identity on disk and where its already-downloaded tools (yt-dlp, ffmpeg, deno) are found.
  mkdir -p "$root/$id"
  cp "$dll" "$root/$id/"
  [[ -f "$out/$project.deps.json" ]] && cp "$out/$project.deps.json" "$root/$id/"
  version=$(grep -oPm1 '(?<=<Version>)[^<]+' "$src/Downloader.Desktop.Plugins/$project/$project.csproj" || true)
  echo "    $id ${version:+($version)}"
done

if [[ $run -eq 0 ]]; then
  echo "==> Skipping the app (--no-run)"
  exit 0
fi

echo "==> Starting the app (Release)"
exec dotnet run --project "$src/Downloader.Desktop/Downloader.Desktop.csproj" -c Release --no-build \
  ${app_args[@]+"${app_args[@]}"}

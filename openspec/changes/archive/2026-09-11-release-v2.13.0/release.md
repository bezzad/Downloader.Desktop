# Released in v2.13.0 (2026-09-11)

Cut from `develop` with a clean tree. `release.sh` stopped once part-way (see below) and a plain re-run
resumed and finished every channel.

## What shipped (since v2.12.0)

- **Redesigned browser-extension popup** in the app's palette: dense rows, a segmented quality band, the
  app mark, and the accent colour followed live over `/api/settings` (extension 1.20.x).
- **The proxy setting reaches every plugin** (`AppProxy` + `IPluginContext.CreateHttpClient()`), and the
  app's own update/catalog lookups.
- **Local API row**: shows the port range actually tried when nothing is bound, plus a Retry button.
- **"Install the plugin" on YouTube when it IS installed — fixed.** Diagnosed live: slow `/api/variants`
  lookups left over from earlier popups held all six of the browser's connections to the app, so the
  next popup's `/ping` and `/api/can-handle` timed out in the browser's queue and never reached the app
  (the instrumented app logged no request). Now at most 2 lookups hold a connection (oldest cancelled),
  can-handle reports `answered`, an unanswered question is retried once and then said honestly.
- **No-sound / no-picture HLS**: the popup no longer hands the app a video-only or audio-only rendition
  when the master is on the page.
- **Shutdown**: cancelling the countdown from the tray really stops it; the scheduler timer only runs
  while something is scheduled.

## Channels

- **Tag**: `v2.13.0` on `main` (`9c585de`); develop head after the mirrors `1516d17`
- **GitHub Release**: published 2026-09-11T16:58Z with curated Highlights + auto "What's Changed";
  13 assets (4 platform archives, 2 extension zips, 3 optional plugins + `plugins-catalog.json`,
  `.deb`, `.snap`, `extension-catalog.json`). `release.yml` run `34624999110` green, AUR included.
- **curl installer**: serves `releases/latest` → `v2.13.0`
- **Snap**: `snap.yml` run `34624999200` green; `latest/stable` = 2.13.0 (rev 29)
- **Homebrew**: `bezzad/homebrew-tap` at 2.13.0 (`d8308fe`; arm64 `b0ff3dc7…`, x64 `8890dd4d…`);
  in-repo mirror synced (`47a1e24`)
- **winget**: PR [microsoft/winget-pkgs#433353](https://github.com/microsoft/winget-pkgs/pull/433353)
  (awaits moderator merge); in-repo mirror bumped (`9431b8f`)
- **AUR**: `downloader-bin` at **2.13.0-1**; in-repo mirror bumped (`1516d17`)

## The run stopped silently at the Homebrew step

The first run exited 1 right after "Updating Homebrew cask" with no error line: `gh release download` of
the arm64 archive failed all five attempts at that moment, and under `set -e` the failing `"$(…)"`
assignment exited before its own `|| die` could print. A re-run resumed and completed Homebrew, winget
and the AUR mirror. `release.sh` now lets the empty value reach the `die`, so it says why next time.

## Post-merge CI

`main` (`9c585de`) `.NET Desktop` run `34624994539`: 5 of 6 legs green; `macos-latest/Release` failed one
test, `MemoryReleaseTests.A_released_stopped_row_can_be_retried_to_completion` (Completed expected,
Failed actual) — the known macOS-only flake. The parallel run `34624994723` on the same commit is fully
green, and the release carried no app-code change that could reach that path.

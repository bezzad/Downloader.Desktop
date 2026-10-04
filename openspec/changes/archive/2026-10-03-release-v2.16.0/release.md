# Released in v2.16.0 (2026-10-03)

A minor release: sidebar with Categories + Queues, title-bar search, one row context menu, an
app-drawn tray menu (works inside the snap), and detailed logging.

## What shipped

- **Sidebar** (`sidebar-queues-sections`): shown by default, Categories + Queues sections, queue
  filter, drag-to-reorder categories, resizable edge down to icons only.
- **Title-bar search** (`titlebar-search`), **row context menu** (`row-context-menu`),
  **checkbox follows selection** (`checkbox-follows-selection`), grid selection-tint fix.
- **Tray menu** (`tray-popup-menu`): app-drawn, translated; left click opens it (snap AppArmor blocks
  the native dbusmenu).
- **Detailed logging** (`detailed-logging`): user actions, settings diffs, 7-day retention, zip export.

## Pre-release CI fix

develop had been red on Windows + macOS since `6663862`; fixed in `03d4bd9` before the cut:
logging tests read the held-open log through `LogScope` (Windows sharing violation), and
`TrayService.OnClicked(bool macOS)` makes both click branches testable on every runner. The first
CI run of `03d4bd9` hit the known Avalonia headless THREAD-AFFINITY crash on macOS/Debug only;
a re-run of that leg passed (run 37127852034, all 6 legs green).

## Coordinates

| | |
|---|---|
| tag | `v2.16.0` (on `main`, `c5d1d1f`) |
| develop at cut | `46fe48d` (`chore(release): bump version to 2.16.0`) |
| release run | 37129706648 success, Snap run 37129706630 success |
| Homebrew tap | `bezzad/homebrew-tap@12f0505` (mirror `2272d99` on develop) |
| winget | PR [microsoft/winget-pkgs#446210](https://github.com/microsoft/winget-pkgs/pull/446210), awaiting a moderator (mirror `8eea9cc`); the only open PR |
| AUR | `downloader-bin` 2.16.0-1, published by CI (mirror `00bf7cf`) |
| Snap | `latest/stable` 2.16.0 |

## Verified

13 assets attached; release body carries the curated Highlights; `Release` and `Snap` workflows
green; tap cask at 2.16.0; one open winget PR; Snap Store stable = 2.16.0; AUR RPC 2.16.0-1.

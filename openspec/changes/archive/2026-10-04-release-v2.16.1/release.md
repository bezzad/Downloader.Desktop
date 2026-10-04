# Released in v2.16.1 (2026-10-04)

Bug-fix release for the Linux tray icon (reported on v2.16.0, Ubuntu GNOME, snap, two monitors).

## What shipped

- **Single click opens the tray menu** (`d74f8bc`): the GNOME AppIndicator extension handles a single
  click itself (opens the item's dbusmenu) and reports only a double click to the app. In the snap that
  menu was empty — AppArmor (`unity7`) lets the shell read a dbusmenu only at `/MenuBar…` or
  `/com/canonical/…`, and Avalonia exports it at `/net/avaloniaui/dbusmenu/<guid>`. `TrayDbusMenu` now
  serves it at `/MenuBar`.
- **Double click brings the app back** on Linux; the app-drawn popup is Windows-only.
- **Popup on the primary screen**, not on whichever monitor its hidden window last sat.
- Verified by the author on a CI-built confined snap (`snap install --dangerous`) before the release.

## Coordinates

| | |
|---|---|
| tag | `v2.16.1` (on `main`, `28ebcbc`) |
| fix commit | `d74f8bc`; its CI run 37188814144 green after re-running windows/Debug (known Avalonia headless crash) |
| release / Snap runs | both success |
| Homebrew tap | 2.16.1, checksums verified against the released archives |
| winget | PR [microsoft/winget-pkgs#446513](https://github.com/microsoft/winget-pkgs/pull/446513), awaiting a moderator |
| AUR | `downloader-bin` 2.16.1-1 |
| Snap | `latest/stable` 2.16.1 |

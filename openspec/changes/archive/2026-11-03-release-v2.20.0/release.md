# Released in v2.20.0 (2026-11-03)

Updates the download engine to Downloader 6.0.1 and carries the Windows start fix for issue #20.

## What shipped

- **Downloader 6.0.1** (`9759572`): strict server certificate check, safe server file names, no login
  details after a redirect to another site, part-request check, safer resume, range past the end of a file
  ends. 6.0.1 also keeps a `Pause()` made while the download is starting (a 6.0.0 regression found by
  `PlanRunnerTests.A_paused_controller_pauses_a_part_engine_that_joins_late`).
- **The app starts on Windows 11 versions without full CET support** (`1563f0b`, issue #20). This shipped
  in the v2.19.0 tag too, but its channels were not finished; 2.20.0 replaces them. Waiting for the reporter
  to confirm on the released win-x64 build.

## Coordinates

| | |
|---|---|
| tag | `v2.20.0` (on `main`, `d741380`) |
| update commit | `9759572`; CI run 41617446816 green on ubuntu/windows/macOS x Debug/Release |
| release / Snap runs | 41618743544 / 41618743546, both success |
| Homebrew tap | 2.20.0, `bezzad/homebrew-tap` `a7ca0b6` |
| winget | PR [microsoft/winget-pkgs#469482](https://github.com/microsoft/winget-pkgs/pull/469482), awaiting a moderator |
| AUR | `downloader-bin` 2.20.0-1 |
| Snap | `latest/stable` 2.20.0 |

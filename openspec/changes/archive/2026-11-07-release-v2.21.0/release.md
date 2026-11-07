# Released in v2.21.0 (2026-11-07)

Updates the download engine to Downloader 7.0.0 and ships browser extension 1.27.1.

## What shipped

- **Downloader 7.0.0** (`7f2d6f9`): `DownloadProgressChanged` is now raised by one deliverer, one call at a
  time and in order, so progress and speed never jump back (Downloader issue #256). A rare hang when the
  memory buffer was full is fixed.
- **Browser extension 1.27.1** (`6564e0e`): clicking an item in the list finds its video on the page more
  often, also inside embedded players.

## Coordinates

| | |
|---|---|
| tag | `v2.21.0` (on `main`, `66304bf`) |
| update commit | `7f2d6f9`; CI run 42394667193 green |
| release / Snap runs | 42395845372 / 42395845337, both success |
| Homebrew tap | 2.21.0, `bezzad/homebrew-tap` `9802f0c` |
| winget | PR [microsoft/winget-pkgs#474306](https://github.com/microsoft/winget-pkgs/pull/474306), awaiting a moderator |
| AUR | `downloader-bin` 2.21.0-1 |
| Snap | `latest/stable` 2.21.0 |

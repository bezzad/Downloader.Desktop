# Released in v2.5.0 (2026-08-24)

Three archived changes shipped together as **v2.5.0** — `dash-mpd-support` (issue #5),
`refresh-expired-link` (issue #6) and `per-download-request-context` (issue #7), all three
requested by @ray2me123 in the tail of issue #4.

- **Tag**: `v2.5.0` on `main` (merge commit `cb29b6a`, develop was 22 commits ahead of `f5b7040`)
- **Pre-release gates**: solution rebuild `0 Warning(s) / 0 Error(s)`; app suite 495/495;
  extension unit 36/36; Playwright e2e 7/7 (`--workers=1`)
- **GitHub Release**: published, curated Highlights + auto "What's Changed", 10 assets;
  `release.yml` run green (win-x64, linux-x64, osx-x64/arm64, extension zips, optional plugins +
  catalog, .deb/APT, MSIX)
- **Homebrew**: `bezzad/homebrew-tap` cask at 2.5.0
  (arm64 `f62de2ad…`, x64 `bd139b24…`); in-repo mirror synced (`83efd0e`)
- **winget**: PR [microsoft/winget-pkgs#423350](https://github.com/microsoft/winget-pkgs/pull/423350)
  (awaits moderator merge); in-repo mirror bumped (`fb6d4c2`, win-x64 sha `FBBA4B1B…`)
- **AUR**: mirror bumped (`043aade`, linux-x64 sha `fe147988…`) but **not published** — the
  `aur` job in `release.yml` warn-and-skipped again because the repo secret
  `AUR_SSH_PRIVATE_KEY` is still unset, so `downloader-bin` remains at 2.2.0.
- **Snap**: `snap.yml` triggered by the tag; publish runs in CI.

## Notes for the next release

- `release.sh` waits only for the **macOS** archives, then hashes `Downloader-win-x64.zip`
  unconditionally. On this run the Windows zip was not attached yet, so `sha256_of_asset` failed
  under `set -e` and the script exited 1 right after the Homebrew step ("winget: not started").
  Re-running the same command resumed cleanly and finished winget + the AUR mirror. Worth making
  the asset wait cover win-x64 too.
- The `extension.yml` AMO publish has failed on **every** run since 2026-07-07, independently of
  this release: AMO validation rejects the Firefox package with *"A content script defined in the
  manifest could not be found at content.js"* (plus `strict_min_version` warnings vs the
  `data_collection_permissions` key). `content.js` exists in the repo, so the packaging step is
  not including it. Firefox listings are therefore stale.

# Released in v1.6.0 (2026-07-03)

This change (local API + CLI + extension silent add) shipped as the headline of **v1.6.0**.

- **Tag**: `v1.6.0` on `main` (merge commit `bbcc837`, develop was 24 commits ahead)
- **GitHub Release**: curated Highlights + auto "What's Changed"; 4 assets
  (win-x64.zip, linux-x64.tar.gz, osx-x64/arm64.tar.gz); release.yml run green (2m10s)
- **Snap Store**: snap.yml run green → published to `latest/stable`
- **Homebrew**: `bezzad/homebrew-tap` commit "downloader 1.6.0"
  (arm64 `f2fc8ae4…`, x64 `193c25bb…`); in-repo mirror synced (`330f4fc`)
- **winget**: PR [microsoft/winget-pkgs#396966](https://github.com/microsoft/winget-pkgs/pull/396966)
  (awaits moderator merge); in-repo mirror bumped (`c47457d`)
- Released via `scripts/release.sh 1.6.0 --yes --notes-file …` (first fully-scripted run from macOS,
  after the portability fixes in `00f4067`).

# Hotfix v1.6.1 (2026-07-03)

- **Fix**: quitting from a modal dialog (Settings → "Restart to update") now closes owned dialogs
  first so the app actually exits and staged updates apply on macOS (`5e966f1`).
- Tag `v1.6.1`; Release + Snap workflows green; notes set (script's first edit attempt raced release
  creation — set manually right after, script warning is known-benign); Homebrew tap + mirror at
  1.6.1; winget PR [#396989](https://github.com/microsoft/winget-pkgs/pull/396989) opened and the
  superseded 1.6.0 PR #396966 closed per the dedup rule.

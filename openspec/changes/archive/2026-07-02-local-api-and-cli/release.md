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

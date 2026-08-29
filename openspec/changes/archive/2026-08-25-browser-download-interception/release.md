# Released in v2.6.0 (2026-08-25)

Two archived changes shipped together as **v2.6.0** — `browser-download-interception` (issue #9,
reported by @ray2me123) and `issue7-followup-fixes` (the three defects @ray2me123 found in the
v2.5.0 request-context work, plus the root cause of issue #10).

- **Tag**: `v2.6.0` on `main` (merge commit `775708d`, develop was 17 commits ahead of `cb29b6a`)
- **Pre-release gates**: solution rebuild `0 Warning(s) / 0 Error(s)`; app suite 525/525;
  extension unit 54/54; Playwright e2e 11 passed / 1 skipped (`--workers=1`)
- **GitHub Release**: [v2.6.0](https://github.com/bezzad/Downloader.Desktop/releases/tag/v2.6.0)
  published, curated Highlights + auto "What's Changed", 10 assets; `release.yml` run
  [32856197107](https://github.com/bezzad/Downloader.Desktop/actions/runs/32856197107) green
  (win-x64, linux-x64, osx-x64/arm64, extension zips, optional plugins + catalog, .deb/APT, MSIX)
- **Homebrew**: `bezzad/homebrew-tap` cask at 2.6.0
  (arm64 `91fc1340…`, x64 `6771415a…`); in-repo mirror synced (`cf38ed5`)
- **winget**: PR [microsoft/winget-pkgs#423917](https://github.com/microsoft/winget-pkgs/pull/423917)
  (awaits moderator merge); in-repo mirror bumped (`be97cd6`, win-x64 sha `B1B0F3AE…`)
- **AUR**: mirror bumped (`77121ef`, linux-x64 sha `4a65abf6…`) but **not published** — the `aur`
  job warn-and-skipped for the third release running, because the repo secret
  `AUR_SSH_PRIVATE_KEY` is still unset, so `downloader-bin` remains at **2.2.0**. The author
  confirmed up front that this was expected and asked for it to be ignored for this release.
- **Snap**: `snap.yml` run [32856196756](https://github.com/bezzad/Downloader.Desktop/actions/runs/32856196756)
  green — **revision 19** created for `downloader` and released to `latest/stable`.

## Run notes

The first `release.sh 2.6.0` invocation exited 1 at the winget step with
"winget: not started" — the same *symptom* as the v2.5.0 run, but **not** a regression of the
`WAIT_ASSETS` fix (`b320b76`). Cause: this release was driven from a fresh clone whose default
branch is `main`, which at that moment was still at v2.5.0 (`cb29b6a`) and therefore carried the
**pre-fix** `release.sh` that waited only for the two macOS archives. It proceeded to the winget
step while `Downloader-win-x64.zip` was still building. Once the merge landed, the resume run
picked up develop's fixed script, correctly reported "all platform archives are attached", and
completed winget + the AUR mirror. Exit code 0.

Take-away for future releases run from a clone: the merge to `main` happens *during* the run, so
the script executing the post-tag steps is whatever version the working tree had when bash read
it. Running from an up-to-date `develop` checkout avoids this entirely.

A second, unrelated stumble: the environment injects a global git `insteadOf` rewrite pointing at
a **1-hour** `ghs_` token, which had expired by the time the release started, so the very first
attempt failed at "Bumping version" with `Invalid username or token` and pushed nothing. Pointing
`origin` at a URL built from a live `gh auth token` fixed it. Nothing was published by that
attempt — no tag, no merge, no release.

## Issue follow-up

Issues #9 and #10 are to be replied to (not closed) once every channel is verified, telling
@ray2me123 the fixes are in v2.6.0 so he can confirm against his own encrypted sources.

---

# Follow-up: released in v2.6.1 (2026-08-26)

The v2.6.0 extension zips were **unloadable**. @ray2me123 reported on issue #9 that installing the
v2.6.0 extension failed with `Could not load options page 'options.html'. Could not load manifest.`

**Root cause** — packaging, not extension code. `scripts/build-extension.sh` zips an explicit
allow-list (`COMMON=(background.js common.js content.js popup.html popup.css popup.js icons)`).
This change added the options page to the source and to `options_ui.page` in *both* manifests but
never added `options.html` / `options.css` / `options.js` to that array, so the published archive
held 11 files with no options page while the bundled manifest still pointed at one. A browser
rejects the whole extension when a manifest-referenced page is missing. Confirmed by listing the
actual released `downloader-extension-chrome.zip` asset.

**Fix** (`5178741`): added the three files to `COMMON`, and added `verify_zip()` — after each zip is
built it extracts every path the manifest references and asserts each is present, failing the
release job otherwise. Verified it fires by re-introducing the omission (exit 1,
`missing: options.html`). Extension bumped to **1.3.1** in both manifests.

- **Tag**: `v2.6.1` on `main` (merge `bf652cb`); driven from an up-to-date `develop` clone this time,
  so the post-tag steps ran develop's fixed `release.sh` (see the v2.6.0 take-away above) — the asset
  wait correctly blocked on `Downloader-win-x64.zip`.
- **Gates**: extension unit 54/54; Playwright e2e 11 passed / 1 skipped. No C# changed, so the app
  suite was not re-run.
- **GitHub Release**: [v2.6.1](https://github.com/bezzad/Downloader.Desktop/releases/tag/v2.6.1),
  10 assets, curated notes; run
  [32951316105](https://github.com/bezzad/Downloader.Desktop/actions/runs/32951316105) — all 10 jobs green.
- **Verified fix in the published artifact**: both `downloader-extension-{chrome,firefox}.zip`
  downloaded from the release contain `options.html`/`.css`/`.js`, manifest version `1.3.1`.
- **Snap**: run [32951316133](https://github.com/bezzad/Downloader.Desktop/actions/runs/32951316133)
  green — **revision 20**, `latest/stable`, confirmed 2.6.1 via the store API.
- **Homebrew**: tap at 2.6.1 (arm64 `93724136…`, x64 `bea11896…`); mirror `615be44`.
- **winget**: PR [microsoft/winget-pkgs#424433](https://github.com/microsoft/winget-pkgs/pull/424433)
  open; mirror `70c996d` (win-x64 sha `06DA1246…`). The v2.6.0 PR #423917 was **merged**, so there
  was no stale PR to dedup.
- **AUR**: mirror bumped (`93ca942`), publish warn-skipped again (`AUR_SSH_PRIVATE_KEY` unset) —
  `downloader-bin` stays at **2.2.0**. The author asked for this to be ignored.

## Run notes — two winget issues worth fixing

1. **`submit_winget` lost a manifest to a race.** The step reported
   "winget: manifest upload failed — skipping PR". Only `bezzad.Downloader.installer.yaml` and
   `bezzad.Downloader.locale.en-US.yaml` reached the fork branch; the *first* PUT
   (`bezzad.Downloader.yaml`) failed, almost certainly because the branch ref created immediately
   before it had not yet propagated. A plain retry of the identical call succeeded, and the PR was
   then opened by hand. The three `gh api -X PUT` calls in `submit_winget` swallow stderr
   (`>/dev/null 2>&1`) and are not wrapped in `retry()` — they should be, and the failure should
   report *which* file failed.
2. **The fork can no longer sync.** `gh api -X POST repos/bezzad/winget-pkgs/merge-upstream` returns
   **422**: "refusing to allow an OAuth App to create or update workflow
   `.github/workflows/manifest-validation-diagnosis.lock.yml` without `workflow` scope". Harmless
   today (the call is `|| true` and the branch is cut from the fork's existing master), but the fork
   drifts further from upstream every release. Fix by re-authenticating `gh` with the `workflow`
   scope, or by syncing the fork through the web UI.

Neither blocked the release; both are unfixed as of this record.

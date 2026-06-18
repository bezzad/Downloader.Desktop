# PLAN.md

This is the living plan for Downloader.Desktop. It is kept current and committed
on `develop` so that work can continue seamlessly across machines. Read this
file (and `TASKS.md`) at the start of every session before doing anything else.

**Standing rules (see `CLAUDE.md` → "Workflow & progress tracking" for full text)**:
invoke the `downloader-desktop` skill before starting; work only on `develop`;
write Clean Code/KISS — simplest solution, no speculative abstractions; on
failure, mark `[!]` in Blocked/Failed with the reason and commit+push immediately
so any machine/AI picks up the true last state.

**Last updated**: 2026-06-19
**Branch**: develop
**Now working on**: Issue 1 — fix false "update available" (version-reporting bug)

Author decisions (2026-06-19 overnight batch): may cut releases autonomously; Issue 1 = just fix the false alarm (don't touch update/download mechanism); Queues = full "real queue manager" redesign (mockup approved); Instagram = R&D plan only, no code.

## Status legend
- `[ ]` todo
- `[~]` in progress
- `[x]` done
- `[!]` blocked / failed

## Active
- [~] **Issue 1 — false "Update available" on latest version.** Root cause: `UpdateService.CurrentVersion` reads `AssemblyVersion`, which csproj pins to `$(VersionPrefix).0.0` = `1.1.0.0`, so the app always reports `1.1.0` and treats the `v1.1.1` release as newer forever. Also the app version shown in About doesn't match the release. Fix (scope: false alarm only, per author): make the app report its real 3-part version and stamp it from the release tag in CI so they can't drift. Then cut a release so the live brew app stops false-alarming. NOT changing the download/apply mechanism this round.

## Todo
- [ ] **Issue 2 — Queues page redesign into a real queue manager.** Current page = static cards listing item names + status badges only. Approved design (mockup): per-queue card with live aggregate stats (running/waiting/done + total speed), combined progress bar, start/pause-whole-queue toggle, concurrency cap, and an items list with per-item progress + pause/resume/remove + reorder (up/down priority) + move between queues. Files: `Views/QueuesView.axaml(.cs)`, `ViewModels/QueuesViewModel.cs` (+ `QueueRowViewModel`), wire aggregates off `DownloadManager`/`DownloadItemViewModel`.
- [ ] **Issue 3 — Instagram video download R&D (plan only).** Engine fetches direct HTTP+range URLs only; IG needs a media extractor (resolve CDN URL, handle login-wall/anti-bot). Deliver a written plan + trade-offs (bundle yt-dlp+ffmpeg vs .NET libs vs IG API), no code. Relates to the deferred m3u8/HLS+YouTube item.

## Done
- [x] **Fixed macOS app invisible in Spotlight + closing when terminal closes.** Root cause: the Homebrew cask installed a bare Unix binary (`binary "Downloader"`) — macOS never indexes it and it runs as a foreground terminal child. Fix: ship a real `Downloader.app` bundle. Added `scripts/make-macos-app.sh` (wraps the self-contained binary + Info.plist + icns), wired into `release.yml` + `publish.sh` for `osx-*`, cask → `app "Downloader.app"`, README/CONTRIBUTING updated — c602e17. Cut **release v1.1.1** (CI built all 4 platforms, macOS as `.app`), filled the cask's real per-arch sha256, pushed to `bezzad/homebrew-tap` (commit 652c1c4). **Verified live on this Mac**: `brew install --cask downloader` → `/Applications/Downloader.app`, found by Spotlight (`mdfind`) + LaunchServices, launches detached (parent PID 1). cask sha commit on develop — see below.
- [x] Set up cross-machine task tracking: PLAN.md, TASKS.md, CLAUDE.md workflow section — 53ec993
- [x] Remove private full-name path from settings screenshots: sanitized sample `DefaultSavePath` + de-hardcoded screenshot `OutDir` in `CaptureScreenshots.cs`, regenerated all 7 PNGs — 4dc44b2. Note: the old string remains in git history on all branches (already public on GitHub) — author chose to leave history as-is rather than rewrite/force-push.
- [x] Codified permanent standing rules in CLAUDE.md (Clean Code/KISS, invoke `downloader-desktop` skill before starting, always record failures in PLAN/TASKS for cross-machine visibility); resolved the resulting conflict with the old "never commit automatically" line; added pointers in PLAN.md/TASKS.md headers — 1ef9a1a
- [x] Diagnosed + fixed non-working winget/Homebrew install commands: confirmed `bezzad/homebrew-tap` (404) and the winget-pkgs manifest (404) were never actually published; corrected README to stop presenting them as ready and explain why; filled in real version+sha256 (was `1.0.0`/placeholder) in `Casks/downloader.rb` + `packaging/winget/*.yaml` from the real v1.1.0 release assets — 588f505.
- [x] **Published the Homebrew tap — `brew install --cask downloader` now works.** Created public repo `github.com/bezzad/homebrew-tap` via `gh`, pushed `Casks/downloader.rb` (v1.1.0 + real per-arch SHA) + README there. **Verified end-to-end on this Mac**: `brew tap bezzad/tap` → `brew install --cask downloader` → real arm64 Mach-O binary linked at `/opt/homebrew/bin/Downloader` (then uninstalled the test). Note: newer Homebrew requires `brew trust bezzad/tap` before install — documented in both READMEs. Main-repo README restored the working `brew` command + trust note — bec765f.

## Blocked/Failed
- (none)

## Waiting on external review
- [~] winget `bezzad.Downloader` v1.1.0 — **PR microsoft/winget-pkgs#390226**, CLA signed (`license/cla: SUCCESS`). Now in Microsoft's automated validation (downloads installer, verifies SHA256, Windows-sandbox install test) + moderator review — entirely on their side, can take hours–days. **`winget install downloader` starts working on Windows only once this PR is merged.** If validation fails, check the PR's Azure pipeline link / labels for the reason and push a fix to fork branch `bezzad:bezzad.Downloader-1.1.0`. Manifests also kept in-repo at `packaging/winget/` for the next version bump.

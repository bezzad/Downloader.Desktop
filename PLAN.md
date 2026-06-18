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
**Now working on**: (idle) — all 3 issues handled; v1.1.3 live via brew (Issue 1 + 2), Instagram R&D delivered

Author decisions (2026-06-19 overnight batch): may cut releases autonomously; Issue 1 = just fix the false alarm (don't touch update/download mechanism); Queues = full "real queue manager" redesign (mockup approved); Instagram = R&D plan only, no code.

## Status legend
- `[ ]` todo
- `[~]` in progress
- `[x]` done
- `[!]` blocked / failed

## Active
- (none)

## Todo
- [ ] (author, when awake) Interactively launch `/Applications/Downloader.app` (Spotlight/double-click) to eyeball the new Queues page. Note: a real GUI launch is expected to work — the only failure seen was an `Avalonia.Native` render-timer error (-6661) when launching from this background/headless shell, which happens at platform bootstrap BEFORE any app code and is purely "no window-server session", not a code bug. Verified instead via build + 66 tests (incl. real-App headless render of Queues) + screenshots.
- [ ] **Issue 3 — Instagram video download R&D (plan only).** Engine fetches direct HTTP+range URLs only; IG needs a media extractor (resolve CDN URL, handle login-wall/anti-bot). Deliver a written plan + trade-offs (bundle yt-dlp+ffmpeg vs .NET libs vs IG API), no code. Relates to the deferred m3u8/HLS+YouTube item.

## Done
- [x] **Issue 3 — Instagram video download R&D (plan only).** Delivered `docs/instagram-rnd.md`: IG links are HTML pages, not files; the engine only fetches direct HTTP+range URLs, so a media extractor is needed. Evaluated yt-dlp-bundling (recommended, also unlocks YouTube/TikTok/HLS — the deferred roadmap item) vs .NET libs (none robust) vs official API (doesn't expose arbitrary public videos) vs DIY scraper (fragile). Flagged the Meta-ToS/legal angle (app is published under author's name). Not simple → no code, per author's "plan only". Decision points listed for the author. Commit pending below.
- [x] **Issue 2 — Queues page redesigned into a real queue manager.** Was static cards (names + badges). Now: per-queue card with live aggregate stats (downloading/waiting/done/failed + total speed), combined progress bar, run/pause toggle, concurrency cap, and the queue's downloads with per-item progress + pause/resume/retry/cancel/remove + reorder (up/down priority) + move-between-queues menu (shown only when >1 queue). Added `MoveToQueue`/`MovePriority` to the manager; `Initialize` backfills `QueueId` for legacy items; `QueueItemViewModel`/`QueueMoveTarget` wrappers; 3 new icons + i18n keys; +2 tests (66 green); queues-dark/light screenshots. Commit 92234e9. Verified both themes via screenshot capture. Released as v1.1.3 (see Todo to push to brew). Detail in SKILL.md.
- [x] **Issue 1 — fixed false "Update available" on the latest version.** Root cause: `AssemblyVersion` was pinned to `$(VersionPrefix).0.0` = `1.1.0.0`, so the app always reported `1.1.0` and saw the `v1.1.1` release as newer forever; About also showed the 4-part `InformationalVersion`. Fix: `VersionPrefix` is now the full 3-part semver (`1.1.2`), `AssemblyVersion=$(VersionPrefix).0` (real patch), `release.yml` stamps `-p:VersionPrefix=<tag-without-v>` so a build always reports its tag, and About (`SettingViewModel.AppVersion`) shows `UpdateService.CurrentVersion` so About+check+tag agree. +1 regression test (app must not flag its own release). Commit f7056d7; cut **release v1.1.2** (CI stamped `1.1.2` into the bundle, verified); tap cask → 1.1.2 + real SHAs. **Verified live**: `brew upgrade` 1.1.1→1.1.2, `/Applications/Downloader.app` reports `1.1.2`. Scope per author: false-alarm only; update download/apply mechanism unchanged (note: the self-updater would still mis-handle the new `.app` layout, but with versions matching it won't be triggered — left for a later round).
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

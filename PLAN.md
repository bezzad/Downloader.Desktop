# PLAN.md

This is the living plan for Downloader.Desktop. It is kept current and committed
on `develop` so that work can continue seamlessly across machines. Read this
file (and `TASKS.md`) at the start of every session before doing anything else.

**Standing rules (see `CLAUDE.md` → "Workflow & progress tracking" for full text)**:
invoke the `downloader-desktop` skill before starting; work only on `develop`;
write Clean Code/KISS — simplest solution, no speculative abstractions; on
failure, mark `[!]` in Blocked/Failed with the reason and commit+push immediately
so any machine/AI picks up the true last state.

**Last updated**: 2026-06-18
**Branch**: develop
**Now working on**: (idle) — winget PR #390226 CLA signed; now in Microsoft's validation/review queue (out of our hands)

## Status legend
- `[ ]` todo
- `[~]` in progress
- `[x]` done
- `[!]` blocked / failed

## Active
- (none)

## Todo
- (none)

## Done
- [x] Set up cross-machine task tracking: PLAN.md, TASKS.md, CLAUDE.md workflow section — 53ec993
- [x] Remove private full-name path from settings screenshots: sanitized sample `DefaultSavePath` + de-hardcoded screenshot `OutDir` in `CaptureScreenshots.cs`, regenerated all 7 PNGs — 4dc44b2. Note: the old string remains in git history on all branches (already public on GitHub) — author chose to leave history as-is rather than rewrite/force-push.
- [x] Codified permanent standing rules in CLAUDE.md (Clean Code/KISS, invoke `downloader-desktop` skill before starting, always record failures in PLAN/TASKS for cross-machine visibility); resolved the resulting conflict with the old "never commit automatically" line; added pointers in PLAN.md/TASKS.md headers — 1ef9a1a
- [x] Diagnosed + fixed non-working winget/Homebrew install commands: confirmed `bezzad/homebrew-tap` (404) and the winget-pkgs manifest (404) were never actually published; corrected README to stop presenting them as ready and explain why; filled in real version+sha256 (was `1.0.0`/placeholder) in `Casks/downloader.rb` + `packaging/winget/*.yaml` from the real v1.1.0 release assets — 588f505.
- [x] **Published the Homebrew tap — `brew install --cask downloader` now works.** Created public repo `github.com/bezzad/homebrew-tap` via `gh`, pushed `Casks/downloader.rb` (v1.1.0 + real per-arch SHA) + README there. **Verified end-to-end on this Mac**: `brew tap bezzad/tap` → `brew install --cask downloader` → real arm64 Mach-O binary linked at `/opt/homebrew/bin/Downloader` (then uninstalled the test). Note: newer Homebrew requires `brew trust bezzad/tap` before install — documented in both READMEs. Main-repo README restored the working `brew` command + trust note — bec765f.

## Blocked/Failed
- (none)

## Waiting on external review
- [~] winget `bezzad.Downloader` v1.1.0 — **PR microsoft/winget-pkgs#390226**, CLA signed (`license/cla: SUCCESS`). Now in Microsoft's automated validation (downloads installer, verifies SHA256, Windows-sandbox install test) + moderator review — entirely on their side, can take hours–days. **`winget install downloader` starts working on Windows only once this PR is merged.** If validation fails, check the PR's Azure pipeline link / labels for the reason and push a fix to fork branch `bezzad:bezzad.Downloader-1.1.0`. Manifests also kept in-repo at `packaging/winget/` for the next version bump.

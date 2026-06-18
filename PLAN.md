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
**Now working on**: (idle — nothing in progress)

## Status legend
- `[ ]` todo
- `[~]` in progress
- `[x]` done
- `[!]` blocked / failed

## Active
- (none)

## Todo
- [ ] (add upcoming tasks here as they're identified)

## Done
- [x] Set up cross-machine task tracking: PLAN.md, TASKS.md, CLAUDE.md workflow section — 53ec993
- [x] Remove private full-name path from settings screenshots: sanitized sample `DefaultSavePath` + de-hardcoded screenshot `OutDir` in `CaptureScreenshots.cs`, regenerated all 7 PNGs — 4dc44b2. Note: the old string remains in git history on all branches (already public on GitHub) — author chose to leave history as-is rather than rewrite/force-push.
- [x] Codified permanent standing rules in CLAUDE.md (Clean Code/KISS, invoke `downloader-desktop` skill before starting, always record failures in PLAN/TASKS for cross-machine visibility); resolved the resulting conflict with the old "never commit automatically" line; added pointers in PLAN.md/TASKS.md headers — 1ef9a1a
- [x] Diagnosed + fixed non-working winget/Homebrew install commands: confirmed `bezzad/homebrew-tap` (404) and the winget-pkgs manifest (404) were never actually published; corrected README to stop presenting them as ready and explain why; filled in real version+sha256 (was `1.0.0`/placeholder) in `Casks/downloader.rb` + `packaging/winget/*.yaml` from the real v1.1.0 release assets — 588f505. Actually publishing (create the tap repo, submit winget-pkgs PR) still needs the author's go-ahead — asked, awaiting answer.

## Blocked/Failed
- (none)

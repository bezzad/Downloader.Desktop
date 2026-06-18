# TASKS.md

Full backlog board for Downloader.Desktop. Complements `PLAN.md` (which tracks
only what's currently Active/Todo/Done/Blocked at a glance). Keep this updated
for larger backlogs; status markers match `PLAN.md`.

Standing rules live in `CLAUDE.md` → "Workflow & progress tracking" (skill-first,
`develop`-only, Clean Code/KISS, always record failures here so other machines
see the true last state). Read both files before starting any task.

Status legend: `[ ]` todo · `[~]` in progress · `[x]` done · `[!]` blocked/failed

| Status | Task | Files/Notes | Commit |
|--------|------|--------------|--------|
| [x] | Set up cross-machine task tracking | PLAN.md, TASKS.md, CLAUDE.md (Workflow & progress tracking section) | 53ec993 |
| [x] | Remove private full name from README settings screenshots | `src/Downloader.Desktop.Tests/CaptureScreenshots.cs` (sanitized sample `DefaultSavePath`, de-hardcoded `OutDir`), regenerated `docs/screenshots/*.png`. History on `main`/other branches still has the old string — author opted to leave history as-is. | 4dc44b2 |
| [x] | Codify standing rules (Clean Code/KISS, skill-first, failure tracking) | `CLAUDE.md` (Conventions + Workflow & progress tracking + resolved commit-policy conflict), `.claude/skills/downloader-desktop/SKILL.md` (commit-policy note), `PLAN.md`/`TASKS.md` header pointers | 1ef9a1a |
| [x] | Fix non-working macOS/Windows install commands | `README.md` (Quick install rewritten), `Casks/downloader.rb` + `packaging/winget/*.yaml` (real v1.1.0 version + sha256), `CLAUDE.md` (Round 14 entry). | 588f505 |
| [x] | Publish Homebrew tap (macOS install works) | Created `github.com/bezzad/homebrew-tap` (public) with `Casks/downloader.rb` + README; verified `brew install --cask downloader` end-to-end on this Mac (arm64 binary linked, then removed). README restored the working brew command + tap-trust note. | _pending_ |
| [ ] | Submit winget PR to microsoft/winget-pkgs (v1.1.0) | Manifests ready+validated in `packaging/winget/`; `gh` authed. Paused at author checkpoint before forking/​PR'ing Microsoft's repo — awaiting explicit go. | — |

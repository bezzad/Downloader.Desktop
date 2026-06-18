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
| [x] | Publish Homebrew tap (macOS install works) | Created `github.com/bezzad/homebrew-tap` (public) with `Casks/downloader.rb` + README; verified `brew install --cask downloader` end-to-end on this Mac (arm64 binary linked, then removed). README restored the working brew command + tap-trust note. | bec765f |
| [x] | macOS: ship `.app` bundle (fix Spotlight + terminal-detach) | `scripts/make-macos-app.sh` wraps binary → `Downloader.app`; `release.yml`+`publish.sh` for `osx-*`; cask `app "Downloader.app"` v1.1.1 + real sha256; README/CONTRIBUTING updated. Cut release v1.1.1 (CI, all 4 platforms); tap commit 652c1c4. Verified live: `/Applications/Downloader.app`, Spotlight-indexed, launches detached. | c602e17 |
| [x] | Issue 1: fix false "update available" | csproj VersionPrefix=full semver + AssemblyVersion=$(VersionPrefix).0; release.yml stamps version from tag; About shows CurrentVersion; +regression test. Released v1.1.2, verified live via brew (app reports 1.1.2). | f7056d7 |
| [x] | Issue 2: Queues redesign (real queue manager) | Aggregate stats + combined progress + per-item progress/actions + reorder + move-between-queues. QueuesViewModel/View, MoveToQueue/MovePriority on manager, QueueId backfill, 2 new tests, screenshots. Released v1.1.3. | 92234e9 |
| [~] | Submit winget PR to microsoft/winget-pkgs (v1.1.0) | **PR microsoft/winget-pkgs#390226** (fork `bezzad/winget-pkgs`, branch `bezzad.Downloader-1.1.0`, 3 manifests via `gh api`). CLA signed (SUCCESS). Now in Microsoft's automated validation + moderator review — `winget install downloader` works on Windows once merged. | #390226 |

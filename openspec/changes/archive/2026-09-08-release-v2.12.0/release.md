# Released in v2.12.0 (2026-09-08)

Cut from `develop` with no active OpenSpec change open — the two changes it ships
(`queue-naming-and-default`, `github-release-asset-picker`) had both been synced and archived earlier
the same day, and the working tree was clean. `release.sh` ran unattended end to end and every channel
came back green on the first pass.

## What shipped (since v2.11.0)

- **Queues you can name and choose** (`queue-naming-and-default`). A queue rename now propagates through
  one seam (`IDownloadManager.RenameQueue`) instead of leaving stale copies in the toolbar's start/stop
  menus and on the download rows until a restart. "New queue" asks for the name first (Enter/Esc) and
  scrolls the new card into view, so the click can no longer look like it did nothing. A batch add
  pre-fills a queue name from what the links' file names share (`QueueNameSuggester`), and the Queues
  page lets the user pick which queue is the default for new downloads.
- **GitHub release links resolve to the asset that was actually pasted**
  (`github-release-asset-picker`). One parser answers `CanResolve`, the variant listing and the resolve,
  so the Add window names the asset and its size, offers a picker when a release carries several, honours
  the tag in the link (path *and* the `#release-<tag>` anchor), and stops claiming links it cannot improve
  (direct asset, issues, PRs, wiki, tree, commits, actions) — which used to answer them all with the
  latest release's tarball.
- **The suite no longer posts real desktop notifications** (`5edf294`) and builds the Avalonia app once
  per assembly rather than per test, and the CI `Test` step is time-bounded (`d1598f4`) so a hang keeps
  its log and artifacts instead of losing them to the job timeout.

## Channels

- **Tag**: `v2.12.0` on `main` (`da49658`); develop head at release `29a6b9d`
- **Pre-release gate**: `release.sh`'s CI gate verified `.NET Desktop` green on the exact commits before
  tagging — develop `5edf294` and main `0eaba8a`. No local test run this session.
- **GitHub Release**: published 2026-09-08T15:06Z with curated Highlights + auto "What's Changed";
  **12 assets** (4 platform archives, 2 extension zips, 3 optional plugins + `plugins-catalog.json`,
  `.deb`, `extension-catalog.json`). `release.yml` run `34242512847` — all 10 jobs green, AUR included.
- **curl installer**: serves `releases/latest` → `v2.12.0`
- **Snap**: `snap.yml` run `34242512925` green (stable channel)
- **Homebrew**: `bezzad/homebrew-tap` at 2.12.0 (`d38b873`; arm64 `f14fcf29…`, x64 `014e7258…`);
  in-repo mirror synced (`37387c8`)
- **winget**: PR [microsoft/winget-pkgs#431403](https://github.com/microsoft/winget-pkgs/pull/431403)
  (awaits moderator merge); in-repo mirror bumped (`f075004`, win-x64 sha `56AFAF43…`)
- **AUR**: `downloader-bin` published at **2.12.0-1** by the `aur` job (linux-x64 sha `3fff2c7c…`);
  in-repo mirror bumped (`29a6b9d`)

## Post-merge CI: the hang is still there, and it is intermittent

The runs *after* the merge/tag were not green on the first attempt, and this is worth recording because
it is the only reason this release needed hand-holding:

- `main` (`da49658`) run `34242510183`: 4 of 6 legs green, `windows-latest/Debug` and
  `macos-latest/Release` sat in the `Test` step until the timeout killed them (conclusion `cancelled`).
- develop head (`29a6b9d`) run `34242942442`: `windows-latest/Debug` and `macos-latest/Debug` the same way.

Re-running the failed legs fixed both — `main` went fully green on the first re-run, develop needed a
second re-run for `windows/Debug` alone. **Both branches are now green on the released commits.** That a
leg which hung passes untouched on re-run is the evidence that this is the known flaky hang (see
`.claude/skills/downloader-desktop/SKILL.md` → the CI hang got worse after PerAssembly, every test leaks
a timer), not a failure introduced by this release. Root-causing it is still open work.

# Released in v2.11.0 (2026-09-06)

Shipped from `develop` after a **half-finished release** left by an earlier session: `2.11.0` was
already bumped (`cf66d13`) and `develop` was already merged into `main` (`db70e41`), but the tag had
never been pushed. `release.sh`'s preflight refused that shape outright — *"develop has no commits
beyond main and VersionPrefix is already 2.11.0 — nothing to release"* — with every channel left at
"not started". The guard was asking about branch geometry when it meant content; `93ca8b8` makes it ask
whether anything landed since the previous tag, and makes the merge and the tag steps idempotent, so a
run that dies between the merge and the tag is now recovered by re-running the same command. This
release is the first exercise of that path: it resumed cleanly and tagged.

## What shipped (since v2.10.0)

- **Connections are a ceiling, not a demand** (`issue #14`). A refusal halves the count for the next
  attempt (8 → 4 → 2 → 1) instead of collapsing to one, and where a download settles is remembered per
  host (clamped by the user's setting, expires after a week, cleared by a success at the ceiling).
  Every guard the v2.9.0 recovery earned is kept. Wording in all 16 language packs.
- **GitHub releases asset picker** (`8d8e2de`, plugin 1.0.0 → 1.1.0). One parser (`GitHubLink.Parse`)
  answers `CanResolve`, the variant listing and the resolve, so they cannot disagree: the tag in the
  link is honoured (path *and* the `#release-<tag>` anchor), every asset is offered as a variant with
  the OS match pre-selected, and links the resolver cannot improve (direct asset, issues, PRs, wiki,
  tree, commits, actions) are no longer claimed.
- **The engine is never disposed from inside its own completion event** (`648e9df`). `Dispose()` is
  `Clear().Wait()` and `Clear()` waits on the semaphore the running `StartDownload` holds — reached
  FROM that operation's own completion event through `OnUi`, on the UI thread. In the app that froze
  the window as a download finished; in CI the test host went silent and the run aborted on the
  3-minute inactivity timer, blaming whichever test came next.
- **The intermittent "test host process crashed"** (`003f8bc`) — 33 of the last 60 runs, a different
  innocent test each time, never reproducible under a plain `dotnet test`. The missing ingredient was
  `--settings coverlet.runsettings`: coverlet's module-unload hook resolves a type THROUGH a
  collectible context that is unloading, which throws on the unload thread with no user code above it
  and kills the process. An unloading context now answers "not mine".

## Channels

- **Tag**: `v2.11.0` on `main` (`0eaba8a`); develop head at release `0db2102`
- **Pre-release gate**: CI `.NET Desktop` green on the exact commits tagged — `93ca8b8` (develop head)
  and `db70e41` (main head at the time). `release.sh`'s CI gate verified both before tagging; no local
  test run was performed this session.
- **GitHub Release**: published 2026-09-06T06:23Z with curated Highlights + auto "What's Changed";
  **13 assets** (4 platform archives, 2 extension zips, 3 optional plugins + `plugins-catalog.json`,
  `.deb`, `.snap`, `extension-catalog.json`). `release.yml` run `34016329603` — every job green,
  AUR included.
- **curl installer**: serves `releases/latest` → `v2.11.0`
- **Snap**: `snap.yml` run `34016329631` green (stable channel)
- **Homebrew**: `bezzad/homebrew-tap` at 2.11.0 (`b779da3`; arm64 `a3cdeff4…`, x64 `63d4c3d9…`);
  in-repo mirror synced (`744fe15`)
- **winget**: PR [microsoft/winget-pkgs#430329](https://github.com/microsoft/winget-pkgs/pull/430329)
  (awaits moderator merge); in-repo mirror bumped (`3772e8e`, win-x64 sha `05D7834B…`)
- **AUR**: `downloader-bin` published at **2.11.0-1** by the `aur` job (linux-x64 sha `f5d7a2a0…`);
  in-repo mirror bumped (`0db2102`)

## Still open

`openspec/changes/github-release-asset-picker` remains active: every task is done except **4.5** — the
author's by-hand check that the Add window lists the release's assets with the Linux build
pre-selected, against the link from the report. The code is in v2.11.0; the change archives once that
check passes.

# Released in v2.14.0 (2026-09-22)

A feature release: downloads are filed by type, finished ones can be archived instead of deleted, the
window remembers its layout, and interrupted streaming downloads resume instead of restarting. Minor
rather than patch because two capabilities are new and one behaviour is breaking (see below). Cut from
`develop` with a clean tree; `release.sh` ran unattended end to end on the second attempt.

## What shipped

Three OpenSpec changes, all synced into the living specs and archived before the release was cut:

- **`categorize-downloads-by-type` (issue #16)** — file type stopped being decoration. The eight built-in
  kinds became editable, reorderable `DownloadCategory` records (config schema v1 → v2, seeded so the
  upgrade is a visual no-op apart from the new Type column). `Services/CategoryService` is the single
  authority; resolution is the user's choice → extension → `Content-Type` → Other. Optional sidebar, off
  by default. Settings export/import carries settings + categories with no filesystem paths, so a file
  written on Linux imports on Windows.
- **`archive-downloads` (issue #17)** — archiving is a separate axis from status, not a seventh bucket.
  The invariant everything rests on: an archived download is never running or queued (`Archive` stops
  first; `Start`/`Resume`/`Retry` un-archive first), enforced in the manager because bulk actions bypass
  any view-level guard. New capability `download-archive`.
- **`remember-main-window-state` (issue #15)** — size and position restored across restarts, including
  the fix for the restore's own echo being mistaken for the user resizing.

Fixes carried in the same release:

- **HLS/DASH stop no longer restarts from 0%** — the cancel path deleted the whole `.parts` scratch
  folder, and a second bug returned before `MarkPartDone`, so finished segments were thrown away twice
  over.
- **HLS segments download concurrently again** — the parallel gate was `pending.All(Kind == Segment)`, so
  ONE non-segment part (exactly what DASH produces with separate video/audio representations) forced the
  entire plan serial. Replaced with a partition, and the concurrency now follows the user's `ChunkCount`
  instead of a hard-coded 4.
- **A blocked connection is no longer reported as an expired session** — `NeedsSession` matched the bare
  substring `"age"`, which also matches "Unable to download API **page**". A network reset reaching
  YouTube told the user to reload the page in their browser, which can never fix it.
- **Engine 5.9.7 → 5.9.8** — fixes the NRE that showed a download complete on disk as
  "Failed - Object reference not set to an instance of an object" after a retry-following-stop, and stops
  a `Dispose()` racing a completion from swallowing the completion event.
- Status-bar total now counts archived downloads (archiving kept the file, so the number dropping read as
  lost data); "Check for updates" reports what it found; "Clear filters" resets the controls as well as
  the list; a proxy is never applied to loopback.

**BREAKING (behavioural), carried from the two feature changes:** "select all" now covers only the rows
the filters are showing (it previously acted on the whole list, so Remove could delete downloads the user
could not see), and an archived unfinished download is no longer resumed by "start all".

## Coordinates

| | |
|---|---|
| tag | `v2.14.0` (on `main`) |
| release merge | `95a70a1` (`release: v2.14.0 (merge develop)`) |
| develop at cut | `70545c1`, CI green on all 6 legs (run 35701804582) |
| engine | `Downloader` 5.9.8 |
| Homebrew tap | `bezzad/homebrew-tap@0bb1e2a` (mirror `f96c9f6` on develop) |
| winget | PR [microsoft/winget-pkgs#438835](https://github.com/microsoft/winget-pkgs/pull/438835), open, awaiting a moderator (mirror `fe47368`) |
| AUR | `downloader-bin` 2.14.0-1, published by CI (mirror `29323a5`) |
| Snap | `latest/stable` 2.14.0, revision 31 |

## Verified

Every channel was checked rather than taken from the script's report: 13 assets attached (the four
platform archives, both extension zips, three optional-plugin zips, both catalogs, the `.deb` and the
`.snap`); release body carries the curated Highlights; `Release` and `Snap` workflows green; tap cask at
2.14.0; winget PR open; AUR RPC reports 2.14.0-1; `snap info` reports 2.14.0.

## Note for the next release

`release.sh`'s CI gate refused the first attempt with *"no CI run for develop@70545c1"* while a run for
exactly that commit was in flight and visible to the same `gh run list` query run by hand, both with and
without `GH_TOKEN`. The gate swallows query failures (`2>/dev/null || true`) and cannot tell "the API did
not answer" from "there are no runs", so a transient API error reads as the latter and burns the 120 s
grace. Waiting for CI to finish and re-running went straight through. If this recurs it is worth making
that branch distinguish an empty answer from a failed call — the gate is right to be strict, but it
should not be able to refuse for a reason that is not true.

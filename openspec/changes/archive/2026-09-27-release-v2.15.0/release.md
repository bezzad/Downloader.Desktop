# Released in v2.15.0 (2026-09-27)

A minor release: a new scheduler time picker (new UI), plus layout and extension fixes.

## What shipped

- **Scheduler 24-hour range dial** (`c1b1428`) — replaces the two spinner `TimePicker`s. Drag the
  start (solid) and stop (hollow) handles; the arc is the download window, the center reads the range
  and its length; 1-minute steps by drag, arrow keys or wheel (Shift: 15). A new schedule starts at the
  next whole hour with a one-hour window (both handles visible, and it no longer fires the moment it is
  created). The dial never mirrors in RTL languages.
- **Downloads grid fits with the category sidebar open** (`c6141b1`) — tightened column widths and,
  when still too narrow, drops Time left → Queue → Type instead of crushing the fixed columns.
- **Extension: popup thumbnails paired by media id** (`44d3d7f`, from a parallel session).
- Docs: README hero is now the category-sidebar screenshot; README/scheduler screenshots refreshed
  (`67abc2a`, `c6141b1`).

## Coordinates

| | |
|---|---|
| tag | `v2.15.0` (on `main`) |
| release merge | `e396910` (`release: v2.15.0 (merge develop)`) |
| develop at cut | `a8a17e4` (`chore(release): bump version to 2.15.0`); `c6141b1` CI green before the cut |
| release run | 36330556047 (all 10 jobs success), Snap run 36330556090 success |
| Homebrew tap | `bezzad/homebrew-tap@950cb33` (mirror `9fa91f7` on develop) |
| winget | PR [microsoft/winget-pkgs#442220](https://github.com/microsoft/winget-pkgs/pull/442220), open, awaiting a moderator (mirror `c26c383`); the only open PR |
| AUR | `downloader-bin` 2.15.0-1, published by CI (mirror `afe92d4`) |
| Snap | `latest/stable` 2.15.0, revision 33 |

## Verified

13 assets attached (four platform archives, both extension zips, three optional-plugin zips, both
catalogs, the `.deb`, the `.snap`); release body carries the curated Highlights; `Release` and `Snap`
workflows green; tap cask at 2.15.0; exactly one open winget PR; `snap info` reports 2.15.0 (rev 33);
AUR RPC reports 2.15.0-1 (it lagged the CI push by a few minutes).

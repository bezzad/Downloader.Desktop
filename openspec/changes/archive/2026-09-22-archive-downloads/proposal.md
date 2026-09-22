## Why

The downloads list only grows. Today the sole way to shorten it is **Remove**, which destroys the
record — so a user who wants a clean working list has to forget what they already downloaded
(reported as issue #17). Archiving keeps the record and its file, and takes the row out of the way.

## What Changes

- A download can be **archived**: it leaves the main list (and every status filter, including *All*)
  but keeps its record, its file and its history.
- A seventh **Archived** filter pill in the footer shows the archived rows; the list can be
  **restored** from there at any time.
- The **per-row action strip** gains an Archive icon; in the archived view that same slot is
  Restore.
- The **toolbar** gains an Archive button beside Remove, enabled by selection exactly like
  Start/Pause/Stop. While the Archived filter is active the action cluster becomes
  **Restore + Remove** — the same mechanism that already swaps the toolbar when a management page
  opens.
- An archived download is **never active**: archiving a running row stops it first, and starting /
  resuming / retrying an archived row un-archives it.
- New setting **"Delete the partial file when removing a download"** (default **off**, today's
  behaviour). When on, Remove also deletes the half-finished `<name>.download`. A completed file is
  never touched.
- **BREAKING (behavioural):** archived rows are excluded from *All*, from the queue pump, from
  bulk Start/Stop, from the Queues page and from the footer speed/count totals. A user who archives
  an unfinished download will not see it resume on "Start all" any more.

## Capabilities

### New Capabilities
- `download-archive`: archiving and restoring a download, the archived view, and the rules that keep
  an archived download inert.

### Modified Capabilities
- `downloads-list`: the footer filter set gains Archived, and the toolbar action cluster changes
  with the active filter.
- `download-status`: "Stopped items appear under All" is narrowed — an archived item appears under
  no status filter.
- `settings`: a new option controls whether Remove also deletes the partial file.

## Impact

- `Models/DownloadItem.cs` (`IsArchived`), `Models/DownloadSettings.cs` (the new option).
- `Services/DownloadManager.cs` — `Archive`/`Unarchive`, the un-archive-on-start rule, and the
  archived exclusion in `PumpQueue`/`StartAll`/`StopAll`/stats/`Remove`.
- `ViewModels/{MainViewModel,DownloadsViewModel,DownloadItemViewModel,QueuesViewModel,SettingViewModel}`.
- `Views/{MainWindow,DownloadsView,SettingView}.axaml` + `Assets/Icons.axaml`.
- All 16 language packs in `Assets/i18n/`.
- `docs/screenshots/` (footer and toolbar change).

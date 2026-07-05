## Why

When adding a link, users often want to drop it into a **new** queue (e.g. "Series S01") — but the
Add Link dialog only offers the existing queues in its picker, so they must cancel, open Queues,
create one, and start over. The author wants queue creation available right in the Add flow.

## What Changes

- `AddDownloadItemView` gets an **"Add queue" button** placed to the **left of the Add button**, with
  the same visual style as the Add button (per the author's spec).
- Clicking it prompts for a queue name (small inline input or mini-dialog), creates the queue via the
  existing `IDownloadManager.AddQueue` path, refreshes the dialog's queue picker, and **selects the
  new queue** so the link being added lands in it.
- The queue picker becomes visible when there is more than one queue (it already is) — creating the
  first extra queue from the dialog makes it appear immediately.

## Capabilities

### Modified Capabilities
- `add-download`: the Add dialog can create a new queue inline and assign the new download(s) to it.

## Impact

- `src/Downloader.Desktop/Views/AddDownloadItemView.axaml(.cs)` — the new button + name prompt.
- `src/Downloader.Desktop/ViewModels/AddDownloadItemViewModel.cs` — `AddQueueCommand`, picker refresh,
  selection; needs an `IDownloadManager` (or callback) handle to create the queue.
- i18n keys in ALL 16 language packs.
- Tests: creating a queue from the dialog adds it to config, refreshes `Queues`, selects it, and the
  started download carries the new `QueueId`.

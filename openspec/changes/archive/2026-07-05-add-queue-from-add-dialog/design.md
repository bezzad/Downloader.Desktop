## Context

`AddDownloadItemViewModel` already exposes `Queues` (from `Config`) + `SelectedQueue` + a
`ShowQueuePicker` (>1 queue) and returns `DownloadItem`s with `QueueId = SelectedQueue?.Id`.
Queue creation lives in `DownloadManager.AddQueue` (raises `QueuesChanged`, seeds `MaxConcurrent`
from Settings). The dialog VM currently has no manager handle — it only gets `Config`.

## Goals / Non-Goals

**Goals:** create + select a new queue without leaving the Add dialog; the started download lands in it.
**Non-Goals:** editing/removing queues here (Queues page owns that); per-queue settings in the dialog.

## Decisions

- **Give the dialog VM the manager**: `AddDownloadItemViewModel` gains an optional
  `IDownloadManager manager` ctor param (MainViewModel passes `_downloadManager`; tests pass a real
  manager or leave null → button hidden). Creating through the manager (not raw `Config.Queues.Add`)
  keeps `QueuesChanged`/pump wiring consistent.
- **Inline name row, not a nested dialog**: clicking "Add queue" reveals a small inline
  TextBox + confirm ("✓")/cancel row above the actions; Enter confirms. Avoids stacking modals.
- **Button style/placement**: same classes as the Add (accent) button, placed immediately to its left
  in the actions row (author's spec).
- After creation: refresh `Queues` (`RaisePropertyChanged`), set `SelectedQueue` to the new queue,
  raise `ShowQueuePicker` so the picker appears when this was the second queue.

## Risks / Trade-offs

- [Duplicate names] → allow (queues are id-keyed; the Queues page already tolerates same names), but
  trim/ignore empty input.

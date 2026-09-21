## 1. Model and persistence

- [ ] 1.1 Add `DownloadItem.IsArchived` (bool, persisted, default false)
- [ ] 1.2 Surface it on `DownloadItemViewModel` with the write-through pattern used by `Status`
- [ ] 1.3 Add `DownloadSettings.DeletePartialFileOnRemove` (bool, default false)
- [ ] 1.4 Test: the archived flag round-trips through a config save/load

## 2. Manager rules (the invariant)

- [ ] 2.1 `DownloadManager.Archive(vm)` — cancel first when Running/Paused/queued, then set the flag
- [ ] 2.2 `DownloadManager.Unarchive(vm)` — clear the flag only (no state change)
- [ ] 2.3 Clear the flag at the top of `Start`, `Resume` and `Retry`
- [ ] 2.4 Test: archiving a running row stops it and the pump does not restart it
- [ ] 2.5 Test: starting/resuming/retrying an archived row un-archives it
- [ ] 2.6 Test: no path leaves a row both archived and Running/Created

## 3. Archived rows take no part in anything

- [ ] 3.1 `PumpQueue` skips archived items when picking the next one to start
- [ ] 3.2 `StartAll` / `StartQueue` skip archived items (including the Stopped/Failed re-queue step)
- [ ] 3.3 `StopAll` / `StopQueue` skip archived items
- [ ] 3.4 `TotalSpeed` and the cumulative downloaded total exclude archived items
- [ ] 3.5 `QueuesViewModel` excludes archived items from queue rows and from every aggregate count
- [ ] 3.6 Test: an archived unfinished row is not started by "start all" or by starting its queue
- [ ] 3.7 Test: an archived row is absent from the queue card and from its counts

## 4. Filtering

- [ ] 4.1 Add `StatusFilter.Archived`
- [ ] 4.2 `DownloadsViewModel.Matches`: archived filter returns archived rows; every other filter (incl. All) rejects them
- [ ] 4.3 `MainViewModel`: `ShowArchivedCommand`, `IsArchivedSelected`, `ArchivedFilterCount`
- [ ] 4.4 Exclude archived items from `AllCount` and the five existing `*FilterCount` properties
- [ ] 4.5 Re-raise the new/changed counts wherever the existing ones are raised
- [ ] 4.6 Test: pill counts match the rows each pill shows, with archived rows present
- [ ] 4.7 Test: search narrows the archived view

## 5. Remove and the partial file

- [ ] 5.1 `DownloadManager.Remove` deletes the `<final>.download` sidecar when the setting is on
- [ ] 5.2 Never delete a completed file, whatever the setting
- [ ] 5.3 Test: off leaves the partial file; on deletes it; a completed file always survives

## 6. UI

- [ ] 6.1 Add the archive/restore icon geometries to `Assets/Icons.axaml`
- [ ] 6.2 Row action strip: Archive icon, shown as Restore while the archived view is active
- [ ] 6.3 Toolbar: Archive button beside Remove, enabled by `HasSelection` like Start/Pause/Stop
- [ ] 6.4 Toolbar: swap the cluster to Restore + Remove while the Archived filter is active
- [ ] 6.5 Footer: the Archived pill with its count
- [ ] 6.6 Settings: the "delete the partial file when removing" toggle
- [ ] 6.7 Test: the toolbar cluster follows the active filter, and both clusters hide on a management page

## 7. Wording

- [ ] 7.1 Add the new keys to `en.json` (archived filter, archive/restore actions, the new setting)
- [ ] 7.2 Translate them into the other 15 language packs
- [ ] 7.3 Test: every pack carries the new keys

## 8. Finish

- [ ] 8.1 `dotnet build Downloader.Desktop.sln -t:Rebuild` — 0 warnings, 0 errors
- [ ] 8.2 Full `dotnet test` green (bounded run per the skill file)
- [ ] 8.3 Regenerate the affected screenshots and check them by eye
- [ ] 8.4 Update `CLAUDE.md` (roadmap entry) and the skill file with anything a future session would re-derive

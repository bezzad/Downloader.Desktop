# Tasks: ux-queue-combo-name-fixes

## 1. Live queue menus
- [x] 1.1 Add `event Action QueuesChanged` to `DownloadManager`; raise it at the end of `AddQueue` and `RemoveQueue`.
- [x] 1.2 In `DownloadsViewModel` ctor, subscribe to `QueuesChanged` and re-raise `StartQueueTargets`, `StopQueueTargets`, `ShowQueue` on the UI thread.
- [x] 1.3 Add a unit test asserting `QueuesChanged` fires on `AddQueue` and `RemoveQueue`.

## 2. ComboBox padding
- [x] 2.1 In `App.axaml`, add `Padding="10 6"` to the global `ComboBox` style and an inset on `ComboBoxItem`.

## 3. Full name on hover
- [x] 3.1 Add `NameTooltip` to `DownloadItemViewModel` (full `DisplayName`, plus error line when failed).
- [x] 3.2 In `DownloadsView.axaml`, bind the Name cell `ToolTip.Tip` to `NameTooltip`.

## 4. Verify
- [x] 4.1 `dotnet build` clean; `dotnet test` green.
- [x] 4.2 Regenerate `docs/screenshots/` (UI changed) and eyeball them.
- [x] 4.3 Append any non-obvious pattern to the `downloader-desktop` skill; commit + push to `develop`.

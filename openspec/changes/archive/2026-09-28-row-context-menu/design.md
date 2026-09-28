## Context

- The grid (`DownloadsView.axaml`) has a grip column, a checkbox column, a Type column
  (`SortMemberPath="CategoryOrder"`), Name (with a `ContextMenu` on its StackPanel: Copy URL, Open
  file, Open folder, Category ▸, plugin action, Remove), Queue, Size, Progress, Speed, Time left, and
  a 192 px action column.
- Row commands live on `DownloadItemViewModel` (`Pause/Resume/Cancel/Retry/Remove/Archive/Unarchive/
  OpenFolder/OpenFile/CopyUrl/PostAction` + `CategoryChoices`). Bulk commands live on
  `DownloadsViewModel` and use `SelectedTargets()`; state rules are guarded in `DownloadManager`.
- `DownloadManager.MoveToQueue(vm, queueId)` exists. `QueueActionTarget` / `QueueMoveTarget` show the
  "dynamic MenuFlyout of runtime items" pattern (skill note: an `ObservableCollection` rebuilt in place).
- Details window = `DialogHelper.ShowDetails` (also opened by double-click).

## Goals / Non-Goals

**Goals:** a clean row, one complete menu on the selection, Restart, Copy as JSON/cURL, shortcuts.

**Non-Goals:** a file checksum item; a separate Edit dialog (Properties covers it); a menu title
line; changing toolbar buttons.

## Decisions

1. **The menu belongs to the page VM, not the row.** It acts on the selection, so its commands are on
   `DownloadsViewModel` (reuse the existing bulk commands where they exist: Start/Resume, Pause, Stop,
   Archive, Restore, Remove). The row VM's per-row commands stay for the Details window and tests.
   One `ContextMenu` defined once for the grid, `DataContext` = the page VM.
2. **Right-click selects first.** On right-button press over a row that is not selected, the view
   selects only that row (via the grid, so the checkbox sync from step 2 applies). On a selected row,
   the selection is kept.
3. **"Resume" means continue-or-retry.** For each target: Failed → `Retry`, otherwise `Resume`. The
   manager already guards states, so the menu does not re-implement rules. Enabled when any target is
   Paused/Stopped/Failed/queued-idle.
4. **Enabled rules** (any target qualifies): Open = a completed target; Open folder = always; Pause =
   a running target; Stop = a running/paused/queued target; Restart = a non-completed, non-running
   target; Archive = a non-archived target; Restore = an archived target (Archive/Restore swap by
   the archived view, like the toolbar); Delete = always; Properties = exactly one target. Command
   `canExecute` observables recompute on selection change and on the targets' status change.
5. **Restart = new manager method `Restart(vm)`.** Stop if active → delete ONLY `<final>.download`
   (the same partial-only delete `DeletePartialFileOnRemove` uses — NOT `DiscardPartialFile`, which also
   deletes the final file) → reset `Downloaded`/`Progress`/`PlanJson`/`VariantId`-keep → re-queue
   through `PumpQueue` (never `Start` directly — keeps the concurrency cap). Refused for Completed.
6. **Copy formats are a pure static helper**, multi-row aware:
   - Link: one URL per line (primary URL).
   - JSON: an array of `{ url, mirrors, fileName, folder, size }` (`System.Text.Json`, indented).
     No cookies, headers, `CookieFilePath`, `PlanJson`.
   - cURL: one line per row, `curl -L -o '<file>' '<url>'` plus `-e '<referer>'` when set, POSIX
     single-quote escaping (`'` → `'\''`). Unit-tested for quotes, spaces, unicode, empty referer.
   Clipboard write via `ClipboardExtensions.SetTextAsync` (Avalonia 12 extension — skill note).
7. **Shortcuts**: `InputGesture` on each `MenuItem` for display, and matching `KeyBinding`s on the
   grid (Ctrl+O, Ctrl+R, Ctrl+P, Ctrl+C, Delete, Ctrl+I). On macOS the gesture uses Cmd
   (`KeyModifiers.Meta`) via the platform hotkey configuration. Ctrl+C is bound on the grid only, so it
   does not steal copy from text boxes.
8. **Delete keeps today's Remove behaviour** (same confirmation and the `DeletePartialFileOnRemove`
   setting) — it is the toolbar Remove command.
9. **Name cell**: `PathIcon` (FileKind → icon, CategoryColor → brush, tooltip CategoryName) + the name
   text, one line. The grid's Name column keeps `MinWidth` so the sidebar cannot squeeze it.

## Risks / Trade-offs

- [Losing sort by type] → accepted by the author (the sidebar now filters by category).
- [Discoverability: new users may not know about right-click] → the toolbar keeps Start/Pause/Stop/
  Archive/Remove and double-click still opens Details.
- [Headless screenshot of an open context menu] → a popup renders in its own top level; capture the
  popup's root (or its `MenuFlyoutPresenter`) instead of the window. If that cannot be made reliable,
  the capture is taken from a test that hosts the menu's presenter in a plain window.
- [Ctrl+C on macOS is Cmd+C] → use the platform hotkey helper, tested by gesture comparison not by OS.

# Proposal: ux-queue-combo-name-fixes

## Why

Three concrete UX defects reported by the author that make the main window feel broken:

1. **Queue start/stop menus go stale.** After adding a new queue, the "Start queue ▾" / "Stop queue ▾"
   dropdowns in the downloads toolbar still list only the old queues — the new one never appears until the
   app is closed and reopened. The menus are bound to computed properties that never re-evaluate when the
   queue set changes.
2. **ComboBox text touches the edge.** Every ComboBox renders its selected text flush against the control's
   left/top border with no breathing room, which looks unfinished. The app's global ComboBox style sets only
   a hand cursor and no padding.
3. **Long file names are unreadable.** When a download's name is wider than the Name column it trims with an
   ellipsis and there is no way to see the rest — the cell's tooltip is currently wired to the (usually empty)
   error message, not the name. The user can only ever read the beginning of a long name.

## What Changes

1. **Live queue menus** — the download manager raises a `QueuesChanged` event when a queue is added or removed;
   `DownloadsViewModel` subscribes and re-raises `StartQueueTargets`/`StopQueueTargets` (and `ShowQueue`) so the
   toolbar dropdowns reflect the current queues immediately, no restart.
2. **ComboBox padding** — the global `ComboBox` style gains consistent inner padding so the selected text and
   the dropdown items sit inside the control, not on its edge. Applies app-wide (Settings, Add dialog, etc.).
3. **Full name on hover** — the downloads grid Name cell shows the complete file name (plus the error reason
   when failed) in a tooltip on hover, so a trimmed long name is fully readable.

## Impact

- Affected specs: `queues` (new), `downloads-list` (new), `ui-theme` (new).
- Affected code: `Services/DownloadManager.cs` (new event), `ViewModels/DownloadsViewModel.cs` (subscribe +
  re-raise), `App.axaml` (ComboBox padding), `Views/DownloadsView.axaml` (Name cell tooltip).
- No data-model or persistence changes; no engine changes. Small, localized, low-risk.
- Tests: a unit test asserting the manager fires `QueuesChanged` on add/remove.

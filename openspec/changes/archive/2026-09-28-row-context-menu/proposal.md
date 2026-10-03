## Why

Each downloads-grid row carries a 192 px strip of up to eight small icon buttons, plus a separate
70 px Type column. Together they take a third of the grid, the icons are hard to tell apart, and the
right-click menu (only on the Name cell) duplicates part of them. The author wants the row to show
just information — a type icon beside the name — and every action in one right-click menu on the
row. (Step 3 of 4; builds on `checkbox-follows-selection`: the menu acts on the selected rows.)

## What Changes

- **Remove the Type column.** The category icon (in the category's color, tooltip = category name)
  moves to the left of the file name in the Name cell. **BREAKING (behavioural):** "sort by type" goes
  away with the column.
- **Remove the per-row action column** (pause / resume / retry / stop / plugin action / open / open
  folder / archive / restore / remove icons).
- **One right-click menu on the whole row** (not only the Name cell). It has no title line. It acts on
  **the selected rows**; right-clicking a row that is not selected first selects only that row.
  Items, in order:
  - **Open** (Ctrl+O) · **Open folder**
  - **Resume** (Ctrl+R; retries a failed row) · **Pause** (Ctrl+P) · **Stop** · **Restart download**
  - **Move to queue ▸** (only when there is more than one queue) · **Category ▸** (existing)
  - **Copy ▸** → Copy link (Ctrl+C) · Copy as JSON · Copy as cURL
  - plugin action (e.g. "Add to Ollama", only when offered — existing)
  - **Archive** / **Restore** · **Delete** (Del)
  - **Properties** (Ctrl+I) — opens the Details window; enabled only when exactly one row is selected
- Items that do not apply to any selected row are shown **disabled** (like the toolbar), not hidden —
  except Move to queue (hidden with one queue) and the plugin action (hidden when not offered).
- **New action: Restart download** — throws away the partial file and downloads again from 0 %.
  Not offered for a completed download (it would have to delete the finished file).
- **New actions: Copy as JSON / Copy as cURL.** They never include cookies or request headers (those
  are secrets and are never saved); the referer is included because it is already saved.
- The keyboard shortcuts shown in the menu also work on the grid while it has focus.

## Capabilities

### New Capabilities
- `download-row-menu`: the right-click menu on a download row — items, targets, enabled rules,
  shortcuts, Restart, Copy as JSON / cURL.

### Modified Capabilities
- `downloads-list`: the Type column is removed and the Name cell shows the type icon; the per-row
  action strip is removed.
- `download-archive`: per-row Archive/Restore moves from the action strip to the right-click menu.
- `category-sidebar`: the icon-choice requirement no longer mentions the grid's Type column.

## Impact

- `Views/DownloadsView.axaml(.cs)` — columns removed, icon in the Name cell, row context menu
  (via a `DataGridRow` style or the grid's `ContextMenu`), right-click-selects-row, grid key bindings.
- `ViewModels/DownloadsViewModel.cs` — menu commands over the selection (open, resume/retry, pause,
  stop, restart, move-to-queue targets, copy link/JSON/cURL, archive/restore, delete, properties) and
  their enabled state.
- `Services/DownloadManager.cs` (+ `IDownloadManager`) — `Restart(vm)`.
- New pure helper for the copy formats (e.g. `Services/DownloadCopyFormat.cs`).
- All 16 language packs in `Assets/i18n/` (new menu labels).
- `docs/screenshots/` — grid captures change; a new capture with the menu open.

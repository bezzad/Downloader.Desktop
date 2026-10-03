## Context

- The sidebar (`MainWindow.axaml`) is an `ItemsControl` of `CategoryRowViewModel` rows (All + each
  category) with a right-click menu (Edit, Move up/down, Delete) and an "Add" button, visible when
  `IsCategorySidebarOpen && IsDownloadsSelected`. `Button.cat.selected` = translucent
  `RowSelectionBrush` + semibold.
- `MainViewModel.SelectedCategoryId` ↔ `DownloadsViewModel.CategoryFilter`; category counts are
  computed with `MatchesExceptCategory` (so they respect status + search).
- Queues: `Config.Queues`, `IDownloadManager.QueuesChanged` (raised by Add/Remove/Rename — skill
  note), `DownloadItemViewModel.QueueId`.
- `CategoryService.Move(id, delta)` exists (±1).
- `Config.SchemaVersion` + `EnsureValid` migration pattern exists (v1 flipped the browser toggle once).

## Goals / Non-Goals

**Goals:** sections, queue filter, one selection, accent bar, drag reorder of categories, on by default.

**Non-Goals:** reordering queues from the sidebar; queue actions (start/stop/rename) in the sidebar;
persisting which section is collapsed; AB-style "Finished / Unfinished" groups (the footer status pills
already do that).

## Decisions

1. **One filter "slot" in the shell.** `MainViewModel` keeps `SelectedCategoryId` and adds
   `SelectedQueueId`; setting one clears the other; All clears both. `DownloadsViewModel` gains
   `QueueFilter` checked in `Matches` next to `CategoryFilter`. Row `IsSelected` flags are refreshed
   from these two values in one place (same pattern as today's category rows).
2. **Queue counts use the same "all filters except my own dimension" rule** as category counts:
   a `MatchesExceptSidebar` (status + search, not category/queue) so every number is what clicking it
   will show. Archived rows follow the existing status-filter rules.
3. **Sidebar queue row VM** `SidebarQueueRowViewModel { Id, Name, Count, IsSelected, SelectCommand }`,
   rebuilt from `_config.Queues` on `QueuesChanged` (post to the UI thread like the category rows).
   If the selected queue no longer exists → select All.
4. **Sections are two `Expander`-like headers** (a header button with a chevron + an
   `IsVisible`-bound panel), not the Fluent `Expander` control, to keep the compact sidebar look.
   Expanded state = two VM bools, default true, not persisted (YAGNI).
5. **Accent bar** = a 3 px `Border` at the row's start edge, `Background={DynamicResource
   SystemAccentColor}`, visible only when selected (style on `Button.cat.selected`). Because it binds
   the dynamic accent resource, `ThemeService.ApplyAccent` recolors it live. `FlowDirection` mirrors it
   in RTL. The translucent selected background stays.
6. **Hover grip + drag** on category rows only (not All, not queues). The grip `PathIcon`
   (`GripDotsRegular`) is visible on `Button.cat:pointerover` (and while dragging). Drag uses the same
   manual pointer-capture approach as the grid (the OS DragDrop session shows no visual on X11 — skill
   note): press on the grip captures, move highlights the target row, release calls
   `CategoryService.MoveTo(id, targetIndex)` (new; clamps so nothing lands above All) and persists.
   A floating ghost is used only if the grid's ghost code can be shared without copying it; otherwise
   the drop-target highlight is enough.
7. **On by default + one-time migration.** `IsCategorySidebarOpen` defaults to true for new configs;
   `CurrentSchemaVersion` 2 → 3 and `EnsureValid` sets it to true once when `SchemaVersion < 3`. After
   that the user's choice is respected (same pattern as the v1 browser-integration flip).

## Risks / Trade-offs

- [Sidebar width 204 px vs long queue names] → names trim with an ellipsis and show a tooltip.
- [Grip + count pill in a narrow row] → the grip sits at the row end, beside the pill; checked in the
  hover capture.
- [A test can't hover headlessly in a way that shows up] → assert the grip's `IsVisible` via the
  `:pointerover` pseudo-class set programmatically, and use a capture for the visual.
- [Existing users who deliberately closed the sidebar see it once] → accepted; it is one toggle to
  close again and the choice then sticks.

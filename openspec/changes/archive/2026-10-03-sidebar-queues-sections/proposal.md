## Why

The sidebar only filters by category, and queues can only be seen on the separate Queues page. The
author wants one sidebar that lists both, like a folder tree: an expandable **Categories** section and
an expandable **Queues** section. Clicking a queue shows only its downloads. The selected row should be
easy to spot (an accent bar on its left edge), and categories should be re-sortable by dragging.
Because the sidebar becomes the main way to move between these views, it is shown by default.
(Step 4 of 4.)

## What Changes

- The sidebar has **two collapsible sections**: **Categories** (All + every category + "Add") and
  **Queues** (every queue with its count). Each section header has a chevron; both start expanded.
- **Selecting a queue filters the list to that queue's downloads.** It combines with the status
  filter and the search box, exactly like a category.
- **One selection across both sections:** picking a queue clears the category filter, picking a
  category clears the queue filter, and **All** clears both.
- The selected row (category or queue) shows a **vertical bar on its left edge in the theme's accent
  color** (it follows the accent picker and mirrors to the right edge in RTL languages).
- Hovering a category row (not All) shows a **6-dot drag handle**; dragging it up/down reorders the
  categories and the new order is saved. Move up / Move down stay in the right-click menu.
- **The sidebar is shown by default.** **BREAKING (behavioural):** existing users see it once after
  the update (one-time migration of the saved "hidden" value); after that their choice is kept. The
  toggle button stays.
- Queue rows follow live changes: a new, renamed or removed queue appears/updates/disappears at once;
  removing the selected queue falls back to All.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `category-sidebar`: shown by default; sectioned (Categories + Queues); queue filter; one selection;
  accent selection bar; drag-to-reorder categories.

## Impact

- `Views/MainWindow.axaml` — sidebar layout (two sections), accent bar, hover grip, drag handlers
  (code-behind, same manual pointer-capture pattern as the grid's row drag).
- `App.axaml` — `Button.cat` selected/hover styles (accent bar, grip visibility on hover).
- `ViewModels/MainViewModel.cs` — `QueueRows`, section expanded flags, single-selection logic,
  `ClearFilters` also clears the queue.
- New `ViewModels/QueueRowViewModel`-like sidebar row (name must not clash with the Queues page's
  `QueueRowViewModel` — e.g. `SidebarQueueRowViewModel`).
- `ViewModels/DownloadsViewModel.cs` — `QueueFilter` in `Matches`, per-queue counts.
- `Services/CategoryService.cs` — move a category to a target position (drag drop).
- `Models/Config.cs` — `CurrentSchemaVersion` 2 → 3 with the one-time "show sidebar" migration.
- All 16 language packs (section titles if new keys are needed).
- `docs/screenshots/` — sidebar captures (queues, selected accent bar, hover grip), light + dark.

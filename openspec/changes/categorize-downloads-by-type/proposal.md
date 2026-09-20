## Why

As a download list grows, every kind of file is mixed together and finding one download becomes
work (issue #16). The app already knows each file's kind — it draws a type icon in every Name cell —
but that knowledge is decorative: it cannot be filtered on, cannot be sorted on, and the user cannot
correct it when the guess is wrong. This change turns file type from decoration into a first-class,
user-owned dimension of the list.

## What Changes

- **Categories become data, not a hardcoded switch.** The eight built-in kinds (Video, Audio, Image,
  Document, Archive, App, Disc, Other) become editable records the user can rename, recolor,
  re-icon, reorder, and extend with categories of their own. `DownloadItemViewModel.GetFileKind`'s
  static `switch` is replaced by a `CategoryService` reading the persisted list.
- **An optional left sidebar** lists the categories with per-category counts. It is **off by
  default** and toggled by a menu button placed immediately left of the "Paste download link" box;
  its open/closed state persists across restarts. Clicking a category filters the whole downloads
  list; an "All" row clears that one dimension.
- **A new sortable Type column** in the downloads grid, placed immediately before Name, showing the
  category icon with a tooltip naming the category. The type icon is **removed from the Name cell**
  (it moves here). Sorting is by the user's own category order, not alphabetically.
- **Per-item category override.** `DownloadItem` gains a `CategoryId` field. `null` means "detect
  automatically"; any other value is the user's explicit choice and wins over detection. The user
  sets it in the Add dialog, in the Details window, and from the row's right-click menu.
- **Detection gains a MIME fallback.** Resolution order becomes: user override → file extension →
  `Content-Type` → Other. **This requires a change in the sibling `Downloader` engine repo**: adding
  a `ContentType` property to `RemoteFileInfo`, populated from response headers the engine already
  fetches and currently discards. The browser extension's existing `mime` field — sent on every
  hand-off today and ignored by the app — is read on the `/api/add` path.
- **Settings export/import.** A settings file the user can carry between machines, containing
  settings + categories only (no download data, no queues, no schedules). Deliberately free of file
  paths and platform-specific data so a file exported on Linux imports cleanly on Windows.
- **BREAKING (behavioral):** the grid's "select all" checkbox now selects only the rows currently
  visible under the active filters, not the entire download list. Previously — with no category
  filter to hide rows — the distinction never arose; with one, the old behavior would delete
  downloads the user cannot see.

## Capabilities

### New Capabilities
- `download-categories`: the category model itself — built-in and user-defined categories, their
  ordering and the precedence that ordering confers, how a download's category is resolved
  (override → extension → MIME → Other), and the user's ability to override it per download.
- `category-sidebar`: the optional left sidebar — its toggle, persistence, contents, counts,
  category management (add / edit / reorder / delete) and its role as a filter.
- `settings-portability`: exporting and importing user settings + categories as a
  platform-independent file, and the rules that keep an import non-destructive.

### Modified Capabilities
- `downloads-list`: a new sortable Type column, the type icon leaving the Name cell, and "select
  all" scoping to the visible rows.
- `settings`: the sidebar's persisted open/closed state, the persisted category list, and the
  export/import entry points.
- `local-api`: `/api/add` accepts and stores the `mime` value the browser extension already sends.

## Impact

- **App** — new `Services/CategoryService`, new category models on `Config`, `DownloadItem.CategoryId`,
  `Config.SchemaVersion` bump with a migration that seeds the built-in categories for existing
  configs, `DownloadsViewModel.Matches` gaining a third filter dimension,
  `FileKindToIconConverter` generalized to resolve an icon key plus a color, `MainWindow.axaml`
  (toggle button + sidebar), `DownloadsView.axaml` (Type column), Add dialog, Details window, row
  context menu, Settings page (export/import), and the 16 language packs.
- **Engine (`../Downloader`, separate repo)** — `RemoteFileInfo.ContentType` added and populated in
  `SocketClient.GetFileInfoAsync`. Additive only, not a breaking change, but it needs its own NuGet
  release **before** the app task that consumes it.
- **Browser extension** — no change required; the app starts reading a field the extension already
  sends.
- **Dead code removed** — `MainViewModel.IsSidebarExpanded` / `SidebarWidth` / `ToggleSidebarCommand`
  survive from the nav rail deleted in `71793e5`; the three-state width logic is dropped and the
  toggle is repurposed for the two-state category sidebar.
- **Docs/screenshots** — the main window layout changes, so `docs/screenshots/` must be regenerated.

## Design proposals considered

Two proposals were put forward during exploration; the change adopts the author's, with four
additions from the second.

**The author's proposal (adopted as the core).** Put a menu button to the left of the "Paste
download link" box, toggled off on first run. Turning it on opens a sticky left sidebar listing each
category by name and its own icon, with a low-contrast "+ Add" entry under the list for creating a
new one. Move the type icon out of the Name cell into a dedicated Type column carrying a tooltip, so
the list can also be sorted by type. Categories are stored in settings along with the sidebar's
state, are exportable to another machine, are reorderable, and a click on one filters the entire
list — not just completed downloads — with an "All" entry to leave the filter.

**The second proposal (four additions, all adopted).**

1. *Two sidebar states, not three.* The deleted nav rail had hidden / icons-only / expanded, and
   that extra state was part of what made it confusing (its removal commit says so outright). The
   category sidebar is open or closed, nothing between.
2. *A count beside every category.* Without one, the user must click a category to learn whether it
   is empty. Empty categories stay listed but dimmed — hiding and re-showing them as downloads
   arrive would make the sidebar twitch. Counts respect the active status filter, so each number
   means exactly what clicking it will show.
3. *"Change category" in the row's right-click menu.* The menu already exists, so the cost is
   near-zero, and it is the only way to re-categorize several downloads in a row without opening and
   closing the Details window for each one.
4. *Category order carries three meanings at once.* The position the user drags a category to is
   simultaneously its place in the sidebar, the sort key for the Type column (an alphabetical sort
   would order by internal English keys and read as nonsense in Persian or Arabic), and the
   precedence used when two categories claim the same extension — first match by order wins. One
   concept the user already understands, doing three jobs, instead of three separate mechanisms.

**Rejected:** giving each category its own destination folder. It is a natural extension of the
model and nearly free in the data layer, but the author was explicit that this feature tags data and
does not move files. Noted here only so a future session does not re-derive it as new.

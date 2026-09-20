## Context

The app already computes a file "kind" for every download — `DownloadItemViewModel.GetFileKind` is a
static `switch` over the extension returning one of eight strings, and
`Converters/FileKindToIconConverter` maps that string to one of eight hardcoded `Geometry` paths
drawn in the Name cell. Nothing else consumes it: it cannot be filtered on, sorted on or corrected.

The downloads list is filtered today through `DataGridCollectionView.Filter` pointing at
`DownloadsViewModel.Matches`, which combines a `StatusFilter` (the footer pills) with the search box.
Adding a category filter means adding a third dimension at that same choke point.

A left nav rail existed until commit `71793e5`, which deleted it because "the left menu confused
users" — it mixed status filters, page navigation and management links in one column. Its view model
plumbing (`IsSidebarExpanded`, `SidebarWidth`, `ToggleSidebarCommand`) is still in `MainViewModel`,
unused, with a test still exercising it. The sidebar this change introduces is deliberately narrower
in purpose: one job, off by default.

Three constraints from the author shape the design:

- Categories **tag** data. They never move files or change a download's destination folder.
- Category names are **user data**, stored verbatim, never translated — a user running the app in
  English may well name their categories in Japanese.
- Most users do not install the browser extension, so MIME detection cannot depend on it.

## Goals / Non-Goals

**Goals:**

- File type becomes a filterable, sortable, user-correctable dimension of the download list.
- The built-in categorization the app already performs is preserved exactly, but becomes editable
  data rather than compiled-in logic.
- A user's categories and settings move between machines and operating systems without breaking.
- Nothing about the list's behavior surprises the user — in particular, a bulk action never touches
  a download that is filtered out of view.

**Non-Goals:**

- Per-category destination folders. Explicitly ruled out by the author; recorded in the proposal so
  it is not re-derived later.
- Uploading a custom image as a category icon. Out of scope this round; the export schema reserves a
  field so adding it later does not invalidate existing export files.
- Pattern or glob matching for category rules. Extensions only.
- Bulk re-categorization of a multi-row selection. The right-click menu acts on one row; a future
  round can widen it.
- Any change to how downloads are stored, queued or scheduled.

## Decisions

### `CategoryId` is nullable, and `null` means "detect"

`DownloadItem` gains a single nullable `CategoryId`. The category shown is
`CategoryId ?? Detect(fileName, contentType)`. The alternative — resolving once at add time and
storing the result — was rejected for three concrete failures:

1. A row added before its name resolves would freeze at Other forever, because the stored value
   would never be recomputed. With `null`, it corrects itself when the name arrives.
2. A user who later creates an "Ebooks" category claiming `epub` would see no existing `.epub`
   download move into it without a full re-scan of the list.
3. There would be no way to distinguish "the user chose Video" from "the app guessed Video", so
   "go back to automatic" could not be offered, and an import could not safely re-categorize
   anything.

The cost is that the effective category is computed rather than read. It is a dictionary lookup on a
string, recomputed only when the name, the content type or the category list changes, and the row
view model already caches and invalidates derived values this way.

### `CategoryService` owns resolution; `GetFileKind` goes away

A single registered service holds the category list and exposes resolution, so the sidebar, the Type
column, the filter, the Add dialog, the Details window and the context menu all agree by
construction. `DownloadItemViewModel.GetFileKind` is removed rather than kept as a wrapper — leaving
a second path to an answer is how the two drift apart.

The existing `LogicTests` call `GetFileKind` statically. They are rewritten against the service, and
must still assert the same extension-to-kind mapping, since the built-in categories are required to
reproduce it exactly.

### Category order is the only ordering concept

The position the user drags a category to serves three purposes: the order of the sidebar list, the
sort key for the Type column, and the precedence when two categories claim the same extension
(earliest wins). Alternatives considered and rejected:

- *Sorting the Type column alphabetically* — sorts by the internal key or by a localized name, both
  of which are unstable. The user's own order is stable and is what they already see.
- *A separate precedence rule for extension conflicts* (most-specific-wins, or newest-wins) — a
  second invisible mechanism to explain. "The one higher in the list wins" is visible and draggable.

### Detection: extension, then content type, then Other

`Content-Type` is not currently reachable. The engine's `RemoteFileInfo` carries only `Address`,
`FileName`, `FileSize` and `SupportsRange`. But `SocketClient.SendRequestAsync` already copies
**every** response header into a `ResponseHeaders` dictionary (and `HttpHeaderNames.ContentType` is
already declared), and `GetFileInfoAsync` simply never reads it. So the engine change is additive
and costs no extra network request:

- add `ContentType` to `RemoteFileInfo`;
- populate it in `SocketClient.GetFileInfoAsync` from the already-populated header dictionary;
- set it to `null` on the best-effort fallback path in `RemoteFileResolver.GetFileInfoAsync`.

The alternative of an app-side probe was rejected: it would issue a second network request per
extension-less URL, duplicating work the engine already does. Relying on the browser extension alone
was rejected because most users do not install it.

Separately, the extension already computes a MIME value and sends it on hand-off (`mime` in its
hand-off payload) and the app discards it. Reading it on the add path is free.

**Ordering constraint:** the engine change must be released to NuGet before the app task that
consumes it, or the app pins a package version that does not exist.

### Icons are a key plus a color, never a file path

A category stores an icon **key** naming one of the app's shipped icons, plus a hex color. Nothing
platform-specific, nothing on disk. This is what makes a Linux export import cleanly on Windows, and
it keeps every category icon theme-consistent — the existing icons are vector paths tinted at render
time, so a category icon still looks like it belongs in both light and dark themes.

An unrecognized key must render a default icon **and preserve the original key** in the
configuration. Dropping it would make an import from a newer version silently destructive.

`FileKindToIconConverter` currently maps a kind string to a `Geometry`. It generalizes to resolving
an icon key, keeping its geometry cache; the color comes from the category and binds separately.

### Export contains settings and categories, and nothing else

Per the author: no download data, no queues, no schedules. Additionally excluded are values that are
meaningless on another machine — the default save folder, the resolved local API port and the
remembered window sizes.

The export carries a format version so a future field addition is detectable, and reserves an
`iconData` field, always null today, so that adding custom uploaded icons later does not invalidate
files exported now.

An import validates before it applies anything, and refuses a file whose category list is empty —
otherwise a malformed file could leave the user with no categories, and every download resolving to
a category that does not exist.

### Select-all scopes to the visible rows

Today the header checkbox operates on the manager's full item list. With a category filter able to
hide rows, that becomes: the user sees 8 checked rows and Remove deletes 24 files. The checkbox is
rescoped to the filtered view, and the toolbar shows the selected count while any filter is active.

This is a behavior change to shipped functionality, taken deliberately: the old behavior was only
ever safe because nothing could hide a row.

### Two sidebar states, and the old rail's code is deleted

The deleted rail had three states (hidden / 56px icons / 208px expanded) and that was part of its
complexity. The category sidebar is open or closed. `SidebarWidth`'s two-value logic and the
`MainViewModel` members left over from the rail are removed rather than reused, and the test that
exercises the old width behavior is replaced with one for the new toggle. Leaving dead members
behind invites a future session to wire the wrong thing up.

## Risks / Trade-offs

- **The engine release blocks part of the app work.** → Sequence it first in `tasks.md`. The app
  tasks that do not need MIME (sidebar, Type column, overrides, filter, export/import) are
  independent and can proceed while the package is published; only the MIME-detection task waits.

- **A second repo enters the change.** The engine lives in `../Downloader` and releases on its own
  cadence, so "done" for this change now depends on a NuGet publish. → The engine change is a
  property and one assignment, additive and backward compatible. If the release slips, detection
  degrades to extension-then-Other, which is exactly today's behavior, so the change can still ship.

- **Removing the Name cell's icon reduces scannability.** → The Type column sits immediately before
  Name, so the icon moves one column, not out of sight. Verify on the regenerated screenshots rather
  than assuming.

- **The resolution is computed on every render of a row.** → It is a dictionary lookup keyed by
  extension, cached per row and invalidated on name, content-type or category-list change. Measure
  if the list is large; the existing 250 ms UI pump already bounds how often rows refresh.

- **Changing select-all is a behavior change users may notice.** → It only differs from the old
  behavior when a filter is hiding rows, which is new functionality. Cover both directions with
  tests.

- **A category the user deletes may be referenced by download overrides.** → Deleting a category
  clears the overrides pointing at it, so no download is left referencing something that is gone.
  Same on import, where a category in an override may not exist in the imported set.

- **Duplicate category names make the sidebar ambiguous.** → Refuse a duplicate name at save time
  rather than trying to disambiguate in the UI.

## Migration Plan

1. Release the engine change (`../Downloader`): `RemoteFileInfo.ContentType`, populated in
   `SocketClient.GetFileInfoAsync`. Additive; no consumer breaks.
2. Bump `Config.SchemaVersion`. On load, a configuration without a category list gets the eight
   built-in categories seeded with the extension sets from the current `GetFileKind` switch, named in
   the app's current language, with the sidebar hidden. Every existing download has a null
   `CategoryId`, so all of them resolve exactly as they did before — the upgrade is visually a no-op
   apart from the new column.
3. Ship the app changes. No rollback machinery is needed: an older app build reading the newer
   configuration ignores the category list and the `CategoryId` values (unknown JSON members are
   already tolerated), and behaves as it does today.

## Open Questions

- Should the category filter also apply to the Queues page, which renders the same type icon per
  item? The specs scope it to the downloads list. Applying it there too is a small addition if the
  author wants it.
- Should the sidebar's category rows accept a download dropped onto them as a way to re-categorize
  by drag? The grid already has row drag-and-drop for reordering and moving between queues, so the
  interaction exists; deferred until the basic flow is in use.

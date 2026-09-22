## Context

The downloads list is one flat `ObservableCollection<DownloadItemViewModel>` on `DownloadManager`,
presented through a `DataGridCollectionView` whose `Filter` is `DownloadsViewModel.Matches`. The
footer's six pills map onto a `StatusFilter` enum; the toolbar's action cluster is already swapped by
page (`IsVisible="{Binding IsDownloadsSelected}"`). Archiving reuses all three mechanisms rather
than introducing a second list or a second window.

Everything the user can do to a download funnels through `DownloadManager` — that is deliberate and
documented: per-row buttons gate themselves with `Can*`/`IsVisible`, but **bulk actions do not**, so
state rules that live only in the view are bypassed the moment a user ticks several rows.

## Goals / Non-Goals

**Goals:**
- Take finished or abandoned downloads out of the working list without losing their record.
- Make "archived" a property of the record, persisted, with one place that enforces its rules.
- Keep the existing filter and toolbar mechanics; add a state to them, do not rebuild them.

**Non-Goals:**
- Automatic archiving (by age, on completion, by rule). Manual only, as issue #17 asks.
- A separate archive store or a second config file — the record stays where it is.
- Any change to what Remove means today; the new delete-partial option is opt-in.

## Decisions

### Archived is a flag on the record, not a separate collection
`DownloadItem.IsArchived` (persisted, default false), surfaced through
`DownloadItemViewModel.IsArchived` with the usual write-through pattern. A second collection would
have to be kept in sync with the master list on every add, remove, reorder and queue move, and the
Queues page, the pump and the stats all iterate the master list — each of those would need to know
which list to read. One flag means they each need one predicate instead.

**Alternative considered:** moving the row out of `Items` into an `Archived` collection. Rejected:
it duplicates ordering/queue state and makes restore a re-insertion problem (where in the order?).

### Archived is a separate axis, not a seventh status bucket
`StatusFilter.Archived` is added to the enum, but `Matches` treats it as a different question:

```
if (_filter == Archived) return vm.IsArchived;   // whatever its download state
if (vm.IsArchived)       return false;           // invisible to every other filter
```

A download's *state* (Running/Failed/…) is orthogonal to whether the user has filed it away — an
archived download is still "Failed", and if it is restored it must read Failed again. Folding
archived into the status enum would have destroyed that state.

The same predicate must be applied to `AllCount` and the five `*FilterCount` properties, or the pill
numbers stop matching the rows the pills show.

### The rules live in `DownloadManager`, not in the buttons
Two new methods, `Archive(vm)` and `Unarchive(vm)`, plus two rules inside the existing guards:

- `Archive` calls the existing `Cancel` first when the row is Running/Paused/queued, then sets the
  flag. `Cancel` already no-ops on terminal states, so this is safe for every row.
- `Start`/`Resume`/`Retry` clear the flag before doing anything else.

This is the same reasoning as the state-transition guards already there: bulk actions reach these
methods directly, so a rule enforced in the view is not enforced at all. The invariant the pair
maintains is **an archived download is never running or queued** — which is what makes the
exclusion from the pump safe rather than a way to strand a download invisibly.

### Exclusion is a predicate at each iteration site, listed explicitly
`PumpQueue`, `StartAll`, `StopAll`, `TotalSpeed`, `TotalDownloaded`, `AllCount`, the
`*FilterCount`s, and `QueuesViewModel`'s per-queue rows and aggregates each skip archived items.
These are enumerated in tasks.md rather than hidden behind a filtered property on the manager,
because `Items` is also the master ordering used by drag-reorder and the pump's priority — filtering
it globally would change what "the list" means to code that legitimately needs all of it.

### The toolbar swaps on the filter, exactly as it swaps on the page
Two sibling `StackPanel`s bound to `IsDownloadsSelected && !IsArchivedSelected` and
`IsDownloadsSelected && IsArchivedSelected`. No new mechanism, and the existing page-level swap keeps
working unchanged: opening Settings hides both.

The Archive button is **always present and enabled by `HasSelection`**, like Start/Pause/Stop — a
button that appears and disappears with the selection count would shift the toolbar's layout while
the user is ticking rows, and would be the only button in the row behaving that way.

### Delete-on-remove is opt-in and covers only the partial file
`DownloadSettings.DeletePartialFileOnRemove` (default false). `DownloadManager.Remove` already
deletes the multi-part scratch folder; it gains the engine's `<final>.download` sidecar when the
option is on. The completed file is never touched — Remove is a one-click action on a grid row, and
an accidental click must not be able to destroy a finished download.

## Risks / Trade-offs

- **[A user archives an unfinished download and wonders why it never resumes]** → the invariant makes
  that explicit rather than accidental: archiving stops it, and the archived view shows its real
  state. Restoring or starting it brings it back.
- **[Eight iteration sites must each skip archived rows; one missed site is an invisible download]**
  → each site is its own task and its own test; the pump and bulk-start cases are the dangerous ones
  and are specified as scenarios.
- **[Select-all + Archive on a filtered view]** → already settled by the previous change: select-all
  covers only the rows the filters show, so "archive everything completed" does what it looks like.
- **[Deleting a partial file is irreversible]** → default off, opt-in in Settings, and scoped to the
  `.download` sidecar only.

## Open Questions

None — the four design choices (row icon, always-visible toolbar button, partial-file-only deletion,
un-archive on start) were settled with the author before this change was written.

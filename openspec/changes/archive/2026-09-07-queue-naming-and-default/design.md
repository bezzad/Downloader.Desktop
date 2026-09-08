## Context

All four problems live around one weak point: **queue identity and queue membership were read in more
places than they were announced from**. The Queues page wrote `Queue.Name` directly; the toolbar menus
snapshotted names into `QueueActionTarget`; the default queue was implicitly `Queues[0]`.

## Decisions

### Renames go through the manager, and do NOT trim or reject

`RenameQueue(queue, name)` is deliberately permissive: it runs on **every keystroke** of the name box, so
trimming the text or refusing a blank value would rewrite what the user is typing under their cursor and
snap the caret. It also skips `NotifyList()` — a per-keystroke refresh of the whole downloads grid is far
too costly, and re-raising `QueueName` on that queue's rows already updates the only column that shows it.

### Naming a queue before creating it, not after

The alternative (create it, then focus its name box) keeps the "something appeared" problem: the card is
below the fold. Asking first means the card that appears is already the user's, and the page scrolls to it.
`QueueAdded` is an event rather than a direct view call so the view model stays UI-free.

### The suggested name is tokens, not a character prefix

A longest-common-character prefix of `The.X.Movie.S01.E02.mkv` and `…E03.mkv` is `The.X.Movie.S01.E0` —
not a name anyone would choose. Splitting on `. - _ space +` and keeping the common leading TOKENS gives
`The.X.Movie.S01`, and dropping trailing numbering tokens (`S01`, `E02`, `part3`, a bare year) gives
`The.X.Movie`. Links with no file name (a page URL) return null rather than a guess, and the caller falls
back to the plain "New queue" — a wrong name is worse than no name.

### The default is an id, resolved late

`Config.DefaultQueueId` stores the choice and `Config.DefaultQueue` resolves it, falling back to the first
queue when it is empty **or names a queue that no longer exists**. That fallback is what makes deleting the
default safe; `RemoveQueue` also clears the dangling id so nothing stale is persisted.

`SetDefaultQueue` carries the Settings "max concurrent downloads" value with it. That number mirrors the
DEFAULT queue's cap (an older decision), so leaving it behind would make Settings display, and write to,
a queue that is no longer the default.

### Radio semantics live in the view model, not in a RadioButton

The author asked for a checkbox. Exclusivity is enforced in `QueueRowViewModel.IsDefault`: setting it true
calls `SetDefaultQueue`, and setting it false re-raises the property to restore the tick — the app must
always have a default, so "untick" has no meaning. Every card re-reads its tick on `QueuesChanged`.

## Risks / Trade-offs

- **The suggestion is offered on every paste of 2+ links.** Mitigated by pre-filling rather than acting:
  the user confirms or cancels, and cancelling stops it being offered again in that dialog.
- **A rename fires `QueuesChanged` per keystroke**, which rebuilds the menus and each card's item list.
  Measured cost is a list scan per queue — cheap next to the grid refresh that was deliberately dropped.

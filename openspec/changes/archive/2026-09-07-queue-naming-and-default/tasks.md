## 1. A rename that reaches everything

- [x] 1.1 Add `IDownloadManager.RenameQueue(queue, name)`: set the name, re-raise `QueueName` on that queue's download rows, raise `QueuesChanged`.
- [x] 1.2 Route `QueueRowViewModel.Name` through it instead of writing `Queue.Name` directly.
- [x] 1.3 Keep it permissive (no trim, no blank check) — it runs per keystroke — and skip `NotifyList()` so a rename never refreshes the whole grid.
- [x] 1.4 Test: renaming a queue updates both toolbar menus and the row's queue name, and the old name is gone.

## 2. Name the queue before creating it

- [x] 2.1 `QueuesViewModel`: `IsAddingQueue` / `NewQueueName` / `ConfirmNewQueue` / cancel, with "New queue" opening the box rather than creating anything.
- [x] 2.2 Inline name box in the Queues page header (Enter confirms, Esc cancels), mirroring the Add dialog's box.
- [x] 2.3 Raise `QueueAdded` after creating, and scroll the page to the new card from the view.
- [x] 2.4 Test: nothing is created until a non-blank name is given; cancelling creates nothing; creating raises the scroll event.

## 3. Suggest a queue name for a batch

- [x] 3.1 `Services/QueueNameSuggester.Suggest(urls)` — pure: file names → tokens → common leading tokens → drop trailing numbering.
- [x] 3.2 Links with no file name return null; the Add dialog falls back to the localized "New queue".
- [x] 3.3 Wire it into `AddDownloadItemViewModel`: 2+ links pre-fill and open the queue-name box; cancelling stops it being offered again.
- [x] 3.4 Test the suggester (series, other separators + query string, nothing shared, page URLs, a single link) and the dialog behaviour.

## 4. Choose the default queue

- [x] 4.1 `Config.DefaultQueueId` persists the choice; `DefaultQueue` resolves it and falls back to the first queue when empty or unknown.
- [x] 4.2 `IDownloadManager.SetDefaultQueue` records it and brings the Settings "max concurrent" value with it.
- [x] 4.3 `RemoveQueue` clears the id when the default is deleted.
- [x] 4.4 A "Default" tick on each queue card with radio semantics in `QueueRowViewModel.IsDefault`; every card re-reads it on `QueuesChanged`.
- [x] 4.5 `Queues_Default` / `Queues_DefaultTip` in all 16 language packs.
- [x] 4.6 Test: exactly one default, picking another moves it, the current one cannot be unticked, deleting the default leaves a valid one, a new download lands in the chosen queue.

## 5. Ship it

- [x] 5.1 CI green on ubuntu Debug+Release and Windows Debug for the final commit; the legs that did not finish were killed by the 30-minute timeout with no `[FAIL]` (the known hang, recorded in `SKILL.md`).
- [x] 5.2 Note the patterns in `.claude/skills/downloader-desktop/SKILL.md` (rename propagation, name-first creation, the suggester).
- [x] 5.3 Author verified the four behaviours by hand in the running app.
- [ ] 5.4 Regenerate `docs/screenshots/` for the changed Queues page — NOT done: the session's container has no .NET SDK (the SDK host is blocked by the proxy), so the capture test cannot run. Needs a Linux box with the SDK.

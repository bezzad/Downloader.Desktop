## Why

Four things about queues were wrong in the same place, all reported together by the author:

1. **Renaming a queue only renamed it on the Queues page.** The toolbar's "Start queue" / "Stop queue"
   dropdowns keep a copy of the name they were built with, and a download row's queue name is a computed
   property nothing re-raises — so both kept showing the OLD name until the app was restarted.
2. **"New queue" created an unnamed card and said nothing.** With several queues the new card is added
   below the fold, so the click looked like it had done nothing; the user then had to find the card and
   rename it.
3. **A batch of links silently went to the main queue.** Adding a season of episodes gave no way to keep
   them together, and the app knew enough to offer one: their file names share a prefix.
4. **There was no way to choose which queue is the default.** New downloads always landed in the first
   queue in the list.

## What Changes

- **Renames propagate.** A new `IDownloadManager.RenameQueue` is the single place a queue is renamed: it
  re-raises the queue name on that queue's download rows and raises `QueuesChanged`, which rebuilds the
  toolbar menus in place.
- **The Queues page asks for the name first.** "New queue" opens an inline name box (Enter confirms, Esc
  cancels); nothing is created until a name is given, and the page then scrolls the new card into view.
- **A batch add offers a queue name.** A pure `QueueNameSuggester` reads what the links' file names share
  — `The.X.Movie.S01.E02.mkv` + `…E03.mkv` → `The.X.Movie` — and the Add dialog opens its queue-name box
  pre-filled so the user only confirms. Links with no file name fall back to "New queue".
- **A default-queue tick per queue card**, with radio semantics: exactly one queue is the default, ticking
  another moves it, and unticking the current one does nothing. The choice persists.

## Impact

- Affected specs: `queues`, `add-download`.
- Affected code: `Services/DownloadManager`, `Services/IDownloadManager`, `Services/QueueNameSuggester`
  (new), `Models/Config`, `ViewModels/QueuesViewModel`, `ViewModels/AddDownloadItemViewModel`,
  `Views/QueuesView`, all 16 i18n packs.
- No migration: `Config.DefaultQueueId` is absent in existing configs and an absent/unknown id means
  "the first queue", which is exactly today's behaviour.

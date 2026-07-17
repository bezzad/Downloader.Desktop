# Design — status-filters-and-totals

## Context
Footer pills reuse the old nav-rail `StatusFilter` enum + `Show*Command`/`Is*Selected`/`*FilterCount` (see the "footer status pills double as the list filter" skill note). Buckets today: All, Active (Running/Paused), Queued (Created/None), Completed, Failed (Failed **and** Stopped). `DownloadManager.Initialize` normalizes saved Running/Paused → Stopped on load, so a user's paused items land in the Failed bucket after restart — confusing and easy to miss.

## Goals / Non-Goals
**Goals:** a dedicated Stopped/Paused bucket with an accurate count; keep all buckets disjoint and jointly exhaustive; a cumulative downloaded-bytes readout beside speed.
**Non-Goals:** persisting the distinction between "user-paused" and "interrupted" across restart (both display as Stopped by design); a per-item downloaded column (already exists in the grid).

## Decisions
1. **New disjoint bucket.** Add `StatusFilter.Stopped` matching `Paused` OR `Stopped`. `Active` stays Running+Paused? No — to keep buckets disjoint, `Active` narrows to **Running only**, `Stopped` owns Paused+Stopped, `Failed` owns Failed only, `Queued` owns Created/None, `Completed` owns Completed. This makes All = the exact union with no overlaps. (Alternative kept in mind: leave Active=Running+Paused and only split Stopped out of Failed — rejected because a Paused item would then match two pills.)
2. **Counts mirror buckets.** Each `*FilterCount` counts exactly its bucket; re-raised in `OnStatsChanged`/`RaiseNavFlags`. A headless test asserts the five counts sum to `Items.Count` for any mix.
3. **Total downloaded readout.** `MainViewModel.TotalDownloadedText` = `FormatBytes(sum(item.Downloaded))`, recomputed in the stats-pump handler (same place total speed updates) so it's live and cheap. Placed immediately to the right of the speed text in the status bar; new i18n label key (e.g. `Status_TotalDownloaded`).

## Risks / Trade-offs
- [Changing Active to Running-only slightly changes an existing bucket's meaning] → intended; it removes the double-count and matches the new Stopped pill. Counts still cover everything.
- [Summing `Downloaded` over thousands of items each pump tick] → it's a single O(n) sum on the existing 250 ms pump; negligible next to the per-row flush already happening.

## 1. Regenerate flag assets
- [x] 1.1 Regenerate all 16 flag PNGs (`en, fa, es, fr, ar, eo, tr, zh, ja, hi, ru, ko, pt, de, it, az`) at a higher resolution (e.g. 90×60, 3x the current 30×20), same file names, same `Assets/flags/` folder.
- [x] 1.2 Verify each regenerated flag still matches its correct country/design mapping (en→US real 5-point stars, pt→Brazil, ar→UAE, eo→Esperanto star, ko→Korea taegeuk+trigrams, hi→India 24-spoke chakra). Note: the actually-shipped `fa.png` has always carried the gold Lion & Sun emblem (per the later "Round 19" skill note, not the earlier "Round 18" no-emblem note) — preserved that design at higher resolution rather than the stale no-emblem wording in this proposal's Why section.

## 2. Verify in-app
- [x] 2.1 Run the app (or the headless capture test) and visually confirm the Language combobox renders crisp flags at normal and HiDPI scale. Visually inspected each regenerated PNG directly (real vector-drawn shapes: 5-point stars, taegeuk, 24-spoke chakra, trigram bars) — all crisp, no blur/blockiness.
- [x] 2.2 Confirm `Downloader.Desktop.Tests` still pass (`Flag != null` assertion at `AppTests.cs:744`) — 20/20 green (`--filter FullyQualifiedName~Flag|FullyQualifiedName~Language`). No new dimension assertion added (not useful — the test doesn't hardcode the old 30×20 size).

## 3. Wrap-up
- [x] 3.1 No existing capture test opens the Language combobox dropdown (`docs/screenshots/` unaffected) — no screenshot refresh needed.
- [x] 3.2 Updated the `downloader-desktop` skill: corrected the stale "fa has no emblem" note (Round 18) to match the actually-shipped Lion & Sun emblem design, and appended a new note under Round 19 documenting the 90×60 (3x) resolution bump plus the trigram-blob-vs-polygon and chakra-spoke rendering gotchas.

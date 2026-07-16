# Tasks — main-grid-sort-and-layout

Each task is TDD: failing test first, make it pass, keep build + full `dotnet test` green, commit to `develop`, push, confirm GitHub Actions green before the next task.

## 1. Tri-state sorting + drag interplay (task #12)

- [ ] 1.1 Write tests on `DownloadsViewModel`: a `CycleSort(column)` goes Asc→Desc→None; None restores master order; `ClearSort()` removes sort descriptions; the current-sort state is queryable.
- [ ] 1.2 Implement VM-owned sort (apply/clear `SortDescriptions`), disable the DataGrid built-in 2-state sort, render our glyph. Make 1.1 pass.
- [ ] 1.3 Wire header-click → `CycleSort` and drag `PointerPressed` → `ClearSort()` in `DownloadsView.axaml.cs`; test that starting a drag clears an active sort. Build + full tests green; commit/push; wait for green CI.

## 2. Header checkbox alignment (task #13)

- [ ] 2.1 Adjust the overlaid select-all checkbox `Margin`/alignment to sit above the row checkbox column.
- [ ] 2.2 Regenerate `home-*` screenshots and verify the alignment visually. Build + full tests green; commit/push; wait for green CI.

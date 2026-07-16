# Tasks — queues-page-ux

Each task is TDD: failing test first, make it pass, keep build + full `dotnet test` green, commit to `develop`, push, confirm GitHub Actions green before the next task.

## 1. Collapsible queues + collapse/expand-all (task #10)

- [ ] 1.1 Write tests: `QueueRowViewModel.IsExpanded` defaults true; `QueuesViewModel.CollapseAll()` sets every row false, `ExpandAll()` sets every row true; a single row toggles independently.
- [ ] 1.2 Add `IsExpanded` + `CollapseAll`/`ExpandAll`; wrap each queue card body in an `Expander` (stats in the header, item list in content); add the toolbar collapse/expand-all toggle. Make 1.1 pass.
- [ ] 1.3 i18n labels/tooltips in all 16 packs. Build + full tests green; regenerate Queues screenshot; commit/push; wait for green CI.

## 2. Toolbar affordance refactor (task #8)

- [ ] 2.1 Regroup the queue-header controls (spacing/dividers, button chrome for actions, labels attached to controls) in `QueuesView.axaml`.
- [ ] 2.2 Regenerate the Queues screenshot and verify the clearer grouping visually. Build + full tests green; commit/push; wait for green CI.

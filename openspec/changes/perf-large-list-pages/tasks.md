# Tasks — perf-large-list-pages
TDD; build + full tests green; commit to develop; push; CI green before next task.
- [ ] 1.1 Test: adding N items via AddRange fires ListChanged O(N/slice) times, not O(N); UI-thread slices yield (headless: dispatcher stays responsive).
- [ ] 1.2 Implement AddRange (sliced, batched) + wire the Add flow to close-modal-then-stream. Make 1.1 pass.
- [ ] 2.1 Test: collapsed queue cards build no item wrappers; expanding builds them; rebuild reuses wrappers when unchanged.
- [ ] 2.2 Virtualize the queue item list (ListBox) + lazy/expanded-only RebuildItems. Make 2.1 pass.
- [ ] 3.1 Test: navigating between pages returns the SAME view instance (reference equality) and preserves VM state.
- [ ] 3.2 Implement the page-view cache in MainViewModel/MainWindow. Make 3.1 pass. Screenshots if UI changed.

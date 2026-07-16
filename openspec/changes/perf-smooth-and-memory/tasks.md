# Tasks — perf-smooth-and-memory

Each task is TDD: write the failing test first, make it pass, keep build + full `dotnet test` green, commit to `develop`, push, and confirm the GitHub Actions run is green before starting the next task.

## 1. Responsive Add-modal on huge paste (task #1)

Root cause corrected during apply: the freeze is Avalonia's non-virtualized multi-line `TextBox` laying out thousands of lines, not parsing (see design.md). Fix = don't render a large list in an editable box.

- [x] 1.1 Write tests on `AddDownloadItemViewModel`: a large seeded list (~2000, sample-shaped, blanks) sets `IsBulk=true` + correct `LinkCount`; `BulkSummaryText` shows the count; the injected resolve seam is NOT invoked for the bulk list (no per-link probing); `BuildItems()` still yields one item per link; `ClearUrls` empties the input and `IsBulk` returns false. A small list keeps `IsBulk=false`.
- [x] 1.2 Implement the cached parse + `LinkCount`/`IsBulk`/`BulkSummaryText`/`ClearUrls`; in `AddDownloadItemView` bind the editable box to `!IsBulk` and add the summary chip + Clear; add i18n keys in all 16 packs. Make 1.1 pass.
- [x] 1.3 Intercept `Ctrl+V`/`Shift+Insert` on the modal's `UrlBox` (tunnel → set `Urls`, `Handled`), and route a large top-bar paste into the Add dialog via `MainViewModel.OpenAddWithText`; keep small pastes unchanged. Build + full tests green; regenerate the Add-dialog screenshot if applicable; commit/push; wait for green CI.

## 2. Release memory on completion (task #11)

- [x] 2.1 Write an integration test: download ~30 small files via the loopback `HttpListener`; after all Completed assert every row's `Download==null` (engine released) — deterministic proof (the leaked `DownloadService` holds the package+buffers) that fails today because engines are retained. (Heap thresholds are unreliable for tiny payloads in a shared parallel process; `Download==null` is the reliable proxy per design.) Display state (name, 100%) preserved.
- [x] 2.2 In `DownloadManager.FinishTerminal`, `ReleaseEngine(vm)` disposes `vm.Download` and nulls it on Completed/Failed/Stopped (Package is owned by the engine → released on Dispose); Paused keeps its engine. `DownloadItemViewModel` keeps its model-backed display fields. Make 2.1 pass.
- [x] 2.3 Test that a released Stopped row retries correctly (fresh engine rebuilt in `Start`, continues to completion). Build + full tests green; commit/push; wait for green CI.

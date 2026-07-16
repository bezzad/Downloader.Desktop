# Tasks — perf-smooth-and-memory

Each task is TDD: write the failing test first, make it pass, keep build + full `dotnet test` green, commit to `develop`, push, and confirm the GitHub Actions run is green before starting the next task.

## 1. Async Add-modal (task #1)

- [ ] 1.1 Write a test proving the bulk-paste parse/validate does not run on the UI thread (e.g. `AddDownloadItemViewModel` exposes an awaitable parse seam; assert it completes without dispatcher work / that a large list is parsed on a background thread). Use the sample file's shape (~2000 lines, dupes, blanks).
- [ ] 1.2 Move multi-URL parse/dedupe/validate into `Task.Run`; marshal only the final list + counts to the UI thread; keep single-URL debounced resolve off-thread. Make 1.1 pass.
- [ ] 1.3 Verify no per-URL network probe happens for a multi-URL paste (test asserts the resolve seam is not invoked per item). Build + full tests green; commit/push; wait for green CI.

## 2. Release memory on completion (task #11)

- [ ] 2.1 Write an integration test: download ~50 small files via the loopback `HttpListener`; after all Completed + `GC.Collect()/WaitForPendingFinalizers`, assert `GC.GetTotalMemory(true)` is bounded near the pre-batch baseline (fails today because engines are retained). Also assert a per-row "engine released" flag/`Download==null` after terminal state.
- [ ] 2.2 In `DownloadManager` terminal handling, dispose `vm.Download` and null the retained `Download`/`Package`; ensure `DownloadItemViewModel` keeps display fields. Make 2.1 pass.
- [ ] 2.3 Add/confirm tests that a released Stopped/Failed row resumes/retries correctly (fresh engine, continues from partial). Build + full tests green; commit/push; wait for green CI.

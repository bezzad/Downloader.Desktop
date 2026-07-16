# Design — perf-smooth-and-memory

## Context
`AddDownloadItemViewModel` resolves file name/size and plugin variants on the `Urls` setter; with a 2000-line paste this runs synchronously on the dispatcher (parse, dedupe, validate, then per-URL probes) and the app locks for ~10 s. Separately, `DownloadManager` keeps each row's `DownloadService` (with its `DownloadPackage`, chunk buffers, and up to `MaximumMemoryBufferBytes`) alive after completion — nothing disposes it, so 2000 finished rows accumulate GBs that only a restart clears.

## Goals / Non-Goals
**Goals:** (1) the Add modal opens and stays responsive while a huge list is pasted/parsed; (2) memory returns toward the idle baseline as downloads finish; (3) no regression to pause/resume/retry/resume-after-restart.
**Non-Goals:** capping how many links can be added; changing the engine's own buffering strategy; reducing memory of *active* downloads.

## Decisions
1. **Off-thread paste pipeline.** The multi-URL parse/dedupe/validate runs in `Task.Run`; only the final list assignment + count marshals back to the UI thread. The single-URL name/size and variant lookups already debounce — keep the debounce but ensure the awaited work never touches the dispatcher except for the final property write. `CanDownload` stays gated only by the single-URL variant lookup (existing behavior); a bulk paste skips per-URL probing entirely (already the case for multi-URL) so 2000 links do zero network work at add time.
2. **Dispose on terminal transition, keep display state.** A single choke point in `DownloadManager` (the terminal handler `FinishTerminal` / the state guards) disposes `vm.Download` (`DownloadService` implements the engine's dispose/`Clear()`), then nulls `vm.Download` and the retained `Package`. `DownloadItemViewModel` keeps `FileName/Size/Downloaded/Progress/Status/Folder/Urls` (all model-backed) for the grid and for resume. Pause is unaffected (a paused row is not terminal — its engine stays). **Resume/Retry of a disposed row rebuilds a fresh `DownloadService` in `Start`** exactly as a first start does (engine `EnableAutoResumeDownload` + the existing `.download` file continue the bytes), so releasing the engine on Stopped/Failed/Completed is safe.
3. **Measuring memory in a test.** An `[AvaloniaFact]` integration test downloads many (~50) small files from the existing loopback `HttpListener`, forces `GC.Collect()`+`WaitForPendingFinalizers` after all complete, and asserts the managed heap (`GC.GetTotalMemory(true)`) after completion is within a bound of the pre-batch baseline (not strictly increasing per download). This proves engines are released; it can't assert OS RSS headlessly, but heap + disposed-count is a reliable proxy.

## Risks / Trade-offs
- [Disposing the engine loses live per-connection detail for a finished row's Details dialog] → acceptable: a completed download's Details shows the final segmented bar snapped to 100% from persisted parts, which already works without a live engine.
- [A race where a late progress event arrives after dispose] → the pump already drops staged progress unless `Status==Running`; nulling `Download` after the terminal state is set (which happens before dispose) avoids touching a disposed instance.
- [Rebuild-on-resume must re-apply per-item speed limit / headers] → `Start` already builds config from settings + `HasCustomSpeedLimit`, so a rebuilt engine is configured identically.

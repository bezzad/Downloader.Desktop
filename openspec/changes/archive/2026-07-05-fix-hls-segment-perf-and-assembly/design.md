## Context

`DownloadManager.ExecutePlanAsync` (Phase 2 plan runner) builds `_config.Settings.ToConfiguration()`
fresh **per part** and hands the post-processor `finalPath + ".assembling"`. The author's live HLS run
proved the pipeline works (36/36 segments downloaded, assembly reached) but exposed segment-download
overkill and the ffmpeg output-naming failure.

## Goals / Non-Goals

**Goals:**
- A segment part downloads with exactly one connection/chunk (no multipart overhead per segment).
- ffmpeg-based post-processors always receive an output path with a standard media extension.
- The assembled file lands with a sensible container extension (`.mp4`), never `.m3u8`.
- (If the author opts in) M segments download concurrently for real HLS throughput.

**Non-Goals:**
- No SDK/plugin changes; no change to single-file (non-plan) downloads.
- No re-mux format detection beyond the extension normalization (ffmpeg/the plugin decides codecs).

## Decisions

### D1: Per-part engine config override (host-side)
In the part loop: `if (part.Kind == PartKind.Segment || (part.ExpectedSize is > 0 and < SmallPartBytes))`
then `cfg.ChunkCount = 1; cfg.ParallelDownload = false;`. `SmallPartBytes` ≈ 8 MB. Rationale: chunking
only pays off above the engine's own `MinimumSizeOfChunking` anyway, and segments are transient files.

### D2: Temp name keeps the extension last
`AssemblingPath(finalPath)` = `{dir}/{stem}.assembling{ext}` (e.g. `video.assembling.mp4`), replacing
today's `finalPath + ".assembling"`. Atomic move to `finalPath` unchanged. Concat path gets the same
scheme for consistency.

### D3: Final-name normalization for post-processed plans
`NormalizeAssembledName(name, plan)`: when `plan.PostProcessKind != None` and the extension is
`.m3u8`/`.m3u` (or empty), swap to `.mp4` — unless the plugin's `SuggestedFileName` provides a
different concrete media extension, which wins. Pure + unit-tested. Applied once when the final name
is chosen in `RunPlanAsync` (so the row, parts folder and output all agree).

### D4 (optional): Bounded parallel segments
`Parallel.ForEachAsync`-style loop with `MaxDegreeOfParallelism = min(4, settings.ParallelCount or 4)`
over the not-yet-complete parts, each single-chunk; progress = completed-bytes aggregate; cancel checks
per part; assembly still strictly ordered by part index (paths are index-prefixed already). Pause maps
to pausing all in-flight part services (the row handle becomes a small multiplexer) — this is the main
complexity and why D4 is separable.

## Risks / Trade-offs

- [D4 pause/cancel semantics across concurrent parts] → keep D4 optional; D1–D3 alone already fix the
  author's failing scenario.
- [A plugin that genuinely wants a `.m3u8` output] → normalization only triggers when a post-process
  step exists (the output is then real media, not a playlist).

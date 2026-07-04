## Context

`DownloadManager.Start` → `ResolveViaPluginsAsync(url, name, ct)` asks `PluginManager.ResolveAsync` for a
`DownloadPlan` and currently returns only `(plan.Parts[0].Url, suggestedName)` — multi-part plans collapse
to their first part. The engine side (`DownloadService`, pause/resume/cancel, queue pump, staged progress
via `EnsureUiPump`) is mature; what's missing is a **plan executor** between "resolver returned N parts"
and "one final file on disk". Plugins already installed-and-tested that need it: HLS v1.1.0
(N segment parts + Concat/Mux) and its x.com extraction (progressive single part, HLS parts, or
video+audio + Mux).

## Goals / Non-Goals

**Goals:**
- A resolver plan with N parts + post-process produces one final playable file in the user's save folder,
  through the existing engine (speed limits, retries, settings all apply per part).
- One aggregate row progress; pause/resume/cancel; app-restart resume from the first incomplete part.
- Single-part `PostProcess.None` plans behave exactly as today (zero regression risk path).

**Non-Goals:**
- No parallel part downloads in v1 (sequential parts; each part still uses engine multipart internally).
- No `ITransferProvider` wiring (no plugin uses it yet).
- No per-part rows/strips in the details dialog (existing per-connection strip stays as-is).
- No SDK changes.

## Decisions

### D1: Sequential part execution through the existing engine
Each part downloads with a `DownloadService` configured from the item's settings, into
`<folder>/<final-name>.parts/NNN_<safe-part-name>`. Sequential keeps pause/resume/cancel semantics trivial
(they apply to the current part) and avoids queue-slot double-counting: the whole plan run occupies one
queue slot, exactly like a single download today. *Alternative:* parallel parts — better for many tiny
HLS segments, but complicates control semantics and progress; deferred until proven needed.

### D2: Plan persisted on the item
`DownloadItem` gains a nullable `PlanJson` (parts: url/kind/headers/expectedSize; postProcess kind+recipe;
suggestedFileName) written when resolution returns a multi-part/post-process plan. Completed parts are
detected by **files on disk** in the parts folder (size == expected when known, else presence + a
`.done` marker), so restart-resume needs no extra bookkeeping and a crashed part restarts cleanly via the
engine's own resume into the same part path.

### D3: Progress model
Total = Σ `ExpectedSize` when all parts have sizes (byte-weighted, live bytes of the current part count);
otherwise parts-completed/parts-total. The last 10% of the bar is reserved for post-processing when the
plan has one (mirrors the HLS post-processor's own 0→1 progress). Status text shows
`"part 12/240 · 34%"` style during multi-part runs and `"Assembling…"` during post-process — via the
existing staged-progress pump (no new per-event UI posts).

### D4: Post-process execution & cleanup
After the last part: `FindPostProcessor(plan.PostProcess)`; missing processor → Failed with a clear
message (plugin disabled/removed mid-download is the realistic cause). Output goes to a temp name then
atomically moves to the final path (same pattern as `FileService`). On success the parts folder is
deleted; on Cancel it is deleted; on Pause/Fail it is kept for resume/retry. Retry re-resolves the
original link (plans can go stale — signed segment URLs expire) and reuses still-valid completed parts
only when the re-resolved plan matches (same part count + urls), else starts the plan fresh.

### D5: Where it lives
A private `PlanRunner` (file `Services/DownloadManager.Plans.cs`, partial class) so `DownloadManager`'s
public surface and existing 1000-line flow stay untouched; `Start` branches to the runner only when the
resolved plan is multi-part/post-process. The runner reports into the same row VM staging API
(`StageProgress`) the engine path uses.

## Risks / Trade-offs

- **Expiring segment URLs** (signed HLS) during long runs → parts download back-to-back; on 403/expiry the
  run fails with the friendly message and Retry re-resolves. Good enough for v1; refresh-in-place later.
- **Disk usage** (parts + assembled output coexist briefly) → parts are deleted right after assembly;
  documented; post-processor already streams.
- **Pause semantics inside a part** rely on engine pause (existing, works); pausing between parts is
  trivially safe.
- **Queue cap**: the plan run must go through `PumpQueue` like any download (one slot per item) — reuse
  the existing start paths so bulk actions/scheduler guards keep applying.

## Migration Plan

1. Persist-plan model + runner behind the `Start` branch; single-part plans keep the old path.
2. Wire progress/status; then restart-resume; then Retry-re-resolve.
   Rollback = remove the branch; behavior returns to first-part-only.

## Open Questions

- Should the parts folder live under a hidden app temp dir instead of next to the target file? (Next to
  the file survives reboots + same-volume atomic moves; hidden dir is tidier. v1: next to file,
  dot-prefixed.)

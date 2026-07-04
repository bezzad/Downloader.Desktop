## Why

The host already routes pasted links through enabled plugins' resolvers, but it only downloads
**the first part** of a resolved plan (`DownloadManager.ResolveViaPluginsAsync` logs *"multi-part assembly
is not wired yet"*). That makes every multi-part plugin useless in the app today: the HLS plugin (v1.1.0,
segments + concat/mux) and the site video extraction (x.com via yt-dlp) are fully built and tested but
can't actually produce a playable file in the app. This is the known "Phase-2" gap and it currently blocks
`add-video-site-extraction` task 7.3 (in-app e2e) and the HLS plugin end-to-end.

## What Changes

- **Multi-part plan execution** in `DownloadManager`: when a resolver returns a plan with multiple parts
  and/or a post-process step, download **all** parts (sequentially, each through the existing engine with
  the item's settings and the part's per-request `Headers`) into a temp parts folder next to the target
  file, then run the matching plugin `IPostProcessor` (found via `PluginManager.FindPostProcessor`) to
  assemble the final file, then clean up the parts.
- **Progress & controls**: the row shows one aggregate progress (byte-weighted when part sizes are known,
  else completed-parts/total, with a reserved tail for post-processing); pause/resume/cancel keep working
  (pause stops at the current part and resumes there; cancel cleans up).
- **Persistence/resume**: the resolved plan (part list + post-process recipe + counter of completed parts)
  is persisted on the `DownloadItem`, so restarting the app resumes from the first incomplete part instead
  of restarting or forgetting the plan.
- **Failures stay friendly**: a failing part or post-process marks the item Failed with the existing
  friendly error text; the original link is kept so Retry re-resolves.
- Single-part plans with `PostProcess.None` keep today's exact behavior (no temp folder, no change).

## Capabilities

### Modified Capabilities
- `plugins`: the "download flow resolves links through enabled plugins" behavior is extended from
  first-part-only to full plan execution (multi-part download + post-process assembly + resume).

## Impact

- **Code**: `Downloader.Desktop` only — `DownloadManager` (plan executor), `DownloadItem` (persisted plan),
  `DownloadItemViewModel` (aggregate progress/status text for parts + assembling phase). No SDK changes;
  no plugin changes (HLS/Ollama/GitHub plugins already produce the right plans).
- **Out of scope**: `ITransferProvider` wiring (torrent — no plugin uses it yet), parallel part downloads
  (v1 sequential; each part still gets engine multipart within itself), per-part UI in the details dialog.
- **Unblocks**: HLS plugin in-app, `add-video-site-extraction` 7.3, and the Ollama plugin's future
  multi-part v2.

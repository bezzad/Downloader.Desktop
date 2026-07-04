# Plugin architecture

Goal: keep the **core app dependency-free** (no ffmpeg/yt-dlp for normal users) and let optional
**plugins** add capabilities — first **HLS / video-site downloads**, later **torrents**. Designed from the
brainstorm: the app is a *transfer manager*, not a media tool, so plugins extend a **3-phase pipeline**
rather than a single "media extractor".

## The pipeline (where plugins hook)
```
user input ──▶ [1] RESOLVE ──▶ [2] TRANSFER ──▶ [3] POST-PROCESS ──▶ final file
              (input→plan)    (fetch bytes)    (combine/decode)
```
- **Resolve** (`ILinkResolver`) — turn a pasted input into a `DownloadPlan`: one or more real URLs
  (parts) + a post-process recipe. yt-dlp/HLS live here. A plain `http://….zip` skips resolve.
- **Transfer** (`ITransferProvider`/`ITransfer`) — how bytes are fetched. **Core ships the default HTTP
  multipart engine.** A plugin can register an *alternative* transfer (torrent owns the whole download).
- **Post-process** (`IPostProcessor`) — combine the downloaded part files into the output (ffmpeg
  mux/concat, decrypt, …). Optional.

A plugin implements only the phase(s) it needs:
| Plugin | Resolve | Transfer | Post-process |
|---|---|---|---|
| HLS / video sites | ✅ yt-dlp → URLs | core HTTP | ✅ ffmpeg mux/concat |
| Torrent | recognize magnet | ✅ owns transfer | — |

## Job vs Transfers (progress aggregation)
The user sees **one Job** (one row, one queue entry, one final file). A Job owns **N part-transfers**
(video+audio = 2; HLS = N segments; plain = 1; torrent = 1 self-aggregating). A core **JobCoordinator**
owns the parts, reduces their progress to the single item the UI binds to
(`item% = Σ partBytes / Σ partTotal`, `speed = Σ partSpeed`), then runs the post-processor. **The UI binds
to the Job, never to a transfer** — same as today's row binding. (Detailed in the brainstorm; Phase 2.)

## Loading plugins (.NET)
- Plugins are external DLLs that reference **`Downloader.Desktop.Plugins.Abstractions`** (the small,
  stable SDK assembly — *only* interfaces + POCO types; no app dependency).
- Loaded at runtime with a collectible **`AssemblyLoadContext`** + `AssemblyDependencyResolver` per plugin
  (Microsoft's "app with plugins" pattern). The load context resolves the **Abstractions** assembly (and
  other host assemblies) back to the host's already-loaded copy, so `IDownloaderPlugin` is the *same* type
  on both sides (shared type identity). Works with the single-file host.
- **No trimming** (`PublishTrimmed` is off) — required for reflection-based loading.

## How the user gets plugins (UI)
A **Plugins** page (nav → MANAGE) lists installed plugins (name / version / author / description) with an
**enable** toggle, plus:
- **Install plugin…** → file picker → copies the `.dll` (+ siblings) into the plugins folder → reloads.
- **Open plugins folder** → `%AppData%/Downloader/plugins` (Linux `~/.config/Downloader/plugins`).
- **Reload** → rescan the folder.
Disabled plugin ids persist in `Config.DisabledPlugins`. (Trust note: a plugin is arbitrary code with full
app privileges — the safe long-term UX is an in-app catalog of **vetted/official** plugins from our GitHub,
ideally signed; the open folder is for power users.)

## Phasing
- **Phase 1 (done):** the Abstractions SDK + `PluginManager` (registry + ALC loader + enable/disable
  + `ResolveAsync`/post-processor/transfer lookup) + the Plugins UI + a safe resolve hook (no plugin → core
  behavior unchanged). TDD: registry/pipeline/manager behavior.
- **Phase 2 (done — the Resolve → Transfer → Post-process pipeline now runs end to end):** the multi-part
  **plan runner** in `DownloadManager` (`Services/DownloadManager.Plans.cs`). When a resolver returns a
  `DownloadPlan` with more than one part or a post-process step, `Start` hands off to `RunPlanAsync` →
  `ExecutePlanAsync` (UI-free, unit-tested):
  1. **Download** each part sequentially through the engine (the item's settings + the part's per-request
     `Headers`), into a hidden `<folder>/.<final-name>.parts/NNNN_<name>` scratch folder. Completed parts
     are detected by files on disk (size match, else a `.done` marker), so an **app restart resumes from
     the first incomplete part**. One queue slot per plan (same as a normal download).
  2. **Assemble** via `PluginManager.FindPostProcessor(plan.PostProcess)` (ffmpeg concat/mux, …) → temp file
     → atomic move to the final path → delete the parts folder. A missing processor / failed part marks the
     row **Failed** with a friendly message; **Retry re-resolves** the link (segment URLs expire) and reuses
     still-valid completed parts.
  - **Progress/controls:** one aggregate row progress (byte-weighted when part sizes are known, else
     parts-completed/total, reserving the last 10% for assembly), status text `Part i/N` / `Assembling…`,
     and pause/resume/cancel via the existing per-row `vm.Download` handle (each part's engine is published
     to it). The plan is persisted on `DownloadItem.PlanJson`.
  - The resolved plan is stored as a `PersistedPlan` (`Models/PersistedPlan.cs`) — a JSON-friendly copy of
     the SDK's `DownloadPlan`.
  - Still deferred: the `ITransfer` path (torrent — no plugin uses it yet) and parallel part downloads
     (v1 is sequential; each part still uses engine multipart internally).

## Breaking changes
- New project `Downloader.Desktop.Plugins.Abstractions` added to the solution; app references it.
- New `Config.DisabledPlugins`. New nav section. These are additive; Phase 2's `ITransfer` refactor of the
  download pipeline is the larger breaking change and is intentionally deferred.

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

## Plugin tiers (built-in vs. optional/catalog)
All first-party plugins live in one repo (`src/Downloader.Desktop.Plugins/`) but ship in two ways:

- **Built-in** (`GitHub`, `Ollama`): staged into the app's own build/publish `plugins/` folder, bundled with
  every install, **disable-only** (never removable), and updated with the app. The app csproj's
  `StageBundledPlugins` target copies them — an **explicit per-plugin allow-list**, so an optional plugin
  sitting in the same parent folder is never accidentally bundled.
- **Optional / catalog** (`Hls`, future `Torrent`): compiled + tested in the solution but the app has **no
  reference to them and never copies them into its output** — they are absent on a fresh install and keep
  the core dependency-free. They ship only as downloadable release assets and are installed on demand.

## The optional-plugin catalog (discover / install / update)
At release time, `scripts/build-plugins.sh` (run by `.github/workflows/release.yml`) builds each optional
plugin, zips it (dll + `deps.json`), computes its SHA-256, and attaches the zip plus a generated
`plugins-catalog.json` to the **same** `vX.Y.Z` GitHub Release as the app archives. The static metadata
(id/name/description/minAppVersion) comes from `packaging/plugins/optional-plugins.json`; the version comes
from the plugin's csproj `<Version>` (also the single source for the plugin's runtime-reported version, so
the update check can't loop).

The app (`Services/PluginCatalogService`) fetches that catalog off the **latest** release — the same call
`UpdateService` uses for the app's own self-update. In Settings → Plugins:
- **Add** (on a "More plugins" catalog row): download the asset → **verify SHA-256 before extracting or
  loading anything** (`PluginManager.InstallFromZipAsync` — a mismatch aborts with a friendly error and
  never loads unverified code) → extract into `PluginsRoot` → load. It then behaves like any user-installed
  plugin (Disable/Remove).
- **Update**: each installed optional plugin's version is compared to the catalog's; when newer the user is
  offered an update (a startup toast and an in-page Update button) — **only** on explicit acceptance is the
  new asset downloaded, verified, and swapped (unload old ALC → replace → reload). Never silent.

## How the user gets plugins (UI)
A **Plugins** section (Settings) lists installed plugins (name / version / author / description) with an
**enable** toggle, the catalog "More plugins" list (above), plus:
- **Install plugin…** → file picker → copies the `.dll` (+ siblings) into the plugins folder → reloads
  (manual/power-user path for a plugin not in the catalog).
- **Open plugins folder** → `%AppData%/Downloader/plugins` (Linux `~/.config/Downloader/plugins`).
- **Reload** → rescan the folder.
Disabled plugin ids persist in `Config.DisabledPlugins`. (Trust note: a plugin is arbitrary code with full
app privileges — the catalog install path verifies a SHA-256 we publish before loading; code signing is a
possible future hardening step. The manual open-folder/file-picker path is for power users.)

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
  - **Segment efficiency:** `PartKind.Segment` parts (and known-≤8 MB parts) download single-chunk —
     never N engine chunks per tiny segment — and segment-only plans run up to **4 segments in
     parallel** (assembly stays index-ordered). Bigger video/audio parts keep full engine multipart
     and stay sequential.
  - **Naming rules for ffmpeg:** the post-process temp output keeps its media extension LAST
     (`video.assembling.mp4`, never `video.mp4.assembling`), and a playlist-derived final name
     (`.m3u8`/`.m3u`) is normalized to `.mp4` (or the plugin's suggested extension) when the plan has
     a post-process step — ffmpeg picks its muxer from the output extension.
  - **The `ITransfer` path is now LIVE** (website-offline-zip-plugin change): when an enabled plugin's
    `ITransferProvider` claims an item's URL (checked in `DownloadManager.Start` BEFORE link resolution,
    so a dedicated scheme like `websitezip:` never round-trips through resolvers), the plugin's
    `ITransfer` owns the whole download — progress flows through the normal staging pump,
    Pause/Resume/Cancel route to the transfer (`DownloadItemViewModel.ActiveTransfer` /
    `TransferCancellation`), the queue cap applies, and the returned path becomes the Completed file
    (`DownloadManager.Transfers.cs`). First consumer: the optional **Website offline copy** plugin
    (`com.bezzad.website-zip`, crawl → offline rewrite → zip). Torrent can reuse this as-is.
  - **Fallback resolvers**: `ILinkResolver.IsFallback` (default false) lets a generic resolver (e.g.
    "any web page") claim broadly without shadowing specific plugins — `PluginManager.FindResolver` is
    two-pass (regular resolvers first), and `GetVariantsAsync` shows only the DETECTED resolver's
    variants (first non-empty answer in fallback order — a fallback's generic variant never pollutes a
    specific plugin's quality list). A catalog entry's `minAppVersion` is now
    enforced (`PluginCatalogService.MeetsMinAppVersion`) so plugins needing newer host plumbing are
    hidden from older apps.

## Breaking changes
- New project `Downloader.Desktop.Plugins.Abstractions` added to the solution; app references it.
- New `Config.DisabledPlugins`. New nav section. These are additive; Phase 2's `ITransfer` refactor of the
  download pipeline is the larger breaking change and is intentionally deferred.

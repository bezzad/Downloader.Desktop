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
- **Phase 1 (this change):** the Abstractions SDK + `PluginManager` (registry + ALC loader + enable/disable
  + `ResolveAsync`/post-processor/transfer lookup) + the Plugins UI + a safe resolve hook (no plugin → core
  behavior unchanged). TDD: registry/pipeline/manager behavior.
- **Phase 2 (later):** the `JobCoordinator` + multi-part download + the `ITransfer` refactor that lets the
  queue/UI drive torrent/HLS uniformly; the official HLS (yt-dlp+ffmpeg) and torrent plugins.

## Breaking changes
- New project `Downloader.Desktop.Plugins.Abstractions` added to the solution; app references it.
- New `Config.DisabledPlugins`. New nav section. These are additive; Phase 2's `ITransfer` refactor of the
  download pipeline is the larger breaking change and is intentionally deferred.

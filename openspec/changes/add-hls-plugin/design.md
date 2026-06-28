## Context

The host app (Downloader.Desktop) shipped the plugin **SDK + loader + Plugins UI** in Phase 1, but does **not yet run the pipeline**: there is no `JobCoordinator`, no multi-part download driver, and the resolver/transfer hooks are not yet executed by the download flow. The `Downloader` engine itself only downloads a single direct file URL (with HTTP range/multipart). HLS (`.m3u8`) is a segmented playlist format, so it cannot be handed straight to the engine.

The SDK contracts (`Downloader.Desktop.Plugins.Abstractions`, net10.0, nullable on) are stable and the reference implementation of all interfaces is `samples/Downloader.Desktop.SamplePlugin`. This design covers an **HLS plugin built in a separate repo** against that SDK, fully unit-testable standalone, that will run end-to-end only once host Phase-2 lands.

Locked decisions (from the proposal Q&A): **separate repo** `bezzad/downloader-plugins`; **local ProjectReference** to the sibling SDK csproj; **HLS only** (no Torrent, no host Phase-2 wiring).

## Goals / Non-Goals

**Goals:**
- Resolve a direct `.m3u8` link into a `DownloadPlan` of ordered segment parts plus a `Concat` post-process recipe.
- Parse master + media playlists, resolve relative URIs, and handle `#EXT-X-KEY` (AES-128) and `#EXT-X-MAP`.
- AES-128-decrypt segments and assemble them into one playable MP4 via ffmpeg stream-copy.
- Provision ffmpeg on first use (download per-OS into the plugin data dir), behind a stubbable `IFfmpeg`.
- Be a loadable DLL recognized as `IDownloaderPlugin` by a host-mirroring `AssemblyLoadContext`.
- Comprehensive standalone tests (parser, loopback resolve, AES round-trip, post-process) + CI.

**Non-Goals:**
- Site extraction (YouTube/Instagram/TikTok). That is the deferred yt-dlp "video-sites" plugin (`docs/instagram-rnd.md`), a separate future deliverable. HLS here is **direct `.m3u8` only**.
- The Torrent plugin (separate plugin in the same future monorepo).
- Host Phase-2 integration (JobCoordinator, multi-part execution, `ITransfer` wiring). The plugin is designed to slot into it later, not to build it.
- Bundling ffmpeg in any installer.
- Live DRM / Widevine / FairPlay playlists (unsupported; surfaced as a clear error).

## Decisions

### Interfaces: resolver + post-processor (engine stays the downloader)
Implement `IDownloaderPlugin` + `ILinkResolver` + `IPostProcessor`. The resolver expands the playlist into `Segment` parts; the **host** downloads each segment (reusing the engine's speed/pause/resume once Phase-2 wires multi-part); the post-processor decrypts + concatenates + remuxes. **Alternative considered:** an `ITransfer` that downloads the segments itself. Rejected as the primary path because it bypasses the engine's multipart/pause/resume. **Fallback:** if Phase-2 multi-part is still unavailable when the host wires this up, an `ITransfer` shim can download segments internally — keep the segment-fetch logic factored so it can be reused there, but do not build the `ITransfer` now.

### Hand-written M3U8 parser behind an interface
Playlists are simple line-based text; write a small focused parser (`IM3u8Parser`) rather than pulling a heavy library. Separating it behind an interface keeps it the unit-test core. **Alternative:** a third-party HLS library — rejected as over-weight for the tag subset we need and an extra dependency in a loadable plugin.

### AES-128 via BCL `System.Security.Cryptography`
Decrypt with `Aes` (CBC, PKCS7 off / full-block) using the key fetched from `#EXT-X-KEY URI` and the IV (explicit, or derived from the media sequence number when absent). No third-party crypto. Decryption is a pure function over (cipherbytes, key, iv) → testable with a known vector.

### ffmpeg downloaded on first use, behind `IFfmpeg`
Concatenating raw `.ts` then remuxing to a clean MP4 container needs ffmpeg (`-i concat -c copy out.mp4`). Ship nothing; on first use download a static per-OS build into `ctx.DataDirectory` and cache it. `IFfmpeg` abstracts "run a remux/concat" so tests stub it; a gated test may exercise real ffmpeg if present on the box. **Alternative:** a managed muxer — rejected (no robust maintained .NET MP4 muxer for arbitrary `.ts`; ffmpeg is the industry standard and matches the broader video-sites plan).

### Concat recipe carried as JSON in `PostProcess.Recipe`
`ResolveAsync` encodes segment order + key/IV (+ init segment) as JSON in `PostProcess{ Kind=Concat, Recipe=... }`, so the post-processor is self-contained given the downloaded segment files and the recipe. Keeps resolver and post-processor decoupled across the SDK boundary (no shared in-memory state).

### SDK via local ProjectReference
Reference `../Downloader.Desktop/src/Downloader.Desktop.Plugins.Abstractions/...csproj` with `<Private>false</Private><ExcludeAssets>runtime</ExcludeAssets>` (host provides the SDK at load time; sharing type identity is what makes `is IDownloaderPlugin` succeed). The plugin csproj sets `<EnableDynamicLoading>true</EnableDynamicLoading>` so the loader gets the `deps.json` it needs. **Alternative:** NuGet package — cleaner/versioned but needs a publish step in the host repo first; deferred (can switch later without code changes).

### Repo + test layout
New `bezzad/downloader-plugins` monorepo: `src/Downloader.Plugins.Hls/`, `tests/Downloader.Plugins.Hls.Tests/`, `samples/` (small `.m3u8` + `.ts` fixtures), `.github/workflows/ci.yml` (dotnet build + test on ubuntu). Tests follow the host's loopback pattern: serve fixture playlists/segments from an `HttpListener` and assert the produced plan/output. TDD — parser and AES tests first.

## Risks / Trade-offs

- **Host Phase-2 not yet present** → The plugin can't run end-to-end in the app until JobCoordinator/multi-part wiring lands. Mitigation: design + test entirely standalone (loopback + stubs); factor segment-fetch so an `ITransfer` fallback is cheap if needed.
- **ffmpeg download size/availability** (per-OS static builds, network on first use) → Mitigation: cache in data dir, verify the binary, clear error + retry on failed download; gate real-ffmpeg tests so CI doesn't depend on it.
- **Playlist variety** (live playlists, byte-range segments, `#EXT-X-MAP`, multiple keys, DASH-like edge cases) → Mitigation: scope to VOD `.m3u8` with AES-128/none; raise clear errors for unsupported features (live/DRM); cover known tags with fixtures and grow coverage incrementally.
- **AES IV/sequence subtleties** (implicit IV from media sequence) → Mitigation: known-vector round-trip tests for both explicit and derived IV.
- **SDK drift** (local ProjectReference, separate repo) → Mitigation: pin to the sibling checkout; if the SDK surface proves insufficient, the SDK change is an explicit separate host-repo task (called out in Impact). Switching to a versioned NuGet later removes drift.
- **Convention friction** — the host repo mandates work on `develop`; this plugin work lives in a *different* repo, so its commits/CI are governed by the new repo, not this one. This OpenSpec change only tracks the proposal/design/tasks here.

## Migration Plan

1. Create `bezzad/downloader-plugins` with scaffolding (sln, Directory.Build.props, .gitignore, CI).
2. TDD the parser, then AES decrypt, then resolver (loopback), then post-processor (stub ffmpeg), then the loadable-DLL test.
3. Wire `IFfmpeg` real implementation (first-use download) last; keep it behind the stub for tests.
4. Ship a loadable DLL + README install instructions (drop the DLL in the app's plugins folder). End-to-end host execution awaits Phase-2; no rollback needed in the host (the host is unchanged).

## Open Questions

- ffmpeg static-build source/URLs per OS (which distribution to pull, checksum/verify strategy).
- Default container/extension for output (`.mp4` via remux vs. raw `.ts` concat) when the source is already fMP4.
- Quality-hint surface: does the host pass a preferred variant/quality, or does the resolver always pick highest BANDWIDTH for v1? (Assume highest-BANDWIDTH default until the host exposes a hint.)
- Whether to keep the `ITransfer` fallback in the first release or add it only if Phase-2 slips.

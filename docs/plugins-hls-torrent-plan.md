# Plan: HLS and Torrent plugins (new repo)

Build two official plugins for Downloader against the plugin SDK
(`Downloader.Desktop.Plugins.Abstractions`), in a **separate repo**, each with tests. This document is the
spec for a fresh Claude session.

## ⚠️ Read first: host-integration dependency (Phase 2)
The host app (Downloader.Desktop) ships the plugin **SDK + loader + Plugins UI** (Phase 1, done), but does
**not yet run the pipeline** — there's no `JobCoordinator`, no multi-part download, and the resolver/transfer
hooks aren't wired into the download flow. So:
- The plugins can be **fully built and unit-tested standalone** now (against the SDK + mocks/loopback).
- They will only **run end-to-end inside the app after Phase 2** (host integration) lands.
Design + test the plugins so their core logic (m3u8 parsing, AES decrypt, ffmpeg orchestration, the
MonoTorrent client) is verified **without a running host**. Treat Phase-2 wiring as a separate host task.

## The SDK contracts (what plugins implement)
From `Downloader.Desktop.Plugins.Abstractions` (net10.0, nullable on):
- `IDownloaderPlugin { Id; Name; Version; Author; Description; Initialize(IPluginContext ctx) }`
- `IPluginContext { RegisterResolver/RegisterTransferProvider/RegisterPostProcessor; string DataDirectory; void Log(string) }`
- `IMediaResolver { bool CanResolve(url); Task<DownloadPlan> ResolveAsync(url, ct) }`
- `ITransferProvider { bool CanHandle(url); ITransfer Create(url, targetFolder) }`
- `ITransfer { event ProgressChanged(TransferProgress); Task<string> StartAsync(ct); void Pause(); void Resume() }`
- `IPostProcessor { bool CanProcess(PostProcess); Task<string> ProcessAsync(inputFiles, plan, outputPath, IProgress<double>, ct) }`
- POCOs: `DownloadPlan { SuggestedFileName; IReadOnlyList<MediaPart> Parts; PostProcess PostProcess }`,
  `MediaPart { string Url; PartKind Kind; IReadOnlyDictionary<string,string>? Headers; long? ExpectedSize }`,
  `PartKind { Combined, Video, Audio, Segment, Subtitle }`,
  `PostProcess { PostProcessKind Kind; string? Recipe }`, `PostProcessKind { None, Mux, Concat, Decrypt }`,
  `TransferProgress { Percentage; BytesReceived; TotalBytes; BytesPerSecond }`.
The reference implementation of *all* interfaces is `samples/Downloader.Desktop.SamplePlugin` (GitHub
Releases). Guides: `docs/writing-plugins.md`, `docs/plugins-architecture.md`.

## New repo
- **Suggested:** `github.com/bezzad/downloader-plugins` — a monorepo for official plugins. Create with
  `gh repo create bezzad/downloader-plugins --public` (or private).
- **Structure:**
  ```
  downloader-plugins/
  ├─ README.md  ·  Downloader.Plugins.sln  ·  Directory.Build.props  ·  .gitignore
  ├─ src/
  │   ├─ Downloader.Plugins.Hls/            (HLS / .m3u8)
  │   └─ Downloader.Plugins.Torrent/        (magnet / .torrent)
  ├─ tests/
  │   ├─ Downloader.Plugins.Hls.Tests/
  │   └─ Downloader.Plugins.Torrent.Tests/
  ├─ samples/  (a couple of small .m3u8 + .ts fixtures, a tiny .torrent fixture)
  └─ .github/workflows/ci.yml  (dotnet build + test on push/PR)
  ```
- Each plugin csproj: `net10.0`, nullable on, **`<EnableDynamicLoading>true</EnableDynamicLoading>`**, and
  reference the SDK with `<Private>false</Private><ExcludeAssets>runtime</ExcludeAssets>` (host provides it).

### SDK distribution (pick one — recommend A)
- **A. NuGet (recommended):** publish `Downloader.Desktop.Plugins.Abstractions` from the main repo
  (`dotnet pack` → push to nuget.org), version it, and `PackageReference` it. Clean + versioned.
  Prerequisite task in the **main** repo: add `<IsPackable>true</IsPackable>` + package metadata to the
  Abstractions csproj and publish.
- **B. Local ProjectReference:** both repos are siblings on disk → reference
  `../Downloader.Desktop/src/Downloader.Desktop.Plugins.Abstractions/Downloader.Desktop.Plugins.Abstractions.csproj`.
  Fastest for local dev; not portable for external contributors.
- **C. Vendor:** copy the 2 tiny SDK source files into a local `Abstractions/` project. Avoid drift.

## Plugin 1 — HLS (`Downloader.Plugins.Hls`)
**Goal:** download an HLS stream (`.m3u8`) to a single playable file. Direct `.m3u8` only here (site
extraction like YouTube/Instagram = a separate yt-dlp "video-sites" plugin, future).

**Interfaces:** `IDownloaderPlugin` + `IMediaResolver` + `IPostProcessor`. (Keep the engine as the
downloader: the resolver expands the playlist into segment parts; the host downloads them; the
post-processor concats+decrypts. If Phase-2 multi-part isn't available when wiring, fall back to an
`ITransfer` that downloads the segments itself.)

**Design:**
1. `CanResolve`: URL path ends with `.m3u8` (case-insensitive), or a cheap HEAD shows
   `application/vnd.apple.mpegurl` / `application/x-mpegURL`.
2. `ResolveAsync`:
   - GET the playlist. If it's a **master** playlist, pick the best variant (`#EXT-X-STREAM-INF`,
     highest BANDWIDTH, or honor a quality hint), GET its **media** playlist.
   - Parse the media playlist → ordered list of segment URIs (resolve relative → absolute), plus
     `#EXT-X-KEY` (AES-128 `METHOD`, `URI`, `IV`) if encrypted, and `#EXT-X-MAP` init segment if present.
   - Return a `DownloadPlan` with one `MediaPart{ Kind=Segment }` per segment (carry per-segment
     `Headers` incl. the key URL/IV in the recipe), `PostProcess{ Kind=Concat, Recipe=<json: keys/iv/order> }`,
     `SuggestedFileName` from the URL (default `.ts`/`.mp4`).
3. `IPostProcessor.ProcessAsync` (Concat):
   - Decrypt each AES-128 segment (System.Security.Cryptography) if needed, concatenate in order, then
     **remux to MP4 with ffmpeg** (`-i concat -c copy out.mp4`) for a clean container.
   - **ffmpeg on first use:** download a static ffmpeg build into `ctx.DataDirectory` per OS (do NOT bundle).
     Provide an `IFfmpeg` abstraction so tests can stub it.
4. **Parser:** write a small, focused M3U8 parser (playlists are simple line-based text) — don't pull a
   heavy lib. Put it behind an interface for testing.

**Tests (standalone, no host):**
- M3U8 parser: master→variant selection; media→segment list; relative URI resolution; `#EXT-X-KEY` parse;
  `#EXT-X-MAP`. Use committed `.m3u8` fixtures.
- Resolve: given a fixture playlist served by a loopback `HttpListener`, assert the produced `DownloadPlan`
  (segment count, order, Concat recipe).
- AES-128 decrypt: round-trip a known key/IV vector.
- Post-process: feed a few local `.ts` fixtures → assert a single output file (ffmpeg stubbed, or a gated
  test that runs real ffmpeg if present).
- Edge: non-HLS URL → `CanResolve` false; empty/garbled playlist → clear error.

## Plugin 2 — Torrent (`Downloader.Plugins.Torrent`)
**Goal:** download from a magnet link or `.torrent` file. A torrent owns its transfer (no HTTP URL), so this
is a **transfer** plugin.

**Interfaces:** `IDownloaderPlugin` + `ITransferProvider` + `ITransfer`.

**Library:** **MonoTorrent** (`PackageReference Include="MonoTorrent"`) — the mature, maintained .NET
BitTorrent library. Don't roll your own BitTorrent.

**Design:**
1. `CanHandle`: `magnet:?xt=urn:btih:` or a `.torrent` path/URL.
2. `Create` → a `TorrentTransfer : ITransfer` wrapping a MonoTorrent `ClientEngine` + `TorrentManager`:
   - `StartAsync(ct)`: add the magnet/torrent to the engine, start, await completion; map MonoTorrent's
     progress (`Progress`, `DownloadSpeed`, `Monitor`) → raise `ProgressChanged(TransferProgress)` (throttle
     to ~whole-percent). Return the downloaded file/folder path.
   - `Pause()`/`Resume()` → `TorrentManager.PauseAsync()`/`StartAsync()`.
   - Honor `targetFolder`; pick the largest file for a single-file result (or return the folder).
   - Settings later: port, max connections, seeding ratio — keep minimal first.
3. Lifecycle: dispose the engine on completion/cancel; respect the `CancellationToken`.

**Tests (self-contained loopback, like the engine's IntegrationTests):**
- Use MonoTorrent to **create a torrent from a temp file**, **seed it** from one `ClientEngine`, and
  **download it** through the plugin's `ITransfer` against `127.0.0.1` — assert the bytes match. No external
  trackers/peers (use a local/private tracker or DHT-off + direct peer).
- `CanHandle`: magnet/.torrent true; http false.
- Progress events fire and reach 100%; `Pause`/`Resume` don't corrupt.
- Cancellation stops cleanly.

## CI & acceptance
- `.github/workflows/ci.yml`: `dotnet build` + `dotnet test` on push/PR (ubuntu; add windows/mac if cheap).
- **Acceptance:** both plugins build to loadable DLLs; all tests green; each plugin loads in a throwaway
  `AssemblyLoadContext` test that mirrors the host loader (resolve the Abstractions assembly from Default →
  assert `is IDownloaderPlugin`); README documents install (drop DLL in the app's plugins folder).
- Keep commits small; TDD (write the parser/transfer tests first).

## Out of scope (note in README)
- Site extraction (yt-dlp) for YouTube/Instagram → a separate future "video-sites" plugin.
- The host **Phase-2** integration (JobCoordinator, multi-part download, ITransfer wiring) lives in the
  Downloader.Desktop repo, not here.

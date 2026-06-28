## Why

Users want to download HLS streams (`.m3u8`) — a common delivery format for video/audio — but the `Downloader` engine only handles direct file URLs with HTTP range support, not segmented playlists. The plugin SDK (`Downloader.Desktop.Plugins.Abstractions`) and host loader already exist (Phase 1), giving us a clean extension point. An official HLS plugin lets the app expand an `.m3u8` playlist into its segments, fetch them, and assemble a single playable file — without changing the host engine.

## What Changes

- Create a **new, separate repository** `bezzad/downloader-plugins` (monorepo for official plugins), seeded with solution/build/CI scaffolding. The HLS plugin is built and tested there, not inside this host repo.
- Add **`Downloader.Plugins.Hls`** implementing the SDK contracts `IDownloaderPlugin` + `ILinkResolver` + `IPostProcessor`:
  - A focused, line-based **M3U8 parser** (master variant selection, media segment list, relative→absolute URI resolution, `#EXT-X-KEY` AES-128, `#EXT-X-MAP` init segment).
  - **`ILinkResolver`** that recognizes `.m3u8` links and expands the playlist into a `DownloadPlan` with one `Segment` part per segment plus a `Concat` post-process recipe (segment order, key/IV).
  - **`IPostProcessor` (Concat)** that AES-128-decrypts segments when needed, concatenates in order, and remuxes to MP4 via **ffmpeg** (`-c copy`).
  - An **`IFfmpeg` abstraction** with a real implementation that **downloads a static ffmpeg build on first use** into `ctx.DataDirectory` (not bundled), and a stub for tests.
- Reference the host SDK via **local ProjectReference** to the sibling `../Downloader.Desktop/.../Downloader.Desktop.Plugins.Abstractions.csproj`.
- Add **standalone tests** (parser, resolve via loopback `HttpListener`, AES-128 round-trip, post-process with stubbed/gated-real ffmpeg, edge cases) and a **CI workflow** (build + test).
- This change is **HLS only**. The Torrent plugin and the host **Phase-2** pipeline wiring (JobCoordinator / multi-part download / `ITransfer` execution) are out of scope and tracked separately.

## Capabilities

### New Capabilities
- `hls-plugin`: An official Downloader plugin that resolves an HLS `.m3u8` link into downloadable segments and post-processes them (AES-128 decrypt + concat + ffmpeg remux) into a single playable file, implemented standalone against the plugin SDK and verifiable without a running host.

### Modified Capabilities
<!-- None. The host `plugins` capability (SDK + loader + Plugins UI) is unchanged; the HLS plugin only consumes its existing contracts. End-to-end host execution depends on the separate Phase-2 work. -->

## Impact

- **New repo**: `bezzad/downloader-plugins` — `Downloader.Plugins.sln`, `Directory.Build.props`, `.gitignore`, `src/Downloader.Plugins.Hls/`, `tests/Downloader.Plugins.Hls.Tests/`, `samples/` fixtures, `.github/workflows/ci.yml`, `README.md`.
- **This repo**: no code changes. The SDK (`Downloader.Desktop.Plugins.Abstractions`) is consumed as-is via local ProjectReference; if its surface is found lacking during implementation, any required SDK change is a separate host-repo task.
- **Dependencies**: a static **ffmpeg** binary downloaded at runtime per-OS (not bundled); `System.Security.Cryptography` for AES-128 (BCL, no package).
- **Runtime**: the plugin is loadable now and unit-tested standalone, but only runs **end-to-end inside the app after host Phase-2** lands.

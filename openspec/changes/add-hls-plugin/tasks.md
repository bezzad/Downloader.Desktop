## 1. New repo scaffolding

- [ ] 1.1 Create `bezzad/downloader-plugins` repo (gh repo create, public) with `README.md`, `.gitignore` (dotnet), `Directory.Build.props` (net10.0, nullable on, LangVersion latest)
- [ ] 1.2 Add `Downloader.Plugins.sln` and the repo structure (`src/`, `tests/`, `samples/`, `.github/workflows/`)
- [ ] 1.3 Add `src/Downloader.Plugins.Hls/Downloader.Plugins.Hls.csproj`: net10.0, nullable on, `<EnableDynamicLoading>true</EnableDynamicLoading>`, ProjectReference to the sibling SDK csproj with `<Private>false</Private><ExcludeAssets>runtime</ExcludeAssets>`
- [ ] 1.4 Add `tests/Downloader.Plugins.Hls.Tests/` test csproj (xUnit) referencing the plugin and the SDK
- [ ] 1.5 Add `.github/workflows/ci.yml` (dotnet build + test on ubuntu push/PR) and confirm an empty solution builds green

## 2. M3U8 parser (TDD)

- [ ] 2.1 Add `.m3u8` + `.ts` fixtures under `samples/` (a master playlist, a media playlist, an AES-128 media playlist, an `#EXT-X-MAP` playlist, and a garbled file)
- [ ] 2.2 Write parser tests first: master→best-variant selection, media→ordered segment list, relative→absolute URI resolution, `#EXT-X-KEY` (METHOD/URI/IV) parse, `#EXT-X-MAP`, and garbled/empty → clear error
- [ ] 2.3 Implement `IM3u8Parser` + parser to make the tests green (line-based; model master variants, media segments, key info, init segment)

## 3. AES-128 decryption (TDD)

- [ ] 3.1 Write a known-vector round-trip test (encrypt a buffer with a known key/IV, assert decrypt returns the plaintext) and an implicit-IV-from-sequence test
- [ ] 3.2 Implement AES-128-CBC decrypt with `System.Security.Cryptography` (explicit IV and IV-derived-from-media-sequence); unencrypted segments pass through

## 4. Resolver (`ILinkResolver`)

- [ ] 4.1 Implement `CanResolve`: `.m3u8` path (case-insensitive, ignoring query) → true; cheap HEAD `application/vnd.apple.mpegurl` / `application/x-mpegURL` → true; otherwise false. Add unit tests for each branch
- [ ] 4.2 Implement `ResolveAsync`: GET playlist; if master, pick best-BANDWIDTH variant and GET its media playlist; build a `DownloadPlan` with one `Segment` part per segment (in order, absolute URLs, per-segment headers), `PostProcess{ Kind=Concat, Recipe=<json: order+key/iv+init> }`, and a `SuggestedFileName`
- [ ] 4.3 Resolve tests via loopback `HttpListener` serving fixtures: assert segment count/order, the Concat recipe JSON, master→media following, and suggested file name

## 5. Post-processor (`IPostProcessor` + ffmpeg)

- [ ] 5.1 Define `IFfmpeg` abstraction (concat/remux operation) and a stub implementation for tests
- [ ] 5.2 Implement `ProcessAsync` (Concat): decrypt AES-128 segments per recipe, concatenate in order, remux to MP4 via `IFfmpeg` (`-c copy`), report progress via `IProgress<double>`, return the output path
- [ ] 5.3 Post-process tests with stubbed ffmpeg (assert ordered decrypt+concat call + single output) and a gated test that runs real ffmpeg if present
- [ ] 5.4 Implement the real `IFfmpeg`: locate ffmpeg in `ctx.DataDirectory`, else download a static per-OS build on first use, verify, cache, and reuse

## 6. Plugin wiring + loadability

- [ ] 6.1 Implement `IDownloaderPlugin` (`Id`/`Name`/`Version`/`Author`/`Description`/`Initialize`): register the resolver and post-processor on `IPluginContext`; log via `ctx.Logger` (ILogger)
- [ ] 6.2 Add a host-mirroring loadable-DLL test: load the built plugin in a collectible `AssemblyLoadContext` that resolves the SDK from Default, assert `is IDownloaderPlugin` and metadata is exposed

## 7. Docs + acceptance

- [ ] 7.1 README: what the HLS plugin does, install (drop DLL in the app's plugins folder), supported scope (direct `.m3u8`, AES-128/none, VOD), out-of-scope (site extraction, DRM, live, Torrent, host Phase-2)
- [ ] 7.2 Verify acceptance: plugin builds to a loadable DLL, all tests green in CI, loadable-DLL test passes; note in the host repo's skill that the HLS plugin repo exists and how it relates to Phase-2

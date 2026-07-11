# Website offline-copy (.zip) plugin

## Why

Users often want to keep a web page — or a whole small site — readable offline: documentation, articles, references. Today the app downloads the raw HTML file only, which is broken offline (no CSS/images/scripts, links point back to the network). A dedicated plugin can capture a page or site with all its requisites, rewrite links for local viewing, and deliver one portable `.zip` (the native-.NET equivalent of `wget --mirror --page-requisites --convert-links` + zip).

## What Changes

- New **optional/catalog-tier plugin** `Downloader.Desktop.Plugins.Website` (id `com.bezzad.website-zip`): recursive same-host crawl (depth/page caps) + all page requisites (CSS/JS/images/fonts from any host, incl. assets referenced from CSS), offline link rewriting, zipped output.
- **Trigger UX**: the Add dialog's existing variant picker offers "Offline copy (.zip)" for URLs that serve `text/html`, via a `SubstituteUrl` variant that rewrites the item URL to a `websitezip:` scheme — normal downloads stay the default.
- **SDK (additive, non-breaking)**: `ILinkResolver.IsFallback` (default-implemented, `false`) so a generic resolver can claim page-like URLs without shadowing specific plugins (GitHub/HLS/Ollama).
- **PluginManager**: two-pass `FindResolver`/`FindResolverPluginId` (non-fallback resolvers win); `GetVariantsAsync` merges variants from all claiming resolvers.
- **DownloadManager**: consumes `ITransferProvider`/`ITransfer` for the first time — a claimed URL runs as a self-managed transfer with progress staging, pause/resume/cancel, queue-cap compliance, and terminal handling. This also unblocks future transfer plugins (e.g. torrent).
- **Packaging**: ships like the HLS plugin — in the solution for build/test only, never bundled; added to `scripts/build-plugins.sh` + `packaging/plugins/optional-plugins.json`; isolation guarded by `PluginIsolationTests`.

## Capabilities

### New Capabilities
- `website-offline-copy`: crawling a page/site into an offline-viewable zip — scope rules (same-host recursion, cross-host requisites, caps), link rewriting, output naming, variant trigger, transfer lifecycle (progress/pause/resume/cancel).

### Modified Capabilities
- `plugins`: resolver selection gains fallback semantics (`IsFallback`, two-pass `FindResolver`); variant listing merges all claiming resolvers; the host must run a plugin-provided `ITransfer` end-to-end (previously unconsumed SDK surface).
- `link-variants`: variant offers may come from a fallback resolver on generic `text/html` URLs (zip variant appears alongside the default download).

## Impact

- **New project**: `src/Downloader.Desktop.Plugins/Downloader.Desktop.Plugins.Website` (+ solution entry; test folder `Downloader.Desktop.Tests/Plugins/Website`).
- **SDK**: `Downloader.Desktop.Plugins.Abstractions/Pipeline.cs` (additive DIM property).
- **App**: `Services/PluginManager.cs` (resolver ordering, variant merge), `Services/DownloadManager.cs` (+ transfer path), `ViewModels/DownloadItemViewModel.cs` (transfer handle for pause/resume).
- **Release**: `scripts/build-plugins.sh`, `packaging/plugins/optional-plugins.json`, `PluginIsolationTests`.
- No external binaries, no new NuGet dependencies (HttpClient + System.IO.Compression).

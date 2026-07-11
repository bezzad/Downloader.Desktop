# Tasks — website-offline-zip-plugin

## 1. SDK + host plumbing (fallback resolvers, variant merge, transfer path)

- [x] 1.1 Add `ILinkResolver.IsFallback` (DIM, `false`) to `Pipeline.cs`; make `PluginManager.FindResolver`/`FindResolverPluginId` two-pass (non-fallback first) with unit tests (fake fallback + specific resolver both claiming a URL)
- [x] 1.2 Make `PluginManager.GetVariantsAsync` merge variants from ALL claiming resolvers (non-fallback first, first default wins, per-resolver failures isolated) with unit tests
- [x] 1.3 Wire the transfer path into `DownloadManager.Start`: `FindTransferProvider(url)` checked before plugin resolve; run `ITransfer.StartAsync` with per-item CTS; `ProgressChanged` → `StageProgress`; completed path → Completed row (name/size backfilled); exception → Failed
- [x] 1.4 Route Pause/Resume/Cancel to an active transfer (transient `DownloadItemViewModel.ActiveTransfer`), keep queue-cap/pump semantics (Status=Running set synchronously), and add manager tests with an in-process fake `ITransferProvider` (registered via `RegisterPlugin`): complete, fail, pause/resume, cancel, cap compliance

## 2. Website plugin — crawler core

- [x] 2.1 Create `src/Downloader.Desktop.Plugins/Downloader.Desktop.Plugins.Website` (net10.0, `EnableDynamicLoading`, SDK ref `Private=false`, `<Version>1.0.0</Version>`) + add to solution; plugin id `com.bezzad.website-zip`, registers resolver + transfer provider
- [x] 2.2 Implement pure `LinkExtractor`: HTML attribute extraction (`href`/`src`/`srcset`/`poster`/`<link rel=stylesheet>`/`<script src>`/inline `style url(...)`) + CSS `url(...)`/`@import` parsing, URL normalization (relative→absolute, strip fragments), page-vs-requisite classification — with unit tests
- [x] 2.3 Implement pure `LocalPathMapper`: URL → `<host>/<path>` layout, default doc `index.html`, query-string hashing, extension adjustment from content-type, relative-path rewriting between local files — with unit tests
- [x] 2.4 Implement `SiteCrawler`: BFS same-host page recursion (depth 3 / 200 pages / 2000 assets caps), cross-host requisites downloaded once, link rewriting via the mappers, pause gate between requests, cancellation cleanup, progress callback (monotonic fraction, bytes, sliding-window speed)
- [x] 2.5 Implement `WebsiteTransferProvider`/`WebsiteTransfer` (`websitezip:` scheme): crawl to a temp dir, zip via `System.IO.Compression` to `<host>.zip` in the target folder, delete temp; map `SiteCrawler` progress to `TransferProgress`

## 3. Website plugin — resolver + variant trigger

- [x] 3.1 Implement `WebsiteResolver`: `IsFallback=true`; `CanResolve` = `websitezip:` scheme OR cheap page-like http(s) heuristic; default `ResolveAsync` = pass-through single-part plan
- [x] 3.2 Implement `GetVariantsAsync`: bounded content-type probe (HEAD, ranged-GET fallback, short timeout); offer "Offline copy (.zip)" `SubstituteUrl` variant (`websitezip:<url>`) only for `text/html`; null on any failure — with unit tests for the heuristic + variant shape
- [x] 3.3 Loopback integration test: serve a small multi-page site (index + subpage + external-host asset + CSS with `url()` font + image), run the real transfer end-to-end, assert the zip exists and contains rewritten, offline-consistent files; cover pause/resume and cancel-cleanup

## 4. Packaging, isolation, docs

- [x] 4.1 Extend `PluginIsolationTests` + verify the app csproj never references/stages the Website plugin; add it to `scripts/build-plugins.sh` and `packaging/plugins/optional-plugins.json` (catalog description)
- [ ] 4.2 Update docs (`docs/plugins-architecture.md` note on the now-consumed transfer path, README plugins table) + CLAUDE.md/SKILL.md notes; full build + all tests green; commit on `develop`

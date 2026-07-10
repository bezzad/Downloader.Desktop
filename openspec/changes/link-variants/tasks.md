# Tasks: link-variants

## 1. Plugin SDK (Abstractions)

- [ ] 1.1 Add `LinkVariant` POCO (`Id`, `Label`, `Description?`, `ExpectedSize?`, `IsDefault`) to `Pipeline.cs`
- [ ] 1.2 Add `ILinkResolver.GetVariantsAsync(url, ResolveOptions?, ct)` default-implemented to return null
- [ ] 1.3 Add `ResolveOptions.VariantId` (init-only, nullable)

## 2. Host wiring

- [ ] 2.1 `PluginManager.GetVariantsAsync(url, ct)` — enabled claiming resolver only; null on none/error (logged)
- [ ] 2.2 `DownloadItem.VariantId` (persisted, nullable) + flow through `DownloadManager.ResolvePlanAsync` into `ResolveOptions`
- [ ] 2.3 Unit tests: fake variant resolver via `RegisterPlugin` — lookup routing, disabled-plugin skip, VariantId reaches the resolver, retry re-uses the persisted id

## 3. Add window picker

- [ ] 3.1 `AddDownloadItemViewModel`: injectable variant-lookup seam, debounced fetch on single-URL change (per-keystroke CTS), `Variants`/`IsFetchingVariants`/`HasVariants` state
- [ ] 3.2 Gate `CanDownload` while a claimed URL's variant lookup is in flight; failure/none → enable with default behavior
- [ ] 3.3 Confirm → one `DownloadItem` per checked variant (default pre-checked; filename suffixed by variant label when >1); multi-URL input skips the picker
- [ ] 3.4 `AddDownloadItemView.axaml`: checkbox list + "Fetching options…" indicator; i18n keys in all 16 packs; RTL-safe
- [ ] 3.5 VM unit tests: fetch/cancel-on-edit, gating, multi-item build, fallback path

## 4. HLS plugin (v1.3.0)

- [ ] 4.1 Cache the last yt-dlp extraction per URL (short-lived) so list + resolve share one run
- [ ] 4.2 `SiteExtractor.ListVariants(json)` — distinct heights desc (+ sizes) + "Audio only"; best marked default
- [ ] 4.3 `SiteExtractor.Select(json, variantId)` — honor height pin / audio-only; null id = today's behavior exactly
- [ ] 4.4 `HlsResolver.GetVariantsAsync`/`ResolveAsync` overrides; bump csproj `<Version>` to 1.3.0
- [ ] 4.5 Unit tests on canned JSON: variant listing, height pin, audio-only, null-id regression

## 5. Ollama plugin

- [ ] 5.1 Accept tag-less model references for listing (`OllamaModelRef` parse extension)
- [ ] 5.2 `GetVariantsAsync` → registry tags list (+ sizes where cheap); fully-tagged input → null
- [ ] 5.3 `ResolveAsync` with `VariantId` resolves `<model>:<variant>`; bump csproj `<Version>` (minor)
- [ ] 5.4 Unit tests with a fake registry: tag listing, variant resolve, tagged passthrough

## 6. Wrap-up

- [ ] 6.1 Full build + all tests green; live e2e: YouTube quality pick (720p + Audio only) and an Ollama tag list through the real app
- [ ] 6.2 Update CLAUDE.md/skill notes; refresh screenshots if the Add dialog changed visibly; commit + push develop

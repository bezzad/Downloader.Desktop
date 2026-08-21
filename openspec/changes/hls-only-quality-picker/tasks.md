# Tasks — hls-only-quality-picker

- [x] 1. Master-playlist quality picker: `GetVariantsAsync` lists STREAM-INF (highest = default, size from bandwidth × duration); media playlist returns null
- [x] 2. `ResolveAsync` honors `VariantId`; missing/unknown id still picks `Best()` (default download)
- [x] 3. `CanResolve` no longer claims YouTube/x.com/… page URLs
- [x] 4. Delete yt-dlp / deno / SiteExtractor and their tests; ffmpeg is the only `IHasRuntimeDependencies` entry
- [x] 5. Bump plugin `<Version>` 1.4.0 → 2.0.0; catalog name/description; i18n Plugins_Desc; docs/skill/AGENTS
- [x] 6. `dotnet build` + `dotnet test` green (HLS tests + full suite)

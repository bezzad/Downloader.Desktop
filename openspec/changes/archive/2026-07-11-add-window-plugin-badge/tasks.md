# Tasks — add-window-plugin-badge

## 1. Fix + badge

- [ ] 1.1 `DownloadManager`: pure `UrlLooksLikePage(url)` + skip `IsExpiredOrInvalidLink` for page-like source URLs; unit tests (docs page passes, signed .zip still caught)
- [ ] 1.2 `PluginManager.FindResolverPluginName(url)` (claiming plugin's display name, fallback-ordered) + test
- [ ] 1.3 `AddDownloadItemViewModel`: `getResolverName` seam, `ResolverName`/`HasResolver` recomputed on URL change; `MainViewModel` wires it; VM tests
- [ ] 1.4 `AddDownloadItemView.axaml`: badge pill; `Add_HandledBy` key in all 16 i18n packs; build + full tests green; install built Website plugin locally for the author; commit on `develop`

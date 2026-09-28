# Tasks — add-window-plugin-badge

## 1. Fix + badge

- [x] 1.1 `DownloadManager`: pure `UrlLooksLikePage(url)` + skip `IsExpiredOrInvalidLink` for page-like source URLs; unit tests (docs page passes, signed .zip still caught)
- [x] 1.2 `PluginManager.FindResolverPluginName(url)` (claiming plugin's display name, fallback-ordered) + test
- [x] 1.3 `AddDownloadItemViewModel`: `getResolverName` seam, `ResolverName`/`HasResolver` recomputed on URL change; `MainViewModel` wires it; VM tests
- [x] 1.4 `AddDownloadItemView.axaml`: badge pill; `Add_HandledBy` key in all 16 i18n packs; build + full tests green; install built Website plugin locally for the author; commit on `develop`

> 2026-09-28: boxes ticked after checking the code. All four were implemented but never ticked:
> `UrlLooksLikePage` (`DownloadManager.cs`, tests in `DownloadManagerGuardTests`/`LogicTests`),
> `FindResolverPluginName` (`PluginManager.cs`, tests in `FallbackResolverTests`/`PluginManagerRobustnessTests`),
> `ResolverBadgeText` (`AddDownloadItemViewModel.cs`, wired in `MainViewModel.cs`, test in `AddDialogTests`),
> the badge in `AddDownloadItemView.axaml`, and `Add_HandledBy` in all 16 language packs.

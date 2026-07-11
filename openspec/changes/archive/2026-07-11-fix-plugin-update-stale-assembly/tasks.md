# Tasks: fix-plugin-update-stale-assembly

- [x] 1.1 Diagnose: verify release assets/catalog correct (latest = v2.0.0, HLS zip = 1.3.0)
- [x] 1.2 Diagnose: disk forensics on the snap — installed DLL byte-identical to released 1.3.0, yet app reports 1.1.2
- [x] 1.3 Reproduce in-process with real v1.9.0/v2.0.0 zips → by-path assembly image cache confirmed
- [x] 2.1 Fix: `PluginLoadContext` loads plugin DLLs via `LoadFromStream` (entry + deps)
- [x] 2.2 Fix: `InstallOrUpdateAsync` removes the old copy only after the download succeeds
- [x] 2.3 Fix: `UpdateInstalledAsync` re-syncs lists on failure
- [x] 3.1 Regression test `Plugins/PluginReloadTests` (same-path content swap must load new content)
- [x] 3.2 Full build + suite green (360/360); skill note added; committed `dd2e8e5` on develop

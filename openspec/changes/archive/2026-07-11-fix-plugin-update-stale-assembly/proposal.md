# Proposal: fix-plugin-update-stale-assembly

## Why

User-reported on the v2.0.0 snap (Ubuntu): Settings → Plugins showed HLS **v1.1.2** with an Update
badge; clicking **Update did nothing**, and **Remove + Add "reinstalled 1.1.2"** — while disk
forensics proved the 1.3.0 DLL had been downloaded, sha256-verified, and extracted correctly
(byte-identical to the `v2.0.0` release asset).

Root cause: the .NET runtime **caches loaded assembly images by file path**. The update swaps
`plugins/<id>/<name>.dll` in place, and `AssemblyLoadContext.LoadFromAssemblyPath` on that same path
returns the **old** image — even from a fresh collectible ALC, even after the old ALC was unloaded.
Reproduced in-process with the real v1.9.0 (1.1.2) and v2.0.0 (1.3.0) release zips: the install
"succeeded" but `InstalledVersion` still returned `1.1.2`.

## What Changed

- **`PluginLoadContext.LoadPluginAssembly`**: plugin DLLs (entry + ADR-resolved deps) load via
  `LoadFromStream`, which bypasses the by-path image cache — a replaced file always loads its
  current bytes. (Side effect: plugin `Assembly.Location` is empty; nothing here relies on it.)
- **`PluginCatalogService.InstallOrUpdateAsync`**: the old copy is removed only **after** the new
  zip downloads — a failed download no longer leaves the plugin silently uninstalled behind a
  stale "installed" row.
- **`PluginsViewModel.UpdateInstalledAsync`**: re-syncs the plugin lists on failure too, so the UI
  never shows stale state.
- **Regression test** `Plugins/PluginReloadTests` (CI-safe, no network): load Ollama DLL from a
  path, remove it, copy the HLS DLL over the **same** path, assert the new id loads.

## Outcome

Done and green: build clean, **360/360 tests pass**. Committed to `develop` as `dd2e8e5`.
The affected machine self-heals on app relaunch (the path cache is per-process and the correct
1.3.0 DLL is already on disk); other v2.0.0 users get the fix with the next app release
(author is batching further issue fixes before releasing).

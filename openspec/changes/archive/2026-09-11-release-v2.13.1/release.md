# Released in v2.13.1 (2026-09-11)

A patch release for one regression in v2.13.0: its optional plugins could not load in any app older than
2.13.0, and pressing Update removed a plugin that was working. Cut from `develop` with a clean tree;
`release.sh` ran unattended end to end.

## What was broken

v2.13.0 (4c42789) added `IPluginContext.CreateHttpClient()` and `ProxyAddress` to the plugin SDK, and every
catalog plugin (HLS 2.4.0, Website 1.1.0, SiteMedia 1.4.0) called them in `Initialize`. The default
interface implementation lives in the NEW SDK assembly; an older app loads its own, so the call threw
`MissingMethodException` and no plugin registered — "The downloaded package contained no plugin." The
catalog still declared `minAppVersion` 1.7.0 / 2.1.0 / 2.1.0, so older apps were offered those packages,
and `PluginCatalogService.InstallOrUpdateAsync` removed the installed copy before the new one failed.

## What shipped

- **Live catalog, before any code (2026-09-11):** `plugins-catalog.json` on the v2.13.0 release was
  re-uploaded with `minAppVersion` 2.13.0 for all three entries (nothing else changed), so older apps
  stopped being offered the packages immediately.
- **`014a77e`** — each catalog plugin calls the newer SDK members through its own `HostCompat` (a
  non-inlined call + `catch (MissingMethodException)` → a plain client / no proxy). Versions: Streaming
  media 2.4.1, Website offline copy 1.1.1, Video sites 1.4.1. `optional-plugins.json` now declares
  `minAppVersion` 2.13.0 (author's choice: "both" — gate the catalog AND make the plugins tolerant).
- An update backs up the plugin folder and restores + reloads it when the new package cannot load or its
  tools fail to install (`PluginCatalogService.BackUp`/`Restore`, `PluginManager.InstalledFolder`).
- Tests: `Plugins/OldHostCompatibilityTests` loads each plugin against the REAL v2.12.0 SDK
  (`Plugins/Fixtures/OldSdk/v2.12.0`, from the v2.12.0 macOS bundle). Verified to fail on the unfixed
  plugins with the production `MissingMethodException`, and pass with the fix.
  `PluginInstallFlowTests.An_update_that_cannot_load_keeps_the_copy_that_worked` pins the rollback.
  Full suite 1775/1775, rebuild 0 warnings.

## Channels

- **Tag**: `v2.13.1` on `main` (`fdd614a`); develop head after the mirrors `34236a4`
- **GitHub Release**: 13 assets; `release.yml` run `34649452672` green (notes, AUR, deb, plugins+catalog
  jobs all success). Live catalog: hls 2.4.1, website-zip 1.1.1, site-media 1.4.1, all `minAppVersion`
  2.13.0.
- **curl installer**: serves `releases/latest` → `v2.13.1`
- **Snap**: `snap.yml` run `34649452660` green; `latest/stable` = 2.13.1 (rev 30)
- **Homebrew**: `bezzad/homebrew-tap` at 2.13.1 (`5be2e3a`); in-repo mirror synced (`09f5003`)
- **winget**: PR [microsoft/winget-pkgs#433438](https://github.com/microsoft/winget-pkgs/pull/433438)
  (awaits moderator); in-repo mirror bumped (`6dfa83e`). The 2.13.0 PR #433353 is still open and is now
  superseded.
- **AUR**: the `aur` job logged "Published downloader-bin 2.13.1 to the AUR"; in-repo mirror bumped
  (`34236a4`).

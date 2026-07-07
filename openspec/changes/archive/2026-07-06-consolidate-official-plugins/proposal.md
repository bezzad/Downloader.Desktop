## Why

First-party plugins are split across two repos today: built-ins (`GitHub`, `Ollama`) live and release
inside `Downloader.Desktop`, while the HLS/video-site plugin lives in a separate `Downloader.Plugins`
repo that has git tags (`v1.1.0`–`v1.1.2`) but **zero actual GitHub Releases** — there is no downloadable
asset, so users cannot find or install it, and a live bug (pure YouTube/x.com video-site links fail to
resolve) can't be fixed and shipped without a release pipeline that doesn't exist. This also partially
blocks the in-progress `add-video-site-extraction` change, whose last task depends on a working, host-wired
HLS resolver. Consolidating all first-party plugin source into this repo — with a clear built-in vs.
optional split — lets one team (human + AI) fix, version, and ship plugins the same way the app itself
already does, and gives users an actual in-app way to discover and install optional plugins.

## What Changes

- Move `Downloader.Plugins.Hls`'s source and tests into this repo as
  `src/Downloader.Desktop.Plugins/Downloader.Desktop.Plugins.Hls`, fixing the YouTube/x.com pure video-link
  resolution bug as part of the move. A `Downloader.Desktop.Plugins.Torrent` project may be scaffolded per
  `docs/plugins-hls-torrent-plan.md` but is lower priority than shipping the Hls migration + fix.
- Introduce an explicit **optional/catalog plugin tier**, distinct from the existing built-in tier:
  - Built-in (`GitHub`, `Ollama`, unchanged): staged into the app's own build/publish output, bundled with
    every install, disable-only, updates with the app.
  - Optional/catalog (`Hls`, future `Torrent`): compiled and tested as part of this solution but **never**
    referenced or copied into the app's own build output. Not present on a fresh install. Discovered and
    installed on demand from Settings → Plugins.
- Extend the release workflow (`.github/workflows/release.yml`) so that, on every `vX.Y.Z` tag, it also
  builds and zips each optional plugin (sha256-hashed) and attaches those archives — plus a generated
  `plugins-catalog.json` manifest (`id`, `name`, `description`, `version`, `assetName`, `sha256`,
  `minAppVersion`) — to that same GitHub Release. Each optional plugin keeps its own version, bumped only
  when its code changes.
- Add an in-app catalog UI (Settings → Plugins) listing not-yet-installed optional plugins in a
  de-emphasized style with an **[Add]** action; clicking it downloads the plugin's asset, **verifies its
  sha256 against the catalog entry before extracting or loading anything**, places it in the existing user
  plugins folder, and loads it through the existing `PluginManager` mechanism. Once installed it behaves
  like any user-installed plugin (`[Disable]`/`[Remove]`).
- Add update checking for installed optional plugins: compare installed `PluginDescriptor.Version` against
  the catalog's version (same latest-release fetch used for install), surface a notification, and only
  download/verify/swap on explicit user acceptance — never silently.
- **BREAKING (repo-level, not app-level)**: after this change is implemented and the author has manually
  confirmed it works end to end, the `bezzad/Downloader.Plugins` GitHub repo will be deleted. That deletion
  is an explicit manual follow-up for the author and is **out of scope for this change's tasks**.

## Capabilities

### New Capabilities
(none — this extends the existing `plugins` capability rather than introducing a separate one)

### Modified Capabilities
- `plugins`: adds an optional/catalog plugin tier alongside the existing built-in/user-installed tiers —
  catalog discovery (`plugins-catalog.json` off the latest GitHub Release), on-demand install with
  mandatory sha256 verification before load, and user-accepted update checking for optional plugins. The
  existing built-in-plugin and user-installed-plugin requirements are unchanged.

## Impact

- **Repos**: source, tests, and CI history of `Downloader.Plugins` (the `Hls` plugin) move into
  `Downloader.Desktop`; the external repo is deleted after manual confirmation (post-implementation, not a
  task here).
- **Code**: new `src/Downloader.Desktop.Plugins/Downloader.Desktop.Plugins.Hls` project (solution-only
  reference, no app build dependency); `Services/PluginManager` gains catalog/install/verify/update
  surface; Settings Plugins view gains a catalog section; `Services/UpdateService`'s
  latest-release-fetch pattern is reused/extended for the catalog manifest.
- **CI/CD**: `.github/workflows/release.yml` gains jobs to build, zip, hash, and attach optional-plugin
  assets and the catalog manifest to the existing app release — no new release track or trigger.
- **Coordination**: the HLS fix unblocks `add-video-site-extraction`'s remaining manual-check task; that
  change's already-completed tasks are not duplicated here.

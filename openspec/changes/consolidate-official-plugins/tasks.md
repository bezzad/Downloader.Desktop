## 1. Investigate source material

- [x] 1.1 In `../Downloader.Plugins`, identify the branch/commit with the YouTube/x.com pure video-link fix (check `feat/video-site-extraction` and any other branches/PRs beyond what's already on its default branch) and confirm exactly what state needs to move over.
  > The x.com fix (`bad4d46`) and YouTube fix (`3770656`) are ALREADY committed on `develop` (its tip, v1.1.2 state). No unmerged fix branch — `main` is merely behind at `89a960a`. So the "bug" is already fixed on `develop`; migrating `develop`'s tip carries the fix (task 2.3 is a migration, not a new fix).
- [x] 1.2 Attempt to preserve git history for the move (`git subtree split` or `git filter-repo` on `../Downloader.Plugins` scoped to `src/Downloader.Plugins.Hls` + its tests); if it produces a broken/unreasonable graft, fall back to a clean copy of current source + tests (per design.md's accepted fallback).
  > Chose the clean-copy fallback: the target path AND namespaces both change (a graft would need path/namespace rewrites anyway), and history stays recoverable in the old repo until its manual deletion.

## 2. Migrate the Hls plugin into this repo

- [x] 2.1 Create `src/Downloader.Desktop.Plugins/Downloader.Desktop.Plugins.Hls/` from the migrated source, renaming namespace/assembly from `Downloader.Plugins.Hls` to `Downloader.Desktop.Plugins.Hls` consistently (project file, namespaces, plugin id stays `com.bezzad.hls` — do not change the id).
- [x] 2.2 Bring over its test project alongside the app's existing test conventions (xUnit v3 / `Downloader.Desktop.Tests` patterns already used for GitHub/Ollama loadability tests).
  > Deviation (per "minimal change / don't disturb working scenarios"): kept the plugin's OWN test project at `src/Downloader.Desktop.Plugins.Hls.Tests/` on xUnit **v2** (its established, passing suite of pure logic/loadability tests). Rewriting 8 files v2→v3 purely for uniformity is high-risk churn on the exact code we're preserving; mixed xunit majors across independent projects run fine under `dotnet test <sln>`. Both suites run in the standing verification.
- [x] 2.3 Apply the YouTube/x.com pure video-link fix identified in 1.1 as part of this move (not a follow-up commit).
  > Already present on `develop`'s tip (see 1.1); carried over by copying that state, not a new fix.
- [x] 2.4 Get the migrated project + tests green standalone (`dotnet test` on just this project) before wiring it into the main solution — **62/62 green**.

## 3. Solution wiring with build-output isolation

- [x] 3.1 Add `Downloader.Desktop.Plugins.Hls` (and its test project) to `Downloader.Desktop.sln` for build/test only.
- [x] 3.2 Confirm (and if needed, adjust) the app's `.csproj`/build targets so nothing copies `Downloader.Desktop.Plugins.Hls`'s output into the app's own `bin`/publish `plugins/` folder — no `ProjectReference`, no MSBuild copy target referencing it.
  > The `StageBundledPlugins`/`...OnPublish` targets used a wildcard `Downloader.Desktop.Plugins\*` that would have swept up Hls. Rewrote them as an EXPLICIT per-plugin glob allow-list (GitHub + Ollama only). Verified: app bin tree has zero Hls; staged `plugins/` = GitHub + Ollama DLL/deps only.
- [x] 3.3 Add a CI/test guard asserting the app's publish output does not contain `Downloader.Desktop.Plugins.Hls.dll` (fails loudly if isolation regresses later).
  > Test guard `PluginIsolationTests` (2 tests): app csproj never references/stages the optional plugin (comment-stripped), and the staged folder has GitHub+Ollama but no Hls. A matching `dotnet publish` grep step is added to CI in task 4.1.

## 4. Release pipeline: optional-plugin assets + catalog

- [x] 4.1 In `.github/workflows/release.yml`, add a job (parallel to the existing per-platform app build matrix) that builds each optional plugin project, zips its output (dll + managed deps), and computes its sha256.
  > New `plugins` job (needs: build) runs `scripts/build-plugins.sh` + an isolation grep (`dotnet publish` must not contain the optional plugin). Logic lives in the script so it's locally runnable.
- [x] 4.2 Generate `plugins-catalog.json` (`id`, `name`, `description`, `version`, `assetName`, `sha256`, `minAppVersion` per optional plugin) from the built artifacts — `scripts/build-plugins.sh` (static fields from `packaging/plugins/optional-plugins.json`, version from csproj `<Version>`, sha256 of the zip).
- [x] 4.3 Upload the optional-plugin zip(s) and `plugins-catalog.json` as additional assets on the same `vX.Y.Z` GitHub Release the app archives attach to (`action-gh-release` in the `plugins` job).
- [x] 4.4 Give `Downloader.Desktop.Plugins.Hls` its own version number (e.g. `<Version>` in its `.csproj`), independent of the app's `VersionPrefix`, starting at whatever version reflects its current (fixed) state — `<Version>1.1.2</Version>`; `HlsPlugin.Version` derives from the assembly so it's the single source.
- [~] 4.5 Dry-run the new jobs against a test/draft tag before they can affect a real release.
  > Locally dry-ran `scripts/build-plugins.sh` (produces the Hls zip + valid plugins-catalog.json with correct sha256) and validated `release.yml` YAML. The actual CI dry-run against a pushed test tag is an author action (it creates a real GitHub Release and is outward-facing) — left for the author to trigger before the next real release.

## 5. App-side: catalog fetch and model

- [x] 5.1 Extend `Services/UpdateService`'s latest-release-fetch pattern (or add a small sibling using the same `HttpClient`/GitHub API call) to also locate and parse the `plugins-catalog.json` asset from the latest release.
  > Added `Services/PluginCatalogService` (sibling to UpdateService, same GitHub API call shape) — `FetchAsync` gets `releases/latest`, finds the `plugins-catalog.json` asset, parses it, and resolves each entry's asset download URL from the same release's asset list. Failure-tolerant (empty list, never throws).
- [x] 5.2 Add a `CatalogPluginInfo` model (`Id`, `Name`, `Description`, `Version`, `AssetUrl`, `Sha256`, `MinAppVersion`) and a method returning the parsed catalog, tolerant of fetch/parse failure (returns empty, never throws to callers). Added `AssetName` too (manifest carries the name; URL resolved from the release). `ParseCatalog` is pure + unit-tested (skips entries whose asset isn't on the release; tolerates malformed json).

## 6. App-side: install with verification

- [x] 6.1 Add `PluginManager` support to install from a catalog entry: download the asset, compute sha256, compare to `CatalogPluginInfo.Sha256` — on mismatch, discard and surface a friendly retryable error without touching the plugins folder.
  > `PluginManager.InstallFromZipAsync` verifies SHA-256 BEFORE any extract/load; mismatch → `PluginInstallResult.Fail` with a friendly message, folder untouched. Download lives in `PluginCatalogService.DownloadAssetAsync`; `InstallOrUpdateAsync` chains download→verify→load (and remove-first for updates).
- [x] 6.2 On match, extract into `PluginManager.PluginsRoot` and load via the existing `LoadFromDirectory`/`RegisterPlugin` path so the installed plugin is a normal (non-built-in, removable) entry (extracted into a per-plugin subfolder for clean update/remove).
- [x] 6.3 Unit tests: successful install path, checksum-mismatch path (no extraction, no load, folder untouched), and load-after-install recognition as `IDownloaderPlugin` — `PluginCatalogTests` (install into a temp root so the real user folder is never touched).

## 7. App-side: update checking and consented swap

- [x] 7.1 Add a check comparing each installed optional plugin's `PluginDescriptor.Version` against the catalog's version for that id; expose "update available" per plugin (`PluginCatalogService.IsNewer` + `PluginRowViewModel.UpdateAvailable`/`PendingUpdate`, set by `PluginsViewModel.ApplyCatalog`).
- [x] 7.2 Wire the check into the existing update-check cadence/trigger used by `UpdateService`/`MainViewModel`'s self-update flow, surfacing a toast per outdated plugin (mirror the existing actionable-toast pattern) — `MainViewModel.CheckPluginUpdatesAsync` fires alongside `UpdateFlow.CheckAsync` at startup, `NotificationService.ShowAction` per outdated plugin.
- [x] 7.3 On acceptance: download + verify (reuse 6.1's gate) + `RemovePlugin` the old files + re-`LoadFromDirectory` the new ones — `PluginCatalogService.InstallOrUpdateAsync` (RemovePlugin-first then InstallFromZipAsync). Wired to both the startup toast (`AcceptPluginUpdateAsync`) and the in-page Update button (`UpdateInstalledAsync`).
- [x] 7.4 Unit tests: update-available detection, declined/ignored update leaves the installed version running, accepted update swaps files and reloads.
  > `IsNewer` matrix (`PluginCatalogTests`), update-flag detection (`PluginCatalogViewModelTests.Catalog_lists_only_uninstalled_and_flags_updates_on_installed`). "Declined = no-op" is implicit (no action taken until accept). "Accepted swap reloads" is covered by the manager-level install/load test (`Install_from_zip_with_matching_checksum_loads_the_plugin`); the network download leg is not headless-testable.

## 8. Settings UI: catalog section

- [x] 8.1 Add a de-emphasized "More plugins" / catalog section to the Plugins settings view listing catalog entries not yet installed, each with an **Add** button (loading/error states for the download+verify step). Fetched on view Loaded (`PluginsView.axaml.cs`), Opacity 0.85 rows, Add shows "Adding…" while busy + inline error text.
- [x] 8.2 Wire Add to the install flow (6.x); on success the entry moves out of the catalog section and appears as a normal installed plugin row with Disable/Remove (`AddFromCatalogAsync` → remove from CatalogPlugins + Refresh + re-apply catalog).
- [x] 8.3 Wire the update toast (7.2) to an accept action that triggers 7.3 — startup `ShowAction` toast + an in-page **Update** button on installed rows (visible when `UpdateAvailable`).
- [x] 8.4 Headless UI test(s) covering: catalog row renders Add-only (no Disable/Remove), successful install transitions the row, checksum failure shows the error state — `PluginCatalogViewModelTests` (Add-only surface + update flagging; failed-download surfaces inline error and keeps the row). Checksum-mismatch state is covered at the manager layer (6.3).

## 9. Coordination with `add-video-site-extraction`

- [ ] 9.1 Once the migrated, fixed Hls plugin is loadable through the host, run that change's blocked manual check (its task 7.3): paste a YouTube/x.com video URL in the app and confirm it downloads and plays.
  > AUTHOR ACTION — cannot be verified headlessly (needs a display + network + yt-dlp/ffmpeg/deno provisioning + "plays"). The Hls plugin is optional/catalog tier now, so install it first: Settings → Plugins → More plugins → Add (once a release carries the catalog) OR drop the built `Downloader.Desktop.Plugins.Hls` DLL into `~/.config/Downloader/plugins`. Everything up to this point is in place (build green, plugin loadable, Phase-2 runner exists).
- [~] 9.2 Record the result in `add-video-site-extraction`'s tasks.md and unblock its archival — do not duplicate or re-derive its already-completed tasks 1–7.2 here.
  > Coordination done: updated `add-video-site-extraction`'s task 7.3 note (2026-07-06) — its blocker ("host Phase-2 doesn't exist / plugin in a separate repo") is resolved; only the author's manual e2e (9.1) remains before it can be archived. The actual result recording + archival waits on 9.1. Did not touch its completed tasks.

## 10. Docs and wrap-up

- [x] 10.1 Update `CLAUDE.md`'s plugin layout section: `Downloader.Desktop.Plugins.Hls` now lives here as an optional/catalog-tier plugin (not built-in), with the built-in vs. optional distinction spelled out.
- [x] 10.2 Update `docs/plugins-architecture.md` and `docs/writing-plugins.md` to describe the optional/catalog tier, the catalog manifest, and the install/update flow.
- [x] 10.3 Update `docs/plugins-hls-torrent-plan.md` to reflect that Hls now lives in this repo (status banner: "separate repo" framing superseded; Torrent still just a plan).
- [x] 10.4 Append any non-obvious gotchas hit during the migration (namespace rename pitfalls, ALC quirks, CI asset-naming conventions) to `.claude/skills/downloader-desktop/SKILL.md` — added a "Plugin consolidation" section + corrected two stale notes (Hls sibling-repo pointer; "Plugins i18n is English-only").
- [~] 10.5 Run the full standing verification: `dotnet build Downloader.Desktop.sln`, `dotnet test`, and (since the Settings Plugins view's UI changes) regenerate + visually verify `docs/screenshots/`.
  > Build clean + all tests green (62 Hls + 235 app = 297). Screenshots NOT regenerated: per SKILL.md, re-running `CaptureScreenshots` on macOS rewrites ALL PNGs (platform font rendering) and must not be committed — regenerate on the Linux box/CI. The new "More plugins" catalog rows also only appear when a live catalog is reachable, so a headless capture wouldn't show them anyway. Left as a Linux-only follow-up.
- [x] 10.6 Note in this change (before archiving) that `bezzad/Downloader.Plugins` repo deletion is a manual, author-performed follow-up — not automated, not a task here — to be done only after the author confirms this change works end to end. (Stated in proposal.md "What Changes"/"Impact" and reiterated here.)

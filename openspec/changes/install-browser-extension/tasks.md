> **Read first — this change WILL be implemented from a different session, possibly on a different machine.**
> Run `openspec show install-browser-extension`, then read `proposal.md` → `design.md` → the delta specs
> under `specs/`. **`design.md` → Context lists everything already established** (which browsers can and
> cannot be installed into, what manual loading costs, which repo services are the templates). Do not
> re-derive any of it, and do not re-open the "install it automatically with elevation" question — it is
> settled and the reasoning is in `proposal.md` → Why.
>
> **Invoke the `downloader-desktop` skill before your first edit** (build/run/test commands and the gotcha
> cache live there).
>
> **Every task ships with tests.** A task is done when its test exists, is new or changed for that task, and
> the suite is green — never on "it builds" or "I clicked it once". Pure logic → unit tests
> (`Downloader.Desktop.Tests/Unit`, or `node --test src/browser-extension/common.test.js` for extension
> logic); user-visible paths → a headless UI test; extension UI → the Playwright suite in
> `src/browser-extension/e2e`.
>
> **Zero build warnings is a hard rule.** Verify with `dotnet build Downloader.Desktop.sln -t:Rebuild
> --nologo` and read the `N Warning(s)` line — a plain incremental build re-reports nothing and can look
> clean when it isn't.
>
> Commit per numbered group on `develop`, message prefix `feat(extension-install):` (or `fix:`/`chore:` as
> fits). **Another session may share this checkout: stage explicit paths, never `git add -A`, and read
> `git status --short` before every commit** — anything outside your own files belongs to someone else.
>
> Groups are ordered so each one is independently shippable. **Group 1 is the cheapest and highest value —
> do it first even if the rest slips.** Groups 2–4 are the plumbing, 5–7 the app, 8 the UI, 9 wiring, 10 the
> close-out.

---

## 1. Fix the manifest version drift (independent, do this first)

- [ ] 1.1 Read the `version` field of `src/browser-extension/manifest.json` and
      `src/browser-extension/manifest.firefox.json`. They are currently **1.6.1** and **1.7.0**. Confirm
      which features the 1.7.0 work added (see the archived/active `extension-single-list-thumbnails-path`
      change) and that `manifest.json` is simply the one that was not bumped — i.e. the Chrome build is a
      feature release behind, not intentionally pinned.
- [ ] 1.2 Set `manifest.json`'s `version` to match `manifest.firefox.json`. Change nothing else in either
      file.
- [ ] 1.3 Add a test to `src/browser-extension/common.test.js` that reads both manifest files, parses them,
      and asserts the two `version` values are equal, with a failure message naming both values. It must not
      hard-code a version number — it compares the files to each other.
- [ ] 1.4 Run `node --test src/browser-extension/common.test.js`. Green.
- [ ] 1.5 Add one line to `src/browser-extension/PUBLISHING.md` under the existing "bump both manifests"
      instruction, noting that a test now enforces it.
- [ ] 1.6 Commit. This group touches only `src/browser-extension/`.

## 2. Publish a verifiable extension catalog with each release

- [ ] 2.1 Read `scripts/build-plugins.sh` lines ~40–110 (the catalog-generation loop) — it is the template:
      static fields from a checked-in JSON, version and sha256 computed from the built artifact, assembled
      with `jq`, written to a `dist/*.json`.
- [ ] 2.2 Create `packaging/extension/targets.json` — the static, human-edited half of the catalog. One entry
      per target: `id` (`chrome` / `firefox`), `family` (`chromium` / `gecko`), `name` (display name, e.g.
      "Chrome, Edge, Brave, Vivaldi, Opera"), `assetName` (the existing zip names from
      `scripts/build-extension.sh`), `minAppVersion`, and `storeUrl` — **set `storeUrl` to `null` for both
      today** (no listing is live; see `design.md` → Context). Add a comment-free `README.md` beside it
      explaining that adding a `storeUrl` here is the only step needed to switch a target to the store path.
- [ ] 2.3 Extend `scripts/build-extension.sh`: after the two zips are built and `verify_zip`'d, read
      `packaging/extension/targets.json`, and for each entry compute the version (from the corresponding
      manifest — `manifest.json` for `chrome`, `manifest.firefox.json` for `firefox`) and the sha256 of the
      built zip, then write `dist/extension-catalog.json` as an array of
      `{ id, family, name, version, assetName, sha256, minAppVersion, storeUrl }`.
      **Do not hand-write the version or sha256** — derive both, so the catalog cannot disagree with the zips.
- [ ] 2.4 Run `./scripts/build-extension.sh` locally. Verify: both zips still build, `verify_zip` still
      passes, `dist/extension-catalog.json` exists, and each entry's `sha256` matches
      `sha256sum dist/<assetName>`. Verify `jq . dist/extension-catalog.json` parses.
- [ ] 2.5 In `.github/workflows/release.yml`, add `dist/extension-catalog.json` to the `files:` list of the
      existing `extension` job's release step (alongside the two zips). Change nothing else about that job —
      in particular do not add `generate_release_notes` (see the skill's CI gotcha).
- [ ] 2.6 Verify the workflow YAML still parses (`python3 -c "import yaml,sys;yaml.safe_load(open('.github/workflows/release.yml'))"`).
- [ ] 2.7 Commit.

## 3. `BrowserDetector` — find installed browsers, read nothing else

- [ ] 3.1 Add `src/Downloader.Desktop/Models/DetectedBrowser.cs`: a small record/POCO with `Id`, `Name`,
      `Family` (a new `BrowserFamily { Chromium, Gecko }` enum), `ExecutablePath`.
- [ ] 3.2 Add `src/Downloader.Desktop/Services/BrowserDetector.cs`. Shape it like the other static,
      UI-free services (`UrlResolver`, `ShellLauncher`): a `public static IReadOnlyList<DetectedBrowser>
      Detect()` plus per-platform private helpers, every one failure-tolerant (an unreadable registry key or
      missing directory yields no entry, never an exception to the caller).
- [ ] 3.3 Hard-code the curated candidate table (see `design.md` → D3): Chrome, Chromium, Edge, Brave,
      Vivaldi, Opera as `Chromium`; Firefox, LibreWolf as `Gecko`. Each candidate carries its Windows
      registry client name + exe name, its Linux executable names, and its macOS `.app` bundle name.
- [ ] 3.4 Windows implementation: `Microsoft.Win32.Registry` only — walk
      `HKLM`/`HKCU` `SOFTWARE\Clients\StartMenuInternet\*` reading `shell\open\command` (strip surrounding
      quotes and any trailing arguments), then fall back to
      `SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\<exe>`. Annotate the method
      `[SupportedOSPlatform("windows")]` (this is how `CA1416` is fixed here — see CLAUDE.md → Zero build
      warnings). **Never spawn `reg.exe`** (`NoShellSpawnTests` fails the build on it).
- [ ] 3.5 Linux implementation: for each candidate executable name, probe each `PATH` entry plus the fixed
      list `/usr/bin`, `/usr/local/bin`, `/snap/bin`, `/var/lib/flatpak/exports/bin`,
      `~/.local/share/flatpak/exports/bin`. First hit wins.
- [ ] 3.6 macOS implementation: check `/Applications/<Bundle>.app` and `~/Applications/<Bundle>.app`;
      the executable path is the bundle's `Contents/MacOS/<binary>` when it exists, else the bundle path.
- [ ] 3.7 De-duplicate the result by `Id` and return a stable order (Chromium family first, then Gecko, each
      alphabetical) so the UI list does not reshuffle between opens.
- [ ] 3.8 **The privacy boundary is the point of this service.** Add
      `Downloader.Desktop.Tests/Unit/BrowserDetectorTests.cs` with: (a) `Detect()` never throws on this
      machine and every returned entry has a non-empty `Name` and an `ExecutablePath` that exists;
      (b) results are de-duplicated and ordered as specified; (c) a **source-scanning test** — in the spirit
      of `Unit/NoShellSpawnTests` — that reads `Services/BrowserDetector.cs` and fails if it mentions any
      profile/credential path fragment (`Cookies`, `Login Data`, `places.sqlite`, `cookies.sqlite`,
      `Local State`, `Default/Preferences`, `profiles.ini`). Follow `NoShellSpawnTests`' approach of
      stripping comments but still scanning string literals.
- [ ] 3.9 Extend `Unit/NoShellSpawnTests`' scanned set if the new files are not already covered by its
      globs — confirm by deliberately adding a `cmd /c` string to the new file, seeing the test fail, then
      removing it. (The skill notes this scanner caught its own author's leftovers; trust it.)
- [ ] 3.10 Build with `-t:Rebuild`, 0 warnings. Run the filtered tests. Commit.

## 4. `ExtensionCatalogService` — fetch and gate the catalog

- [ ] 4.1 Read `src/Downloader.Desktop/Services/PluginCatalogService.cs` in full. It is the template:
      static class, one `HttpClient` with a `Downloader.Desktop` user-agent, a `ReleasesUrlOverride`
      internal test seam, asset-URL resolution from the release's own asset list, `MeetsMinAppVersion`, and
      **every method failure-tolerant — empty list or false, never a throw to the caller**.
- [ ] 4.2 Add `src/Downloader.Desktop/Models/ExtensionCatalogEntry.cs` mirroring the catalog JSON from
      task 2.3, plus a resolved `DownloadUrl`.
- [ ] 4.3 Add `src/Downloader.Desktop/Services/ExtensionCatalogService.cs`: `FetchAsync(ct)` reads
      `extension-catalog.json` off the latest release and resolves each entry's `DownloadUrl` from that same
      release's assets. Reuse `PluginCatalogService.MeetsMinAppVersion` (make it `internal static` and shared
      if it is currently private — do not copy the logic) to drop entries the running app is too old for.
      Add an internal `ReleasesUrlOverride` seam.
- [ ] 4.4 Add `Downloader.Desktop.Tests/Unit/ExtensionCatalogServiceTests.cs` driving the seam at a loopback
      `HttpListener` (the repo's standard pattern — see the existing plugin-catalog tests): a well-formed
      catalog resolves entries with download URLs; a missing catalog asset yields an empty list; malformed
      JSON yields an empty list; an entry whose `minAppVersion` exceeds the running version is excluded; an
      entry naming an asset absent from the release is excluded.
- [ ] 4.5 Build (0 warnings), test, commit.

## 5. `ExtensionInstallService` — verify, then unpack, to a stable path

- [ ] 5.1 Read `PluginManager.InstallFromZipAsync` — it is the download → **verify sha256 before extract** →
      install template, including the friendly error on mismatch.
- [ ] 5.2 Add `src/Downloader.Desktop/Services/ExtensionInstallService.cs`. Expose `InstallRoot` (default
      `<config>/Downloader/extension`, i.e. beside `plugins/`, with an **internal `InstallRootOverride` seam
      so tests never touch the developer's real folder** — the plugin tests do exactly this) and
      `TargetPath(string targetId)` = `<InstallRoot>/<targetId>`.
- [ ] 5.3 `InstallAsync(ExtensionCatalogEntry entry, IProgress<double> progress, CancellationToken ct)`:
      1. download the asset to a temp file;
      2. compute sha256 and compare to `entry.Sha256` — **on mismatch delete the temp file, touch nothing
         else, and return a failure result with a plain-language reason**;
      3. validate every zip entry path (reject `..` segments, rooted paths, drive-qualified paths, and any
         entry whose resolved full path is not under the destination) — a bad entry aborts before the first
         file is written;
      4. extract with `System.IO.Compression.ZipFile` into `<TargetPath>.new` (**never `tar.exe`, never a
         shell**), setting no executable bit on anything;
      5. delete `<TargetPath>` if present, then move `.new` into place;
      6. write `<TargetPath>/installed.json` = `{ target, version, installedAt }`;
      7. return a success result carrying the path and version.
- [ ] 5.4 `ReadInstalled(targetId)` returns the `installed.json` contents or null. Tolerate a missing or
      corrupt file (return null) — it is a convenience cache, not a source of truth.
- [ ] 5.5 Clean up a stale `<TargetPath>.new` on the next install attempt.
- [ ] 5.6 Add `Downloader.Desktop.Tests/Unit/ExtensionInstallServiceTests.cs` (loopback server + a zip built
      in-test with `ZipFile`, `InstallRootOverride` to a temp dir, deleted in a `finally`):
      - a matching-checksum zip extracts, and `installed.json` reports the right version;
      - a mismatched checksum extracts **nothing** and reports a failure;
      - a mismatched checksum leaves an existing previous install **intact**;
      - a zip containing a `../escape.txt` entry aborts and writes nothing outside the destination;
      - two installs land on the same path (the stable-path requirement);
      - an interrupted extraction (simulate by pre-creating a read-only/blocking `.new`, or by cancelling)
        leaves the previous install intact rather than half-overwritten;
      - a network failure returns a failure result rather than throwing.
- [ ] 5.7 Add a test asserting **no path the service writes to is under any browser profile or extension
      directory** — i.e. every write target is under `InstallRoot`. Cheapest honest form: assert
      `TargetPath` is rooted at `InstallRoot`, and add the source-scan (as in 3.8) for browser-policy
      fragments (`ExtensionInstallForcelist`, `External Extensions`, `policies.json`, `Wow6432Node`).
- [ ] 5.8 Build (0 warnings), test, commit.

## 6. Report the extension's identity through the local API

- [ ] 6.1 In `src/browser-extension/common.js`, add a pure helper that builds the identity pair —
      `extVersion` from `api.runtime.getManifest().version` and `browser` as a coarse label (`firefox` when
      `globalThis.browser` is present, else `edge` when the user agent names Edg, else `chrome`). It must
      **never throw**: any failure yields an empty object, matching how `captureCookies` returns `[]` so a
      capture problem never blocks the send.
- [ ] 6.2 Attach it to the requests that already go out: query parameters on the GET forms
      (`/ping`, `/add?url=`, `/api/add?url=`, `/api/can-handle`) and JSON fields on the POST form. Also send
      an `X-Downloader-Extension: <version>; <browser>` header so `/ping` carries it too. **No new
      permission, no extra request.**
- [ ] 6.3 Add unit tests to `common.test.js` for the helper: correct label per environment, correct version,
      returns `{}` (never throws) when `getManifest` is unavailable. Remember the harness gotcha: `common.js`
      binds `api` at load, so set `global.chrome` **before** `require("./common.js")` and mutate its
      sub-objects per test.
- [ ] 6.4 In `src/Downloader.Desktop/Services/LocalApiService.cs`, add a pure parser for the identity (from
      query, JSON body, or header — header last) and record the result as an in-memory
      `LastSeenExtension { Version, Browser, At }` keyed by browser label. **Do not persist it and do not
      log it** — the file's existing route-name-only logging comment explains why the request URL is never
      logged; keep that discipline.
- [ ] 6.5 A request carrying **no** identity must behave exactly as before. Add a test that pins this
      (an older extension and the CLI both send none).
- [ ] 6.6 Add tests in `Downloader.Desktop.Tests/Unit` for the parser (all three carriers, malformed input
      ignored) and in `Integration` for the end-to-end record via the existing local-API e2e pattern.
      **Always `Stop()` `LocalApiService` unconditionally in a `finally`** — the skill records three tests
      that broke an unrelated test by conditionally restoring it.
- [ ] 6.7 Add a test asserting the reported identity does **not** appear in a saved `config.json` (mirror
      `Saving_the_config_keeps_the_referer_and_drops_cookies_and_headers`).
- [ ] 6.8 Bump the extension version in **both** manifests (task 1.3's test enforces this) — a behaviour
      change, so a minor bump. Build, run `node --test`, run `dotnet test` filtered. Commit.

## 7. `ExtensionInstallViewModel` — the decisions, with no UI in them

- [ ] 7.1 Add `src/Downloader.Desktop/ViewModels/ExtensionInstallViewModel.cs` following the repo's
      seam-driven style (`AddDownloadItemViewModel` takes a `getVariants` delegate; do the same here) so the
      VM is fully testable headlessly: inject the detector result, a catalog fetch delegate, an install
      delegate, and a "last seen extension" lookup. **The VM must not call the network or the OS directly.**
- [ ] 7.2 Expose one row per detected browser: `Name`, `Family`, `IsSelected`, plus
      `IsConnected`/`ConnectedVersion` from the last-seen lookup, and `UpdateAvailable` when the connected
      version is older than the catalog version for that family.
- [ ] 7.3 Expose per-family `Mode`: `Store` when the family's catalog entry has a non-null `storeUrl`,
      otherwise `Manual`. Expose `StoreUrl`, `InstalledPath`, `Steps` (a localized list per family), and
      `Limitations` (a localized string per family — the Gecko one **must** state the extension is removed
      when the browser restarts).
- [ ] 7.4 Expose `InstallCommand`, `CopyPathCommand`, `OpenFolderCommand`, `OpenStoreCommand`, plus
      `IsBusy`, `Progress`, and `ErrorMessage`. `IsBusy` disables the install action.
- [ ] 7.5 When a family's catalog entry is missing or excluded by `minAppVersion`, the row says the app
      needs updating and its install action is disabled — it must not silently offer nothing.
- [ ] 7.6 `OpenStoreCommand` launches **that browser's own executable at the store URL**, via
      `ShellLauncher` with the absolute path from `DetectedBrowser.ExecutablePath` and no shell. Use
      `ShellLauncher`'s existing `RunOverride`/`OpenOverride` internal seams in tests — the skill records a
      test that really ran `xdg-open` on CI because it had no seam.
- [ ] 7.7 Add `Downloader.Desktop.Tests/Unit/ExtensionInstallViewModelTests.cs`: store mode chosen when a
      `storeUrl` exists and manual mode otherwise; the Gecko limitation text is present in manual mode;
      `IsConnected` is false after a successful install with no request seen (**the show-don't-claim
      requirement — this is the test that matters most in this group**); `IsConnected` true with a reported
      identity; `UpdateAvailable` true only when the reported version is strictly older; an incompatible
      `minAppVersion` disables install with an explanatory message; a failed install surfaces
      `ErrorMessage` and leaves `IsBusy` false; `OpenStoreCommand` targets the right executable and URL.
- [ ] 7.8 Build (0 warnings), test, commit.

## 8. `ExtensionInstallView` + Settings entry point

- [ ] 8.1 Add `src/Downloader.Desktop/Views/ExtensionInstallView.axaml(.cs)` as a modal window in the repo's
      custom-chrome style. Copy the structure of `DownloadDetailsView`: `Background="Transparent"`,
      `TransparencyLevelHint="Transparent"`, root `Border` with `CornerRadius="10" ClipToBounds="True"` and
      **`Background="{DynamicResource SystemRegionColor}"`** (`ThemeBackgroundColor` is undefined here and
      made dialogs see-through), `<v:TitleBar ShowMinMax="False" />`, an `OnKeyDown` override closing on
      `Escape`, and — if it is resizable — a `<v:ResizeGrips />` as the last child of the wrapping `Panel`.
- [ ] 8.2 Lay out: a browser list (checkbox + name + family + a Connected dot mirroring the existing
      `LocalApiStatus` dot in `SettingView.axaml`), then a per-family panel with the primary action
      (Install / Open store), a progress bar while busy, and — after a manual install — the path in a
      selectable box with Copy and Open-folder buttons, the numbered steps, and the limitations text.
- [ ] 8.3 Add the entry point to `src/Downloader.Desktop/Views/SettingView.axaml` **inside the existing
      `IsVisible="{Binding EnableBrowserIntegration}"` region, directly under the "Local API address" row**
      (around line 289): a `Grid Classes="field" ColumnDefinitions="*,Auto"` with a label + hint on the left
      and an "Install browser extension" button on the right. Match the surrounding control sizing
      (`.ctrl` is `Width=148 Height=34 MinHeight=34`).
- [ ] 8.4 Add `DialogHelper.ShowExtensionInstall()` following the existing entry points, and **call
      `BeginModal(view)` before `ShowDialog`** — every modal here must, or a dialog opened from another
      dialog appears underneath it. Wire the Settings button through `SettingViewModel`.
- [ ] 8.5 Add all new i18n keys to **`en.json` first (it is the fallback), then all 16 packs** in
      `src/Downloader.Desktop/Assets/i18n/` with real translations: the button, the dialog title, per-family
      step text, per-family limitations, the copy/open/store actions, connected/not-connected, update
      available, and every error string. Bulk-edit with a `python3` script using
      `object_pairs_hook=OrderedDict` and `json.dump(..., ensure_ascii=False, indent=2)`.
- [ ] 8.6 Use `{i18n:Tr Key}` in XAML (never a literal string) and `Localizer.Instance["Key"]` in the VM.
      Any VM-computed localized string must subscribe to `Localizer.PropertyChanged` and re-raise, with an
      unsubscribe on teardown.
- [ ] 8.7 Add to `Downloader.Desktop.Tests/UI`: the view loads (extend `UI/ViewLoadTests` if that is where
      views are covered), Escape closes it, and the Settings button opens it. A dialog test needs
      `TestSupport/DesktopLifetimeScope` (headless has no lifetime, so every `DialogHelper` entry point
      early-returns without it), and the opened dialog is found via `scope.MainWindow.OwnedWindows`, **not**
      `AppLifetime.Windows`.
- [ ] 8.8 Add an i18n completeness test if one does not already exist for this dialog's keys: every new key
      present in every pack.
- [ ] 8.9 Build (0 warnings), test, commit.

## 9. Wire it up and document it

- [ ] 9.1 Register whatever needs DI in `App.axaml.cs` → `ConfigureServices()`, following the existing
      registrations. Prefer static services (as `PluginCatalogService`/`UrlResolver` are) over new DI
      entries unless state demands otherwise — smallest change that works.
- [ ] 9.2 On startup, alongside the existing app-update and plugin-update checks in `MainViewModel`, check
      whether any **connected** extension is older than the catalog version and, if so, show one actionable
      pointer to Settings. **Never auto-install, and never nag more than once per run** — mirror
      `CheckPluginUpdatesAsync`'s shape exactly (native notification, action lives in the window).
- [ ] 9.3 Update `README.md` → "Browser extension" → the **Install** block (currently "store listings
      pending review… load the unpacked extension"): lead with **Settings → Browser extension & local API →
      Install browser extension** as the way to get it, keep the store-links-when-published line, and keep
      the developer-mode link as the manual alternative. Do not overstate: say plainly that Chrome/Edge
      listings are not published yet and that a manually loaded extension shows a developer-mode notice.
- [ ] 9.4 Update `src/browser-extension/README.md` to mention the in-app installer as the recommended route.
- [ ] 9.5 Add a short "Extension install" section to `docs/codebase-index.md` naming the new files and
      "where to change what", per the repo's standing rule that the index stays current.
- [ ] 9.6 Commit.

## 10. Close out

- [ ] 10.1 `dotnet build Downloader.Desktop.sln -t:Rebuild --nologo` from `src/` → **`0 Warning(s)`**, 0 errors.
- [ ] 10.2 Bounded full suite from `src/`:
      `timeout -k 30 900 dotnet test Downloader.Desktop.Tests/Downloader.Desktop.Tests.csproj -v q --nologo
      --blame-hang --blame-hang-timeout 180s --blame-crash`. Green. **If it appears to hang, first run the
      named class alone** — the skill documents a known shared-dispatcher hang where the named test is
      merely where the dispatcher died, not the cause. Kill leftover hosts by PID (`pkill -f` matches your
      own shell and kills it).
- [ ] 10.3 `node --test src/browser-extension/common.test.js`. Green.
- [ ] 10.4 Playwright: `cd src/browser-extension/e2e && npx playwright test --workers=1` (serial — the specs
      share the fixed port range and take each other's ports in parallel). Green.
- [ ] 10.5 **UI changed, so refresh screenshots** (standing routine):
      `DLDESKTOP_CAPTURE=1 dotnet test Downloader.Desktop.Tests/Downloader.Desktop.Tests.csproj --filter
      FullyQualifiedName~CaptureScreenshots` from `src/`. The new Settings row and the new dialog are the
      point — if either sits below the fold, add a scrolled capture (and `md5sum` it against the unscrolled
      one to prove the scroll happened; the skill records scrolled captures that were silently identical).
      **View the PNGs before committing** and commit only what changed.
- [ ] 10.6 Manual check that cannot be automated here, and must be reported honestly rather than assumed:
      launch the app, open the dialog, install for Chrome, load the unpacked folder in a real Chrome, confirm
      the row flips to **Connected ✓** with the right version. Note in this file what was verified on which
      OS. Windows registry detection and macOS bundle detection are **unverifiable on this Linux box** —
      leave them explicitly unverified rather than claiming otherwise.
- [ ] 10.7 Tick every box above that actually passed. **Leave any failed or skipped task unchecked and write
      why in `proposal.md`/`design.md`** before ending the session — per CLAUDE.md, the next machine learns
      the true last state only from what is committed.
- [ ] 10.8 Append any non-obvious gotcha discovered along the way to
      `.claude/skills/downloader-desktop/SKILL.md` (short and factual) and commit it — standing rule.
- [ ] 10.9 `/opsx:sync` the delta specs into `openspec/specs/`, then `/opsx:archive` this change. Push
      `develop`.

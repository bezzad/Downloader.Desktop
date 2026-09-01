# Codebase index

A navigable map of this repository: what every project, folder and non-obvious file is for, and
where to start when you need to change something. Generated from the tree at `v2.3.0` / `develop`
(commit `afcde1c`, 2026-08-22).

**How this relates to the other top-level docs**

| File | Answers |
| --- | --- |
| `README.md` | End-user: what the app is, install, screenshots. |
| `CLAUDE.md` / `AGENTS.md` | Product decisions, standing conventions, roadmap history, release routine. |
| `.claude/skills/downloader-desktop/SKILL.md` | *How* to build/run/test + accumulated gotchas. |
| **`docs/codebase-index.md`** (this file) | *Where* everything is. |
| `CONTRIBUTING.md` | Publish/release/macOS-bundle mechanics. |

---

## 1. At a glance

- **App**: cross-platform desktop download manager (Avalonia UI 12 + .NET 10, ReactiveUI/MVVM),
  wrapping the external [`Downloader`](https://github.com/bezzad/downloader) engine (NuGet 5.9.5).
- **Version**: `2.3.0` (`src/Downloader.Desktop/Downloader.Desktop.csproj` → `VersionPrefix`;
  build/revision derived from UTC build time).
- **Scale**: ~150 C# files / ~24k lines, 16 `.axaml` views (+2 shared style/icon files), 310 test declarations, 16 UI languages,
  4 first-party plugins, 1 browser extension, 8 distribution channels.
- **Branching**: all work on `develop`; `main` is release-only. Progress tracking lives in
  `openspec/`, not in `PLAN.md`/`TASKS.md` (retired).

### Solution layout (`src/Downloader.Desktop.sln`)

| Project | Role |
| --- | --- |
| `Downloader.Desktop` | The app: entry point, DI, models, services, view models, views, assets. |
| `Downloader.Desktop.Plugins.Abstractions` | The plugin SDK — interfaces + POCOs only. External plugins reference just this. |
| `Downloader.Desktop.Plugins/*` | The four first-party plugins (two bundled, two catalog-tier). |
| `Downloader.Desktop.Tests` | The **single** test project (xUnit v3 + `Avalonia.Headless.XUnit`). |

Not in the solution: `src/browser-extension/` (plain JS, no bundler; its own `npm` e2e suite).

---

## 2. Startup path — read these first

The clearest way into the codebase is the launch sequence:

1. **`Downloader.Desktop/Program.cs`** — `Main`. Three-way fork before Avalonia even starts:
   - a recognized **CLI verb** (`add`/`list`/`pause`/…) → `CliParser.TryParse` → `CliRunner.Run` → exit;
   - a **second instance** → `SingleInstanceService.TryClaim` forwards argv to the primary and exits;
   - otherwise `BuildAvaloniaApp().StartWithClassicDesktopLifetime` (X11 `WmClass`, per-monitor DPI,
     Inter font, Skia, ReactiveUI).
2. **`App.axaml.cs`** — `ConfigureServices()` is the whole DI graph, and it is deliberately tiny:
   `IFileService`, `IDownloadManager`, `PluginManager` (singletons) + `MainViewModel` (transient).
   Every other view model is `new`-ed directly. Also: desktop-platform guard, theme application.
3. **`App.axaml`** — global styles, the ocean-blue/teal light+dark palette, control restyles.
4. **`ViewModels/MainViewModel.cs`** — the root VM; `SetupAppShell()` wires tray, startup, notch,
   taskbar progress, update flow, local API, scheduler timer and debounced autosave.

### Ports (fixed, loopback only)

| Port(s) | Owner | Purpose |
| --- | --- | --- |
| `15150`, then `15156–15158` | `SingleInstanceService.LockPorts` | Single-instance lock **and** IPC channel. Multiple ports because a foreign process can squat 15150. |
| `15151–15155` | `LocalApiService.PortRange` | HTTP API for the extension + CLI + scripts. Deliberately disjoint from the lock ports. |

The extension mirrors the API range in `browser-extension/common.js` (`APP_PORT_RANGE`) — MV3
requires static `host_permissions`, so discovery must stay inside that range.

### Windows: never spawn a shell

An unsigned exe spawning PowerShell is what got the app blocked and quarantined by Bitdefender
(issue #4), so all four sites now call the API in-process. **`NoShellSpawnTests` fails the build** if
`powershell`/`pwsh`/`Expand-Archive`/`WScript.Shell`/`-EncodedCommand`/`cmd /c`/
`--cookies-from-browser`/spawned `reg.exe` reappears in app or plugin source.

| Site | Was | Is now |
| --- | --- | --- |
| `Services/WindowsNotifier.cs` | `powershell.exe -EncodedCommand …` per toast | `Shell_NotifyIconW` + `NIF_INFO` on a hidden message-only window |
| `Services/StartMenuShortcut.cs` | `powershell` + WScript.Shell COM | `IShellLink` + `IPersistFile` COM, in-process |
| `Services/StartupService.cs` | spawned `reg.exe` | `Microsoft.Win32.Registry` |
| `Services/UpdateService.cs` | detached `.cmd` → `powershell Expand-Archive` | detached `.cmd` → `%SystemRoot%\System32\tar.exe` (absolute path) |

Remaining child processes, both legitimate: the update swap `.cmd` (a running process cannot replace
its own exe) and `Plugins.Hls/FfmpegBinary.cs` (`ffmpeg` itself, plus `tar` for `.tar.*` on
Linux/macOS — zip goes through managed `ZipFile`).

The three Windows paths above **cannot be exercised on Linux or in CI** (no Windows runner); they are
fail-soft and their pure parts are unit-tested, but behavior changes there need a manual smoke test.

---

## 3. `Downloader.Desktop` — the app

### `Models/` — persisted state (all JSON, via `FileService`)

| File | What it holds |
| --- | --- |
| `Config.cs` | Root persisted object: settings, download list, queues, schedules, theme, window size. |
| `DownloadSettings.cs` | User-facing mirror of the engine's `DownloadConfiguration` (which can't be serialized — it holds delegates). `ToConfiguration()` maps to the engine. Grouped Basic/Advanced/Request for the Settings UI. |
| `DownloadItem.cs` | One download record. `Urls` = primary + mirrors; folder and file name stored separately so a URL-only add can resolve its name later. |
| `DownloadQueue.cs` | Named group + concurrency cap. Items point at it via `DownloadItem.QueueId`. |
| `DownloadSchedule.cs` | Daily start/stop window over a queue or single item, on selected days. |
| `PersistedPlan.cs` | JSON-round-trippable copy of a resolver's `DownloadPlan`, so multi-part / post-process downloads survive a restart. |
| `CatalogPluginInfo.cs` | One entry of the release-hosted `plugins-catalog.json`. |

### `Services/` — the engine room

**Download core**

| File | Role |
| --- | --- |
| `IDownloadManager.cs` / `DownloadManager.cs` (1.3k lines) | **The choke point.** Owns every download's lifetime: builds `DownloadService` instances, relays engine events to row VMs, enforces state-transition guards (pause/cancel/resume/retry), and runs the queue pump (`MaxConcurrent`). All start paths funnel through the pump — nothing calls `Start` directly except the pump. It also owns **all recovery from a failed attempt**, in one place (`HandleFailure`) and in this order: another ADDRESS (`TryNextUrl` — the engine's extra urls are load spreading, not failover), then the same address over ONE connection (`TryReduceConnections`, which must discard the partial file because a resumed download keeps its old chunk layout), then re-resolving the original link (`TryAutoRefreshLink`). The last two are deliberately disjoint: a 403 cannot say whether it means "too many connections" or "this address is gone", so the backoff gets the first attempt and the link path owns everything after. |
| `DownloadManager.Plans.cs` | Multi-part plan execution (HLS/DASH segments, video+audio mux). Parts download into a hidden `.<name>.parts` folder, then the matching `IPostProcessor` assembles the final file. |
| `DownloadManager.Transfers.cs` | The `ITransfer` path — a plugin owns the whole transfer (used by the Website offline-copy plugin). |
| `PlanRunState.cs` | Thread-safe per-segment progress board a plan run writes to and the Details dialog polls. |
| `UrlResolver.cs` | Name/size preview without downloading (wraps the engine's `RemoteFileResolver`; single `Range: 0-0` GET, follows redirects). Has a URL fast-path, a concurrency semaphore and an 8 s timeout so rows never hang on "Fetching name…". |

**Persistence & logging**

`IFileService.cs`/`FileService.cs` (atomic temp+move write, `SemaphoreSlim`, exception-tolerant load,
`%AppData%/Downloader/config.json`) · `AppLog.cs` (opt-in daily file log; exposes the
`ILoggerFactory` handed to the engine so everything lands in one file).

**Automation surface**

`LocalApiService.cs` (loopback HTTP: the extension's `/add`, `/ping` + a JSON `/api/*` for
add/list/control) · `CliParser.cs` + `CliRunner.cs` (headless verbs; `add` goes through the
single-instance channel, the rest talk HTTP) · `CookieFile.cs` (cookie handoff from the browser
extension) · `SingleInstanceService.cs`.

**OS integration** (all fail-soft — an unsupported platform logs and stays inactive)

`TrayService.cs` · `NotchService.cs` (borderless topmost "dynamic island" overlay) ·
`TaskbarProgressService.cs` (Win ITaskbarList3, Linux Unity DBus, macOS documented skip) ·
`NotificationService.cs` + `MacNotifier.cs` (in-process NSUserNotification so the icon is *ours*)
+ `WindowsNotifier.cs` (PowerShell → WinRT toast) · `StartupService.cs` (Win Run key / XDG
autostart / LaunchAgent) · `StartMenuShortcut.cs` (winget's portable zip creates no Start-menu
entry) · `ShutdownService.cs` (opt-in power-off after all downloads, always cancelable) ·
`WindowActivation.cs` (Show and Activate in **separate** dispatcher ticks — see its comment) ·
`ThemeService.cs`.

**Updates & plugins**

`UpdateService.cs` (GitHub `releases/latest` + version compare) · `UpdateFlow.cs` (silent
background download → persistent "Update Downloader" button → swap **on exit**, then relaunch) ·
`PluginManager.cs` (discovery, collectible `AssemblyLoadContext`, `InstallFromZipAsync` with
**sha256 verified before load**) · `PluginCatalogService.cs` (reads `plugins-catalog.json` off the
latest release; every method failure-tolerant) · `PluginDependencyInstaller.cs` (downloads a
plugin's declared binaries — ffmpeg — through the app's own resumable engine).

**UI plumbing**

`DialogHelper.cs` (`ShowDialog<TView,TVm,TResult>`) · `Localizer.cs` (+ `Markup/TrExtension.cs`,
`{i18n:Tr Key}`; live language switch rides a `Tick` property because indexer-change
notifications were unreliable).

### `ViewModels/`

Root: `MainViewModel` (app shell, page switching via `CurrentPage`, bulk actions, autosave) ·
`Navigation.cs` (`NavSection`, `StatusFilter` enums).

Pages: `DownloadsViewModel` (filterable `DataGridCollectionView` over the manager's master
collection) · `QueuesViewModel` · `SchedulerViewModel` · `SettingViewModel` · `PluginsViewModel`.

Rows/items: `DownloadItemViewModel` (one grid row; live progress staged off-thread and flushed by a
single shared 250 ms `DispatcherTimer`) · `ChunkProgressViewModel`, `MirrorEntryViewModel`,
`QueueRowViewModel`, `QueueItemViewModel`, `ScheduleRowViewModel`, `PluginRowViewModel`,
`CatalogPluginRowViewModel`, `VariantOptionViewModel`.

Dialogs: `AddDownloadItemViewModel` (multi-URL + folder + link variants) ·
`DownloadDetailsViewModel` (per-connection segmented strip, live speed limit, mirror editor) ·
`AboutViewModel` · `DonateViewModel` · `ConfirmViewModel` · `ShutdownViewModel` ·
`UpdatePromptViewModel` · `NotchViewModel`.

### `Views/`

**Nav model**: there is no left rail and no page dialogs. The toolbar (bulk actions + page nav)
lives in `MainWindow`; the central `ContentControl` swaps `MainViewModel.CurrentPage` between
Downloads / Queues / Scheduler / Settings. Only Add-link, Details and About remain modal windows.
Plugins are a collapsible Settings section.

Windows/pages: `MainWindow`, `DownloadsView`, `QueuesView`, `SchedulerView`, `SettingView`,
`PluginsView`, `AddDownloadItemView`, `DownloadDetailsView`, `AboutView`, `DonateView`,
`ConfirmView`, `ShutdownView`, `UpdatePromptView`, `NotchView`.

Shared chrome/helpers: `TitleBar` (custom chrome drawn inside the client area; drags via
`BeginMoveDrag`) · `ResizeGrips` + `WindowResize.cs` · `PageViewCache.cs` · `UrlBoxPaste.cs`.

Support: `Converters/FileKindToIconConverter.cs`, `Converters/StatusToBrushConverter.cs`,
`Behaviors/NumericCoerce.cs`.

### `Assets/`

`Icons.axaml` (vector icon geometries) · `i18n/*.json` — **16 languages** (ar, az, de, en, eo, es,
fa, fr, hi, it, ja, ko, pt, ru, tr, zh; ar/fa render RTL) · `flags/*.svg` (language picker) ·
app icons (`.ico`/`.icns`/`.png`), `Info.plist`, `config.json`.

---

## 4. Plugins

### The SDK — `Downloader.Desktop.Plugins.Abstractions`

Four files, no dependencies beyond the engine's types:

| File | Contracts |
| --- | --- |
| `IDownloaderPlugin.cs` | `IDownloaderPlugin` (entry point; parameterless ctor + `Initialize`), `IPluginContext` (incl. `Logger`). |
| `Pipeline.cs` | `ILinkResolver` (link → `DownloadPlan`; resolves, never downloads), `LinkVariant`, `ResolveOptions`, `ITransferProvider`/`ITransfer`, `IPostProcessor`. |
| `PostDownload.cs` | `IPostDownloadAction` — a user-initiated action on a completed download ("Add to Ollama"). |
| `RuntimeDependencies.cs` | `PluginBinaryDependency`, `IHasRuntimeDependencies` — the host downloads the binary resumably, the plugin finishes placing it. |

Three-phase pipeline: **resolve → download (host) → post-process**. Docs:
`docs/plugins-architecture.md`, `docs/writing-plugins.md`.

### The two tiers

**BUILT-IN** — bundled, disable-only, not removable, ships with the app. Staged into the output's
`plugins/` folder by the app csproj's `StageBundledPlugins` target, which is an **explicit
per-plugin allow-list, not a wildcard** — that is what keeps optional plugins out of the build.

| Plugin | Id | What it does |
| --- | --- | --- |
| `Downloader.Desktop.Plugins.GitHub` | `com.bezzad.github-releases` | **The template plugin** — implements *every* SDK interface in a small real context. Copy it to start your own. |
| `Downloader.Desktop.Plugins.Ollama` | `com.bezzad.ollama-models` / `1.2.0` | `gemma3:12b` / ollama.com links → model blob download + an "Add to Ollama" install that writes the manifest LAST so Ollama never sees a half-install. **1.2.0 adds HuggingFace**: `HuggingFaceResolver` claims `huggingface.co/<owner>/<repo>` (and `/tree`, `/blob`, `/resolve` forms, never datasets/spaces/profiles — parsing is pure, no network), lists the repo's GGUF files through `IHuggingFaceApi` and offers them as variants (quantisation + size, smallest default); `AddHuggingFaceToOllamaAction` installs into `hf.co/<owner>/<repo>:<quant>`, verifying the file against the digest the REPOSITORY publishes (there is no manifest) — asked for at install time so the check survives a restart. A sharded GGUF set, a repo with no GGUF, and a private/missing repo each fail with a message naming which. |

**OPTIONAL / catalog tier** — *not* bundled, *not* referenced by the app, absent on a fresh
install; in the solution for build/test only. Installed on demand from Settings → Plugins.

| Plugin | Id / version | What it does |
| --- | --- | --- |
| `Downloader.Desktop.Plugins.Hls` | `com.bezzad.hls` / `2.2.0` | *Streaming media (HLS & DASH).* `.m3u8` → one part per segment + a `Concat` recipe; quality picker from master playlists; AES-128 decrypt, concat, ffmpeg `-c copy` remux. **`.mpd` (MPEG-DASH, 2.2.0)** → `Dash/DashResolver` + `Dash/MpdParser` expand a static manifest (SegmentTemplate ±SegmentTimeline, SegmentList, SegmentBase/BaseURL) into video parts then audio parts; the recipe's `Streams` groups say where one stream ends, so the post-processor concatenates each and muxes them. Live (`type=dynamic`) and DRM manifests are refused with a reason (`DashException`). ffmpeg is downloaded on first use (`FfmpegBinary`, `BinaryFile` guards truncated downloads), never bundled. Interfaces (`IFfmpeg`, `IM3u8Parser`, `IMpdParser`, `IContentTypeProbe`) exist so the unit tests stay network-free. **2.0.0 dropped yt-dlp/deno site extraction** — no third-party executables, no browser-cookie reading. |
| `Downloader.Desktop.Plugins.Website` | `com.bezzad.website-zip` / `1.0.1` | Save a page/site as an offline-browsable `.zip`. A **fallback** resolver (specific resolvers always win) offering an "Offline copy (.zip)" variant on `text/html`; the crawl runs through the app's `ITransfer` path. `LocalPathMapper` assigns every URL its zip path *before* download so earlier documents can rewrite references to later ones. Requires app ≥ 2.1.0 via the catalog's `minAppVersion`. |

| `Downloader.Desktop.Plugins.SiteMedia` | `com.bezzad.site-media` / `1.0.0` | *Video sites (YouTube and others).* A page URL on a supported host → the real stream(s) via yt-dlp (`IYtDlp`, stubbed in tests), offered as per-quality variants; one progressive part, or a video+audio pair muxed by `MuxPostProcessor`/`FfmpegMuxer`. **The only component in the repo that runs a third-party binary**: fetched on first use, checked against the digest its publisher lists (`ToolChecksum`) BEFORE it is made executable, started from an absolute path with no shell. It reads **no** browser profile or cookie store — a session arrives only as the cookie file the extension captured (`ResolveOptions.CookieFilePath`). An adaptive-only page is refused with a reason rather than duplicating the HLS plugin's segment pipeline. Its own `ToolFile`/`FfmpegMuxer` mirror the HLS plugin's rather than sharing them, because the two are installed independently. |

**Release plumbing**: `scripts/build-plugins.sh` (run by `release.yml`) zips the optional plugins and
generates `plugins-catalog.json` from `packaging/plugins/optional-plugins.json` + the built
version/sha256, attaching both to the same `vX.Y.Z` release. Isolation is guarded by
`PluginIsolationTests` **and** a grep in `release.yml`.

> **Standing rule**: any change under `src/Downloader.Desktop.Plugins/*` must bump that plugin's
> csproj `<Version>` in the same session — the catalog's update check compares installed vs catalog
> version, so a stale version means users never get the fix.

---

## 5. `src/browser-extension` — browser integration

Plain JS, Manifest V3, no build step (load unpacked). Version `1.7.0`. Ships for
Chrome/Edge (`manifest.json`) and Firefox (`manifest.firefox.json`). There is **no content script**:
1.7.0 removed `content.js` (its only job was a relevance hint that has been deleted), so nothing of
the extension runs on a page except while the popup is open.

| File | Role |
| --- | --- |
| `background.js` | Service worker / event page: context menus, per-tab video/audio/HLS sniffing, badge, forwarding to the app. `.mpd` (DASH) is **not** sniffed at all since 1.7.0 — unprobeable and quality-less, so it could only be a row the popup's ordering rule can say nothing about; the app still takes a pasted `.mpd`. |
| `common.js` | Shared helpers: app port discovery over `APP_PORT_RANGE`, media grouping, list ordering (`sortDetectedGroups` = HLS first, then `qualityHeight`, then size), thumbnail mapping (`buildThumbnailIndex`/`pickThumbnail`), download folder (`getSavePath`/`fetchAppDefaultSavePath`). |
| `popup.js/.html/.css` | The popup: ONE list of detected media, best copy first (HLS → quality → size), a preview thumbnail per row, a size/quality upgrade pass, link scan, paste-a-URL, send one/all. |
| `options.js/.html/.css` | Settings: the download folder (prefilled from the app's `/api/settings`) and the interception rules. |
| `common.test.js` | `node --test` unit suite. |
| `e2e/` | Playwright suite (own `package.json`) with a fixture server and real `.m3u8`/`.mp4` fixtures. |

Also: `README.md`, `PRIVACY.md`, `PUBLISHING.md`. Built by `scripts/build-extension.sh`; the
zips are attached to each release by `release.yml`. Store uploads are manual (the automated AMO
workflow was removed 2026-08-24).

---

## 6. Tests — `src/Downloader.Desktop.Tests`

One project, folders with matching namespaces, no loose `.cs` at the root. **310 test
declarations** (`[Fact]`/`[Theory]`/`[AvaloniaFact]`/`[AvaloniaTheory]` + the `Timed*` wrappers).

| Folder | Contents |
| --- | --- |
| `Unit/` | `LogicTests` (state guards, queue cap, progress flush), `LocalizationTests`, `UpdateSwapScriptTests`, `CookieHandoffTests`, `StartMenuShortcutTests`, `WindowResizeTests`, `AurPackagingTests`. |
| `Integration/` | Real engine over a loopback server: `IntegrationTests`, `SpeedLimitTests`, `MemoryReleaseTests`, `DownloadStateDurabilityTests`, `PlanRunnerTests`, `ShutdownVerificationTests`, `LocalApiCliTests`, `XcomRepro`. |
| `UI/` | Avalonia headless: `AppTests` (1.8k lines — the biggest single file in the repo), `DialogHelperTests`, `TrayServiceTests`, `NotchTests`, and **`CaptureScreenshots`** (gated). |
| `Plugins/` | Plugin loading/isolation/reload, catalog, dependency installer, transfer path, link variants, Ollama; `Plugins/Hls/` (m3u8 + MPD parsers, both resolvers, post-processor, AES, loopback server + committed `.m3u8`/`.mpd` fixtures) and `Plugins/Website/`. |
| `TestSupport/` | `TestAppBuilder` + assembly attributes, `GlobalUsings`, `TimedAttributes`. Stays at the root namespace. |

New tests go in the folder that fits, under its sub-namespace.

**Screenshots** are produced by the gated `CaptureScreenshots` fact (env `DLDESKTOP_CAPTURE=1`)
into `docs/screenshots/` — 22 PNGs, light+dark pairs, deterministic (unchanged UI re-renders
byte-identical). Refreshing them when a view changes is part of "done".

---

## 7. Packaging & distribution

| Channel | Where |
| --- | --- |
| GitHub Release archives (win/linux/macOS ×2) | `.github/workflows/release.yml` + `scripts/publish.sh` |
| Linux curl-script | `scripts/install.sh` (+ `uninstall.sh`), Windows `scripts/install.ps1` |
| Homebrew cask | `Casks/downloader.rb` (in-repo mirror of `bezzad/homebrew-tap`) |
| winget | `packaging/winget/*.yaml` → PR to `microsoft/winget-pkgs` (id `bezzad.Downloader`, moniker `downloader`) |
| Snap | `snap/snapcraft.yaml`, `snap/gui/`, `.github/workflows/snap.yml`, `scripts/build-snap.sh` |
| Debian/APT | `packaging/apt/`, `scripts/build-deb.sh`, `scripts/build-apt-repo.sh` |
| Arch/AUR | `packaging/aur/PKGBUILD` + `.SRCINFO`, `scripts/bump-aur.sh` (published **from CI**, not from the release machine) |
| MSIX / Store | `packaging/msix/` + `scripts/build-msix.ps1` |
| macOS `.app` | `scripts/make-macos-app.sh` (needs `IsMacBuild=true` → `net10.0-macos`, the `macos` workload + Xcode; CI releases build plain `net10.0`) |
| Plugin catalog | `packaging/plugins/optional-plugins.json` + `scripts/build-plugins.sh` |

Windows binaries are **not Authenticode-signed** — see issue #4 (antivirus behavioral detection).

**`scripts/release.sh X.Y.Z` automates the whole release routine** (version bump → merge → tag →
wait for assets → notes → Homebrew tap + mirror → winget mirror + PR). The playbook lives in
`.claude/skills/release/SKILL.md`; release notes are mandatory and must be grouped Markdown.

CI: `.github/workflows/dotnet-desktop.yml` (build + test), `release.yml`,
`snap.yml`. Coverage config in `codecov.yml`.

---

## 8. Specs & process (`openspec/`)

The single source of truth for progress — `PLAN.md`/`TASKS.md` were retired 2026-06-23.

- **`openspec/specs/`** — the living capability baseline, **30 capabilities**: `add-download`,
  `browser-download-interception`, `browser-extension`, `cli`, `dash-streams`, `download-status`,
  `downloads-list`, `extension-distribution`, `extension-media-details`,
  `extension-media-relevance`, `hls-download`, `link-refresh`, `link-variants`, `local-api`,
  `notch-overlay`, `notifications`, `ollama-model-download`, `platform-distribution`, `plugins`,
  `queues`, `request-context`, `resource-management`, `settings`, `speed-limit`, `system-tray`,
  `taskbar-progress`, `ui-navigation`, `ui-theme`, `website-offline-copy`, `window-chrome`.
- **`openspec/changes/`** — active work. Currently `hls-only-quality-picker`,
  `issue4-followups-batch` and `packaging-donate-batch`.
- **`openspec/changes/archive/`** — 40 completed changes, `YYYY-MM-DD-<name>`.

Flow: `/opsx:propose` → implement, ticking that change's `tasks.md` → `/opsx:sync` deltas into
`openspec/specs/` → `/opsx:archive`. Start every session with `openspec list`.

Repo-local skills live in `.claude/skills/` (`downloader-desktop`, `release`, `code-review`, `tdd`,
`triage`, `research`, `diagnosing-bugs`, `implement`, `improve-codebase-architecture`,
`codebase-design`, `grill-with-docs`, `to-issues`, `resolving-merge-conflicts`, the five
`openspec-*`), mirrored for other agents in `.agents/skills/` and `.github/skills/`.

---

## 9. Where to change what

| Task | Start here |
| --- | --- |
| Add/alter a download-lifecycle rule | `Services/DownloadManager.cs` — guard at the choke point, not in the VM or the button. |
| Change queue concurrency behavior | `DownloadManager`'s pump + `DownloadSettings.MaxConcurrentDownloads`, which is kept in lockstep with the **primary** queue's `MaxConcurrent`. |
| Add a setting | `Models/DownloadSettings.cs` (+ `ToConfiguration()` if it maps to the engine) → `ViewModels/SettingViewModel.cs` → `Views/SettingView.axaml`. |
| Add a page | `ViewModels/Navigation.cs` (`NavSection`) → new VM → new View → `MainViewModel.CurrentPage` switch → `MainWindow` toolbar. |
| Add UI text | `Assets/i18n/en.json` first, then the other 15 packs; bind `{i18n:Tr Key}`. |
| Support a new link type | A plugin `ILinkResolver` — not app code. Copy the GitHub plugin. |
| Add an automation endpoint | `Services/LocalApiService.cs` (+ `CliParser`/`CliRunner` for a verb), and `docs/local-api.md`. |
| Ship a new version | `scripts/release.sh X.Y.Z` (see the `release` skill). |

### Known rough edges (as documented)

- Avalonia 12 removed `ExtendClientAreaChromeHints` — don't use it.
- `DataGridTextColumn.Binding` needs `{ReflectionBinding …}` (compiled bindings resolve against the
  page VM, not the row item); template columns set `x:DataType` instead.
- DataGrid grouping was removed deliberately — it disables row virtualization.
- `IDownload.Filename` stays empty when no name is supplied; read the resolved name from
  `DownloadStartedEventArgs.FileName`.
- `Nullable` is enabled in `Directory.Build.props` but **overridden off** in the app csproj.
- The test project must mirror the app's `SelfContained=true` + a `RuntimeIdentifier`.
- `docs/plugins-hls-torrent-plan.md` is superseded, kept for historical context only.

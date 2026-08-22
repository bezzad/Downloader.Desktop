# CLAUDE.md — Downloader.Desktop

Cross-platform desktop GUI (Windows/Linux/macOS) for the [Downloader](https://github.com/bezzad/downloader) multipart download library. Status: **early dev — no production release yet**. A full **V1 redesign** is implemented on branch `feat/v1-redesign` (awaiting the author's interactive testing + merge).

## Product vision
- **Goal**: a GUI download manager exposing the `Downloader` engine's features (multipart, pause/resume, speed control, etc.) to **end users, not developers**.
- **Audience**: non-technical people on Windows / Linux / macOS. Must be **stable, simple, self-explanatory** — no exposed/complex config, sensible defaults.
- **Author owns the engine**: `bezzad` developed the `Downloader` library (https://github.com/bezzad/downloader); this app is the UI layer on top of it.
- **Design stance**: this is an **original design**, not a clone of any existing app. Internet Download Manager (IDM) is a useful mental benchmark for the *feature set* of a download manager, but the look, layout and UX here are our own.
- **Platform roadmap**: **Desktop first** (this repo), **mobile later** (Android/iOS — specific first platform TBD). Framework must keep a mobile path open.

## Decisions (settled with the author)
- **V1 / MVP scope** = **Core downloading + Queue & Scheduler**:
  - Core: add URL → pick folder → multipart download with **pause / resume / cancel**, live **progress + speed**, persistent list across restarts.
  - Plus: a **download queue** (cap concurrent downloads) and a **scheduler** (start/stop at set times).
  - Deferred to later: browser/clipboard URL capture, categories, site grabber, full IDM parity.
- **Tech stack** = **Avalonia + .NET — LOCKED** (final, do not re-litigate). Framework comparison (MAUI / Blazor Hybrid / others) was already done across prior sessions and this one; Avalonia chosen because:
  - Reuses the existing **.NET `Downloader`** engine directly.
  - **Keeps Linux desktop** AND offers a mobile path (iOS/Android/Browser) for the next phase. (.NET MAUI drops Linux; Kotlin/Compose drops the engine; Blazor Hybrid fragments mobile + weakens native OS integration.)
  - **Maps to the author's WPF skills** almost 1:1 (XAML, bindings, MVVM, styles, DataTemplates). Author also knows Blazor.
  - Native OS integration (tray, notifications, file pickers, "open folder", drag-drop) matters for a download manager and Avalonia does it natively.
- **Visual style** = **original modern-minimal design** (ours, not a clone of any app). Ocean-blue/teal accent on softly tinted neutrals, lively in both light & dark; clean rounded cards, file-type row icons, friendly empty states; aimed at non-developers. Keep it simple/understandable above all.
- **Mobile**: Avalonia must keep supporting it; first mobile platform decided later. Avoid desktop-only architectural lock-in.

## Working conventions (how I operate on this repo)
- **`CLAUDE.md` is the source of truth** — update it every time decisions, conventions, scope, or structure change. Don't re-litigate settled decisions (esp. the Avalonia lock).
- **Describe before building**: state what I'll add (files/structure/behavior) and why, before/as I write it.
- **Small, reviewable increments** following the roadmap below; get something working early so the author can give feedback.
- **UI = mockup first**: show a layout/structure proposal and let the author pick before committing detailed work.
- **Commit policy — superseded by "Workflow & progress tracking" below**: that section's "commit frequently and push to `develop`" is the current standing rule for routine work (code steps, OpenSpec change artifacts, skill-file notes). The old default of waiting for explicit per-commit approval no longer applies on `develop`; it still applies to anything outside that scope (e.g. force-pushes, branch/history changes, releases/tags).
- The author steers and gives feedback; fold it in and keep this file current.

### Standing operating rules (the author asked for these — always apply, never wait to be told again)
- **Ask all questions BEFORE starting.** The author typically hands over a batch of tasks and then leaves the machine. Front-load every clarifying question (ambiguous scope, design choices, mappings, trade-offs) in one go *before* writing any code, using `AskUserQuestion`, so the work can run unattended afterward. Don't start, hit an ambiguity, and stall waiting for an answer that won't come.
- **Use the available skills.** Invoke the repo's `downloader-desktop` skill first (build/run/test + gotchas), and use any other relevant available skill rather than re-deriving from scratch. Skills are the first source of truth for how to do things here.
- **Cache recurring patterns into the skill automatically.** When a non-obvious pattern, gotcha, or decision comes up that a future session would otherwise re-derive, append a concise note to the relevant skill file (usually `.claude/skills/downloader-desktop/SKILL.md`) and commit it on `develop` — no confirmation needed. Goal: steadily fewer tokens per session.
- **Minimal, targeted changes — don't disturb working scenarios.** Change only what the task needs. Do not refactor, "improve", or alter unrelated code paths that already work; touching them risks new bugs. When a recent change looks odd, assume it may have had a reason — review its history before overriding it. (Reinforces the Clean Code/KISS rule below.)
- **Tests passing = done; then push.** A task is complete when the build is clean and `dotnet test` is green (add/adjust tests for the change). When everything for the session is done and green, commit and push to `develop`. If a view's UI changed, also refresh screenshots (see "Workflow & progress tracking").
- **After every `/opsx:apply` session (all tasks or a batch), build everything and run all tests before calling it done.** Run `dotnet build Downloader.Desktop.sln` (from `src/`) for the app, and for the browser extension load it as an unpacked extension (there's no bundler/build step, it's plain JS) and run its tests: `node --test src/browser-extension/common.test.js` (unit) and the Playwright suite in `src/browser-extension/e2e/` (`npm test` there, after one-time `npm install` + `npx playwright install chromium`) for real-browser UI checks. Then run `dotnet test` (unit + headless UI tests) for the app. Only report the apply session complete once the app build is clean and all three test suites are green — this is a standing step, not something to be asked for per task.

## Stack
- **.NET 10** (`net10.0`); macOS build target switches to `net10.0-macos` when `IsMacBuild=true` (requires the `macos` workload + Xcode; only used for the native `.app` bundle, not the CI release which builds plain `net10.0`).
- **Avalonia UI 12** with **ReactiveUI** (MVVM), Fluent theme, Inter font, Skia, DataGrid.
- **Downloader 5.9.5** NuGet package (the core download engine — not in this repo).
- DI via `Microsoft.Extensions.DependencyInjection`; logging via `Microsoft.Extensions.Logging`.
- macOS `.app` bundling via `Dotnet.Bundle`.
- `Nullable` is **disabled** in the app csproj (enabled in `Directory.Build.props` but overridden).

## Layout (`src/`)

> **Full codebase map: [`docs/codebase-index.md`](docs/codebase-index.md)** — every project, service,
> view model, plugin, test folder, packaging channel and spec, with "where to change what". Read it
> instead of re-exploring the tree; keep it current when structure changes.

- `Downloader.Desktop.sln` — solution.
- `Directory.Build.props` — shared props.
- `Downloader.Desktop.Plugins.Abstractions/` — the **plugin SDK** (interfaces + POCOs only): `IDownloaderPlugin`, `ILinkResolver`, `ITransferProvider`/`ITransfer`, `IPostProcessor`, `IPostDownloadAction` (user-initiated action on a completed download, e.g. "Add to Ollama"). External plugins reference this.
- `Downloader.Desktop.Plugins/` — the first-party plugins, in **two tiers** (all first-party plugin source now lives in THIS repo — the former separate `bezzad/Downloader.Plugins` repo was consolidated in; it is deleted only after the author confirms this works):
  - **BUILT-IN** (bundled, disable-only, not removable, updates with the app): `Downloader.Desktop.Plugins.GitHub` (GitHub Releases resolver; the former `samples/Downloader.Desktop.SamplePlugin`, same id `com.bezzad.github-releases`) and `Downloader.Desktop.Plugins.Ollama` (`gemma3:12b` / ollama.com links → model blob download + "Add to Ollama" install; id `com.bezzad.ollama-models`). Staged into the app output's `plugins/` folder at build/publish by the app csproj's `StageBundledPlugins` target — an **explicit per-plugin allow-list**, NOT a wildcard, so optional plugins in the same folder are never bundled.
  - **OPTIONAL / catalog tier** (NOT bundled, NOT referenced by the app, absent on a fresh install; installed on demand from Settings → Plugins): `Downloader.Desktop.Plugins.Hls` (HLS/`.m3u8` segment download + ffmpeg remux, quality picker from master playlists; id `com.bezzad.hls`) and `Downloader.Desktop.Plugins.Website` (save a page/site as an offline-browsable `.zip`; id `com.bezzad.website-zip` — offers an "Offline copy (.zip)" Add-dialog variant on `text/html` links via the `websitezip:` scheme; its crawl runs through the app's `ITransfer` path, `DownloadManager.Transfers.cs`; requires app ≥ 2.1.0, enforced by the catalog's `minAppVersion`). They're in the solution for build/test only. At release time `scripts/build-plugins.sh` (run by `release.yml`) zips it + generates `plugins-catalog.json` (from `packaging/plugins/optional-plugins.json` + the built version/sha256) and attaches both to the same `vX.Y.Z` release. The app fetches that catalog (`Services/PluginCatalogService`), shows uninstalled ones under "More plugins", and on **Add** downloads → **verifies sha256 before load** (`PluginManager.InstallFromZipAsync`) → installs into `PluginsRoot` as a normal removable plugin. Update checks compare installed vs catalog version and prompt (never auto). Isolation is guarded by `PluginIsolationTests` + a `release.yml` publish grep. Its own version lives in its csproj `<Version>` (single source; `HlsPlugin.Version` derives from the assembly).
  - The `samples/` folder no longer exists. Loaded at runtime by `Services/PluginManager` (collectible `AssemblyLoadContext`). Docs: `docs/plugins-architecture.md` + `docs/writing-plugins.md`. **UI nav model:** no left rail and no page dialogs — the toolbar (bulk actions + page nav) lives in `MainWindow` and the central `ContentControl` swaps `MainViewModel.CurrentPage` between Downloads/Queues/Scheduler/Settings (an icon-only list button returns to Downloads); only Add-link (and Details/About) remain modal windows (`PageDialogView` was deleted 2026-07-10); Plugins live in a collapsible Settings section.
- `Downloader.Desktop/`
  - `Program.cs` — Avalonia entrypoint (`BuildAvaloniaApp`, classic desktop lifetime).
  - `App.axaml(.cs)` — app bootstrap, **DI registration in `ConfigureServices()`**, platform guard (desktop-only), shutdown-save hook (`DesktopOnShutdownRequested`, currently commented out).
  - `Models/` — `Config.cs` (persisted settings + theme), `DownloadItem.cs` (persisted download record).
  - `Services/` — `IFileService`/`FileService.cs` (JSON load/save of `Config`), `DialogHelper.cs` (modal dialogs + folder picker).
  - `ViewModels/` — `ViewModelBase` (has `View`), `MainViewModel`, `DownloadsViewModel`, `DownloadItemViewModel`, `AddDownloadItemViewModel`, `SettingViewModel`.
  - `Views/` — matching `.axaml(.cs)`: `MainWindow`, `DownloadsView`, `AddDownloadItemView`, `SettingView`.
  - `Assets/` — icons (`.ico`/`.icns`/`.png`), `Info.plist`, `config.json`, `Icons.axaml`.
- `Downloader.Desktop.Tests/` — the **single** test project (xUnit v3 + `Avalonia.Headless.XUnit`), organized into folders with matching namespaces (no loose `.cs` at the root): `Unit/` (pure logic), `Integration/` (loopback/engine/local-API-CLI/e2e), `UI/` (Avalonia headless + `CaptureScreenshots`), `Plugins/` (all plugin tests) with `Plugins/Hls/` for the HLS plugin (folded in from the former separate `Downloader.Desktop.Plugins.Hls.Tests` project — now deleted; runs in CI for the first time), and `TestSupport/` (`TestAppBuilder`/assembly attrs, kept at the root namespace). New tests go in the folder that fits + its sub-namespace.

## Architecture notes
- **MVVM**: Views bind to ViewModels (compiled bindings on by default). `MainViewModel` is the root, resolved via DI and set as `MainWindow.DataContext` in `App.OnFrameworkInitializationCompleted`.
- Only `IFileService` (singleton) and `MainViewModel` (transient) are registered in DI; other VMs are `new`-ed up directly.
- **Config persistence**: `FileService` serializes `Config` to `%AppData%/Downloader/config.json` (`Environment.SpecialFolder.ApplicationData`). Missing file → `Config.New()` defaults (4 chunks, Desktop save path, light theme).
- **Dialogs**: `DialogHelper.ShowDialog<TView,TVm,TResult>` shows a modal and returns the result the view is closed with. `AddDownloadItemViewModel.StartDownloadAsync` builds the download via `DownloadBuilder.New()...Build()`, closes the dialog returning the `IDownload`, then starts it.
- **Theme**: `Config.IsThemeDarkMode` ⇄ `ThemeVariant`; applied via `Application.Current.RequestedThemeVariant`.
- **Stubs / unfinished**: `StopAll`, `StartAll`, `ClearAllStoppedItems` (MainViewModel), `SelectFilesAsync`, save-on-shutdown are not implemented yet. `DownloadItemViewModel.Status` percent math is integer-division buggy (`Downloaded/Size*100` → always 0).

## Build & run
```bash
# from src/
dotnet build Downloader.Desktop.sln
dotnet run --project Downloader.Desktop/Downloader.Desktop.csproj
```
macOS `.app` publish + code signing steps are in the root `README.md`.

## Related repos (siblings on disk, not referenced via project ref)
- `../Downloader` — the core download library (separate git repo). The desktop app consumes it as the `Downloader` NuGet package.

## Roadmap & next steps (toward V1)
Rough order to turn the current skeleton into the MVP above:
1. ✅ **Wire the engine into the list** (DONE, Stage 1): `Services/DownloadManager` (DI singleton, `IDownloadManager`) owns the master `ObservableCollection<DownloadItemViewModel>`, builds `IDownload` via `DownloadBuilder`, and relays engine events (`DownloadProgressChanged`/`DownloadFileCompleted`/`DownloadStarted`) to the row VM on the UI thread (`Dispatcher.UIThread`). `DownloadItemViewModel` rewritten with live `Progress`/`SpeedText`/`SizeText`/`StatusText` + per-item commands; integer-division `Status` bug fixed. `AddDownloadItemViewModel` now returns a `DownloadItem` descriptor and the manager builds/starts it.
2. ✅ **Per-item actions** (DONE, Stage 1): pause / resume / cancel / retry / remove / open-folder, contextual in the `DownloadsView` grid via `CanPause`/`CanResume`/`CanRetry`/`IsActive`/`IsCompleted`.
3. ✅ **Bulk actions** (DONE, Stage 1): `StartAll`/`StopAll`/`ClearCompleted` implemented on the manager and wired to `MainViewModel`.
4. ✅ **Full V1 redesign** (DONE, Stages 2–7 on `feat/v1-redesign`):
   - **Settings model** (`Models/DownloadSettings.cs`) mirrors the whole engine `DownloadConfiguration` (+ common request opts) as a JSON-persistable POCO with `ToConfiguration()`. `Config` now holds `Settings`/`Queues`/`Schedules`/`Downloads`.
   - **Filename auto-resolution**: Add dialog takes URL + folder (name optional); manager passes only URL+folder to the engine and reads the resolved name from `DownloadStartedEventArgs.FileName` (note: `IDownload.Filename` stays empty when no name is supplied — must use the event arg).
   - **Main window redesign**: top bar (paste link + Add + search), left nav rail (STATUS filters w/ count pills + MANAGE: Queues/Scheduler/Settings), central `ContentControl` swapping pages via `DataTemplates`, bottom status bar (speed + counts + bulk). Modernized `App.axaml` styles (theme-aware nav/icon/card, blue accent) — replaced the old white-forced button styles. Standard window chrome (dropped the acrylic custom titlebar for cross-platform reliability).
   - **Settings page**: scrollable, Basic card + collapsible Advanced + Network/Request, every option bound to `DownloadSettings`.
   - **Persistence/resume**: config saved on shutdown via the resolved `MainViewModel`; resume relies on engine `EnableAutoResumeDownload` (restart a download to the same path → continues). `FileService` load is exception-tolerant.
   - **Queues**: concurrency engine in `DownloadManager` (enqueue, pump next on completion, start/pause queue, cap, add/remove). `QueuesViewModel`/`QueuesView`.
   - **Scheduler**: `DispatcherTimer` (30s) evaluating schedules → start/stop target queue in a daily window (+run-once). `SchedulerViewModel`/`SchedulerView`.
   - *DataGrid note*: `DataGridTextColumn.Binding` must use `{ReflectionBinding ...}` (compiled bindings resolve against the page VM, not the row item); template columns set `x:DataType` instead.
5. ✅ **Polish rounds + tests** (DONE): friendly error root-cause on failure; periodic autosave (+ save on list change) and Running→Paused normalized at load; **ocean-blue/teal** light+dark palette (`App.axaml`); file-type row icons (`Converters/FileKindToIconConverter` + `DownloadItemViewModel.FileKind`); details window shows a **segmented per-connection** strip + live speed-limit; collapsible sidebar (`MainViewModel.IsSidebarExpanded/SidebarWidth`); Network settings nested under Advanced; default `FileExistPolicy=IgnoreDownload`; multi-URL Add dialog; "Fetching name…" placeholder + failed-row warning color; close-deadlock fixed (`ConfigureAwait(false)` on save); PNG window icon.
   - **Tests** (`src/Downloader.Desktop.Tests`, xUnit **v3** + `Avalonia.Headless.XUnit`): 30 unit/headless tests, all green via `dotnet test`. Screenshots are generated by a **gated** `[AvaloniaFact]` (`CaptureScreenshots`, env `DLDESKTOP_CAPTURE=1`) that renders real frames to `docs/screenshots/`.
   - **Test-project gotchas**: app sets `SelfContained=true`, so the test project must also set `SelfContained=true` + `RuntimeIdentifier=$(NETCoreSdkPortableRuntimeIdentifier)`. `Avalonia.Headless.XUnit 12` pulls **xunit.v3** (don't mix with v2). `[assembly: AvaloniaTestApplication]` lives in namespace `Avalonia.Headless` (not `.XUnit`). For real screenshot pixels, host the real `App` with `.UseSkia().UseHeadless(new(){ UseHeadlessDrawing = false })`.
   - *Remaining (post-V1):* browser/clipboard capture, categories, per-download scheduling UI, request certificates/cookies/credentials in Settings, packaging/installers, and **m3u8/HLS + YouTube** downloads (needs FFmpeg + YoutubeExplode — deferred by author; large scope).
6. ✅ **Round 6 — engine integration + UX fixes** (DONE):
   - **Engine via `DownloadService` (not `DownloadBuilder`)**: enables real **mirror fallbacks** and **engine logging**. Model now stores `DownloadItem.Urls` (`List<string>`, first = primary, rest = mirrors) replacing `Url`+`Mirrors`; old configs migrate via legacy `Url`/`Mirrors` JSON setters. `DownloadItemViewModel.Download` is now `DownloadService`.
   - **Logging bridge**: `AppLog.Factory` (`ILoggerFactory`) handed to `new DownloadService(cfg, AppLog.Factory)`, so the engine's logs land in the app log file when logging is enabled (default off).
   - **Notifications**: `Services/NotificationService` — native `notify-send`/`osascript`, in-app Avalonia toast fallback (Windows); fires on complete/fail, gated by `DownloadSettings.EnableNotifications` (default on).
   - **Perf**: `Start` resolves redirects + does engine setup off the UI thread; `DownloadManager.Batch()` coalesces bulk-action list refreshes (fixes "select all → Start" freeze).
   - **Defaults**: `HttpClientTimeout`=10 s, `MaximumMemoryBufferBytes`=2 GB.
   - **Persistence**: `FileService` atomic write (temp+move) + `SemaphoreSlim`; `MainViewModel.SaveSoon()` debounced save fired on any `SettingViewModel` change (near-instant settings persistence).
   - **Versioning**: auto in csproj — `VersionPrefix` (major.minor) by hand, build/revision derived from UTC build time; shown in Settings → About.
   - **UI**: global numeric-stepper restyle (#1, hand cursor on arrows), DataGrid full-row select (no cell border), centered value columns, batch grouping (`DataGridCollectionView.GroupDescriptions` on `Group`), header-double-click no longer opens details, distinct "All downloads" icon (`AppsListRegular`), Network sub-section card, details window merged progress + add/remove **mirror editor**, folder-picker cancel returns `null` (fixes `UriFormatException`).
   - **Engine API quick-ref** is now cached in the project skill (`.claude/skills/downloader-desktop/SKILL.md`) to cut re-derivation each session.
   - **Custom window chrome** (reverses the earlier "standard chrome" call, at the author's request): reusable `Views/TitleBar` (app icon + title + window buttons) drawn inside the client area; MainWindow + both dialogs set `ExtendClientAreaToDecorationsHint="True"` + `ExtendClientAreaTitleBarHeightHint="-1"` (note: `ExtendClientAreaChromeHints` was **removed in Avalonia 12** — don't use it). `TitleBar` drags via `BeginMoveDrag`, toggles maximize on double-tap, and finds its host with `TopLevel.GetTopLevel(this)`. Dialogs pass `ShowMinMax="False"`.
   - **Notifications UX**: turning the Settings toggle on fires a sample notification immediately so the user can confirm it works.
7. ✅ **Round 7 — i18n + more UX** (DONE):
   - **Localization**: `Services/Localizer` + `Markup/TrExtension` (`{i18n:Tr Key}`) + `TrConverter`; JSON packs in `Assets/i18n/` for **en, fa, es, fr, ar, eo**. Language picker in Settings (`DownloadSettings.Language`), **RTL** mirroring for fa/ar via `Localizer.FlowDirection` bound on each Window. Live switch works via a `Localizer.Tick` property (indexer-change notifications were unreliable — see SKILL.md). VM strings localized + auto-refresh on language change (`DownloadItemViewModel.Detach()` prevents leaks). 8 new headless i18n tests (47 total).
   - **Pause fix**: progress events are ignored once a row is paused/stopped so the bar keeps its last fill; StatusText shows `"62% · Paused"`.
   - **Email logs**: opens a Gmail compose URL in the default browser (not `mailto:`), copies the log path to the clipboard, and includes an auto **diagnostics block** (app version, OS/arch, runtime, theme, key settings).
   - **Settings**: "Reset to defaults" button; compact numeric steppers with a clean single outer border.
8. ✅ **Round 8 — reliability, perf, packaging** (DONE):
   - **Critical**: `HttpClientTimeout` default fixed 10 s → **100 s** (10 s was cancelling chunk reads → "Operation Cancelled" failures after ~1 min).
   - **Status**: a cancel that arrives while still Running is now mapped to **Failed** (only user pause/stop stays Stopped/Paused) — consistent statuses.
   - **Pause**: progress events ignored when not Running, so a paused row keeps its fill + "% · Paused".
   - **Perf**: removed DataGrid grouping (it disabled row virtualization → jank past ~10 rows).
   - **Queued names**: `UrlResolver.ResolveFileNameAsync` + VM-only `PreviewName` show a file name before a queue-capped item starts (without forcing it on the engine).
   - **Multi-URL top box**: `AcceptsReturn` + Enter handler (single-line boxes strip pasted newlines).
   - **DataGrid**: per-cell focus/current border removed via `:current/:focus /template/ Border`; full-row selection.
   - **Details**: shows the saved-to **path** with an open-folder button; **open-folder selects the file** (`RevealInFolder`, cross-platform).
   - **Tests**: end-to-end `IntegrationTests` downloads a 256 KB file from a loopback server through the real engine and asserts bytes (48 tests total).
   - **Publish**: `.github/workflows/{ci,release}.yml` + `scripts/publish.sh` — self-contained single-file builds for win/linux/macOS attached to each GitHub Release (no end-user dependencies).
9. ✅ **Round 9 — details progress accuracy, queue cap, UI-thread perf** (DONE):
   - **Details segmented bar**: fixed "7 of 8 connections" + "not full at 100%" — `DownloadDetailsViewModel` seeds parts from `Package.Chunks`, throttles per-connection (not globally), and snaps all segments to 100% on completion. It also **attaches to the engine handle when it arrives late** (the manager assigns `vm.Download` only after off-thread redirect resolution, so a dialog opened right after Start used to show nothing until reopened) and **reconciles parts on each overall-progress tick** so connections the engine activates after the dialog opened still appear. `DownloadItemViewModel.Download` is now a notifying property.
   - **Queue concurrency cap enforced**: bulk/`StartAll`/`Resume`/`Retry` previously called `Start` directly and ignored `MaxConcurrent` (select 10 with cap 2 → all 10 ran). All start paths now re-queue the item and funnel through `PumpQueue`, which starts/resumes only while `running < cap`. `Start` is the uncapped primitive used only by the pump. **Also**: the Settings "Max concurrent downloads" (`DownloadSettings.MaxConcurrentDownloads`) only *seeded* new queues, so changing it never actually limited running downloads — the real cause of the reported bug. It's now kept in lockstep with the **primary queue's** `MaxConcurrent` (Settings setter writes through + pumps; `Initialize` re-syncs on load for stale configs; the Queues page mirrors default-queue edits back into the setting). Extra queues keep their own caps.
   - **UI-thread perf**: engine `DownloadProgressChanged` no longer posts per event. Handlers `StageProgress(...)` on the row (any thread, no UI); a single shared `DispatcherTimer` (`EnsureUiPump`, 250 ms) flushes all rows and fires stats once per tick, self-stopping when idle. Main-thread cost is now bounded regardless of active download/connection count.
   - **Esc closes the details dialog** (`OnKeyDown` override — no native chrome to do it).
   - **Tests**: +2 (queue-cap enforcement, staged-progress flush semantics) → **50 total**, all green.
10. ✅ **Round 10 — state-transition guards** (DONE):
   - **Bugs**: (a) stopping a *completed* item flipped it to Stopped; (b) a finished download could restart from 0% ("99% → 100% → begins again from 0"); (c) "select all → Stop" stopped the running rows but the pump immediately auto-started the queued rows ("3 stop, 3 start"). Cause: bulk actions (`StopSelected`→`Cancel`, `StartSelected`→`Resume`) apply to *every* selected row regardless of state, while `DownloadManager` changed state unconditionally (per-row buttons gate via `IsVisible`, but bulk bypasses that). For (c): leaving queued rows untouched on Stop meant cancelling the running rows fired `DownloadFileCompleted`→`TryStartNextInQueue`, and the pump refilled the freed slots from the still-queued rows.
   - **Fix** — guard transitions at the manager choke point (covers buttons, bulk, scheduler, pump): `Pause` (Running only); `Cancel`/"Stop" acts on Running/Paused **and queued (Created/None)** → all Stopped (so the queue actually stops; the synchronous `StopSelected` loop finishes before any completion callback, so the pump finds no queued rows to start), no-op only for Completed/Failed/already-Stopped; `Resume`/`Start` skip Running/Completed; `Retry` only Failed/Stopped. Also made the **Queued** badge a steel-blue distinct from the gray **Stopped** badge (`StatusToBrushConverter`). +2 regression tests (`Completed_item_ignores_stop_resume_and_retry`, `Stopping_all_stops_running_and_queued_items`) → **53 total**, all green. (Note: a server lacking HTTP range/resume can still make the *engine* restart a chunk near the end — separate from these UI guards.)

11. ✅ **Round 11 — tray, startup, auto-update, UX polish** (DONE, uncommitted for review):
   - **System tray** (`Services/TrayService`): close-to-tray (downloads keep running), menu Open / Disable-notifications / Quit; `DownloadSettings.EnableSystemTray` toggle. **Run-at-startup** (`Services/StartupService`, `RunAtStartup`): Win `reg.exe` Run key, Linux XDG autostart, macOS LaunchAgent; launches `--minimized`; coupled to the tray toggle. **Auto-update** (`Services/UpdateService` + `UpdateFlow`, `AutoUpdate` + "Check for updates" button): GitHub `releases/latest` → in-app actionable toast → background download via the engine → detached script swaps + relaunches. Wired in `MainViewModel.SetupAppShell()`.
   - **UX fixes**: friendlier timeout/cancel messages (no bare "Operation cancelled"); native notification icons (freedesktop `dialog-error`/`dialog-information`); name-fetch perf (`UrlResolver` URL fast-path + concurrency semaphore + 8s timeout so rows never hang on "Fetching name…"); search/link icons in the top textboxes; trimmed downloads-grid padding + removed its border; nav count-pill spacing + readable selected contrast; **16px rounded corners** on all windows (transparent window + clipped rounded root border); Linux taskbar icon via `X11PlatformOptions.WmClass`.
   - **Install** (`scripts/install.sh` curl|bash, `Casks/downloader.rb`, `packaging/winget/*` templates) + README quick-install commands. winget/brew listings still need the author to publish (winget-pkgs PR / brew tap).
   - **Tests**: +10 (update version logic, queue-stop) → **63 total**, all green. App launches clean. See SKILL.md for the per-pattern gotchas (esp. Avalonia tray/transparency and the AssemblyVersion-vs-tag compare).

12. ✅ **Round 12 — dialog/visual polish + docs** (DONE, uncommitted for review):
   - **Dialog transparency bug**: the rounded root border used `ThemeBackgroundColor` (undefined in Fluent here) → dialogs were see-through. Fixed to `SystemRegionColor` (opaque). Window corners **16px → 10px** everywhere.
   - **Notifications**: completed download now shows a green success icon (`emblem-default`) not the blue info icon. **Details strip**: each connection's fragment renders in its own stable palette color (`ChunkProgressViewModel.Brush` by index, bound to the segment `ProgressBar.Foreground`).
   - **Docs**: hand-authored `docs/banner.svg` hero banner (GitHub renders SVG); README is now end-user focused with **theme-aware screenshots** (`<picture>` + `prefers-color-scheme`, added a `settings-light` capture) and a one-block "Build from source"; all developer/publish/release/macOS-bundle detail moved to new **`CONTRIBUTING.md`**.
   - **Linux exec icon**: a raw ELF can't carry a file-manager icon (OS limitation) — the taskbar icon comes from `Window.Icon` + `X11PlatformOptions.WmClass`, and the file/menu icon from the `.desktop` installed by `scripts/install.sh`.
   - **Tests**: still **63**, all green; screenshots regenerated. See SKILL.md for the per-pattern gotchas.

13. ✅ **Round 13 — small fixes** (DONE, uncommitted for review):
   - **Open-folder** now reveals/selects the file for **in-progress** rows too (reveals `<name>.download` when the final file isn't there yet; completed rows already worked).
   - **Details "Connections"** grid font reduced to 11 to match the section above; **fragment palette** changed to a cohesive **blue→teal** range (no reds/oranges).
   - **winget**: added `Moniker: downloader` so `winget install downloader` will work once the package is actually published to winget-pkgs (identifier must stay `bezzad.Downloader`); README updated.
   - #2 (notification green tick) was already correct on GNOME (`emblem-default`) — no change needed; reverted an unnecessary bundled-icon attempt.
   - **Tests**: still **63**, all green.

14. ✅ **Round 14 — fix non-working install commands** (DONE):
   - `winget install downloader` and `brew tap bezzad/tap` were both broken: the winget package was never submitted to `microsoft/winget-pkgs` and `bezzad/homebrew-tap` doesn't exist on GitHub yet, so both 404/fail on a clean machine. README's old footnote undersold this ("until a listing is live for a given version") when really *no version* has ever been published.
   - **Fixed README**: Quick install now only shows the Linux script as a working one-liner, points Windows/macOS to Manual download, and states plainly that winget/Homebrew aren't published yet (with why, and where the ready manifests live).
   - **Fixed the templates themselves** so they're publish-ready: `Casks/downloader.rb` and `packaging/winget/*.yaml` had `1.0.0`/`REPLACE_WITH_SHA256_*` placeholders never updated for any real release — bumped to `1.1.0` and filled in real sha256 for all 3 assets (computed from the actual `v1.1.0` GitHub release).
   - **Still not actually published** (needs the author's explicit go-ahead, see below): creating `bezzad/homebrew-tap` and submitting the `wingetcreate`/manual PR to `microsoft/winget-pkgs` are both externally-visible, third-party actions — not done automatically.

## Design / privacy note
This is an **original design**. Do not reference or name other download-manager apps in the repo or docs — there is no clone. IDM is only an internal feature-set benchmark.
4. **Persistence**: re-enable save-on-shutdown (`DesktopOnShutdownRequested`) and resume incomplete downloads on startup using the engine's resume support.
5. **Queue**: cap concurrent active downloads (configurable), auto-start next when a slot frees.
6. **Scheduler**: start/stop downloads at configured times.
7. **UX polish**: ab-style modern UI, light/dark toggle (wiring already exists via `Config.ThemeMode`), empty states, simple Settings (only essential options surfaced).
8. **Packaging**: per-OS installers; macOS `.app` + signing steps already drafted in `README.md`.

Keep this list current as items land.

## Conventions
- Git user: `bezzad`. Main branch: `main`.
- C#: `LangVersion=latest`, file-scoped namespaces, `Avalonia`/`ReactiveUI` idioms (`RaiseAndSetIfChanged`, `ReactiveCommand.CreateFromTask`).
- **Code style — Clean Code, KISS, as simple as possible**: smallest change that solves the actual problem, no speculative abstractions/layers/config knobs, no dead code, prefer readability over cleverness. Standing rule, applies to every task without being repeated.
- **Plugin versions — bump on every plugin code change (standing rule).** Whenever a plugin's source changes (`src/Downloader.Desktop.Plugins/*`), bump that plugin's csproj `<Version>` (semver: fixes = patch, behavior/features = minor) in the same session as the change. The catalog update check compares installed vs catalog version — a stale version means users never receive the fix. The csproj `<Version>` is the single source (runtime `Version` derives from the assembly).
- **Logging — ALWAYS use the standard `Microsoft.Extensions.Logging.ILogger`** (`LogInformation`/`LogWarning`/`LogError`), never a custom `Log(string)` API or `Console.WriteLine`. This is the .NET standard and what the `Downloader` engine, `Downloader.Desktop`, and the plugin SDK all use, so everything flows into one log. The app bridges `ILogger` → the app log file via `AppLog.Factory` (`ILoggerFactory`); pass that factory to anything that takes one (e.g. `new DownloadService(cfg, AppLog.Factory)`), and the plugin SDK exposes `IPluginContext.Logger` (an `ILogger`). Standing rule.
- Keep this file updated when structure changes to minimize re-exploration.

## Workflow & progress tracking
These rules are permanent and apply to every conversation/task in this repo — do not wait to be told again.
- **Before starting any task here, invoke the repo's `downloader-desktop` skill first** (build/run/test commands + known gotchas live there — don't re-derive them).
- Do ALL work directly on `develop`. Never create feature branches.
- Commit frequently — one commit per logical step, with clear messages — and push to `develop` so any machine can pull the latest state.
- If work is unfinished at the end of a session, commit the WIP to `develop` anyway, using a `wip:` message prefix, so nothing is stranded on one machine.
- **Progress tracking lives in OpenSpec, not `PLAN.md`/`TASKS.md`** (those root files were retired 2026-06-23 — the OpenSpec change/archive system is now the single source of truth). Use the `/opsx:*` skills:
  - For any non-trivial batch, open a change with `/opsx:propose` (creates `openspec/changes/<name>/` with proposal/design/specs/tasks). Mark `- [x]` in that change's `tasks.md` as steps land; commit the change artifacts together with the code they describe, on `develop`.
  - When the work is done and green, `/opsx:sync` the delta specs into `openspec/specs/` (the living capability baseline), then `/opsx:archive` the change → it moves to `openspec/changes/archive/YYYY-MM-DD-<name>/`.
  - If a task fails or is abandoned, leave its `tasks.md` box unchecked and note the reason in the change's `proposal.md`/`design.md` before ending the session; commit and push so the next machine/AI session learns the true last state.
  - At the START of every session, run `openspec list` (active changes) and read any in-progress change's artifacts to continue from there. Never rely on in-session memory surviving across machines — if it matters, it must be in an OpenSpec change/spec and committed.
  - Small one-off fixes that don't warrant a full change can still go straight to `develop` with a clear commit; reserve OpenSpec changes for multi-step or spec-affecting work.
- **Refresh view screenshots when the UI changes (standing routine).** At the END of all tasks in a session, if any task changed a view's UI (a `Views/*.axaml`, `App.axaml` styles/theme, icons, or anything that alters how a page looks), regenerate the `docs/screenshots/` images and commit them on `develop`:
  - Run the gated capture test: `DLDESKTOP_CAPTURE=1 dotnet test Downloader.Desktop.Tests/Downloader.Desktop.Tests.csproj --filter FullyQualifiedName~CaptureScreenshots` (from `src/`).
  - **Verify the regenerated PNGs by viewing them** (don't commit blind) — confirm the change actually shows and nothing regressed. If a new/changed control sits below the fold, add a capture that scrolls to it (see how `CaptureScreenshots` does the settings-accent shot) so the change is actually visible.
  - The captures are deterministic, so an unchanged UI re-renders byte-identical (no diff = nothing to commit). Commit only what changed.
  - These screenshots feed the README/docs and the Snap Store listing, so keeping them current is part of "done" — not optional.

## Release routine (publishing a new version)
These steps are **standing, pre-authorized** — when the author asks to publish/release a new version
`vX.Y.Z` (even just "go next version X.Y.0"), do ALL of the following without asking again.

**The routine is packaged as the `release` skill (`.claude/skills/release/SKILL.md`) — invoke that
first**; it carries the full playbook (notes format, unattended/background pattern, per-channel
verification checklist, gotchas) so any AI session/model can run a release end-to-end.

**`scripts/release.sh` automates this whole routine** (version bump → merge → tag → wait for assets →
release notes → Homebrew tap + mirror → winget mirror + PR). Prefer running it:
`./scripts/release.sh X.Y.Z` (prompts for release notes; pass `--notes-file notes.md` to supply them
non-interactively). The manual steps below are the fallback / what the script does.

1. Bump the version, merge `develop` → `main`, tag `vX.Y.Z`, and push the tag. `.github/workflows/release.yml`
   then builds win/linux/macOS×2 (`Downloader-<rid>.tar.gz`) and attaches them to the GitHub Release. Wait
   for that run to finish so the macOS assets exist.
2. **Release notes are MANDATORY (high priority) — never ship a noteless release.** Every version must say
   what changed for end users. `release.sh` captures a human "Highlights" block up front and, once the
   release exists, sets the body (highlights + GitHub's auto-generated "What's Changed") via
   `gh release edit "vX.Y.Z" --notes-file …`. As a safety net, a single post-build `notes` job in
   `release.yml` fills GitHub's auto "What's Changed" **only if the body is still empty** (so it never
   clobbers curated notes) — even a bare tag push then gets a changelog. (Do NOT put
   `generate_release_notes: true` on the matrix `action-gh-release` steps — 4 concurrent creates race into
   `tag_name already_exists` and an asset upload fails; this bit v1.4.0's osx-x64.) If you ever release by
   hand, write the notes — a release with an empty body is not "done".
   **FORMAT — notes MUST be GitHub-flavored Markdown, pretty and human-friendly (NOT plain text):**
   - Start with a one-line summary sentence, then short grouped sections with emoji headers, e.g.
     `### ✨ New` / `### 🐛 Fixes` / `### 🔧 Under the hood`, each a few concise bullets.
   - **Simple and summary — keep it short** (a handful of bullets, end-user wording, no commit hashes /
     internal jargon). Reference good examples already on GitHub: **v1.0.0 / v1.1.0 / v1.2.0**.
   - End with a thin divider + an install hint line if useful. The auto-generated "What's Changed" list
     may follow under its own heading, but the curated Markdown highlights come first.
3. **Always update the Homebrew tap — this is a mandatory part of every release, not a separate request.**
   In `bezzad/homebrew-tap` → `Casks/downloader.rb`, set `version "X.Y.Z"` and the two `sha256` (arm64 then
   intel) from the released macOS archives, commit, and push to the tap repo. Then sync the in-repo mirror
   `Casks/downloader.rb` on `develop` to match.

   ```bash
   VER=X.Y.Z
   TAP=$(brew --repository bezzad/tap)
   ARM=$(curl -fsSL "https://github.com/bezzad/Downloader.Desktop/releases/download/v$VER/Downloader-osx-arm64.tar.gz" | shasum -a 256 | awk '{print $1}')
   X64=$(curl -fsSL "https://github.com/bezzad/Downloader.Desktop/releases/download/v$VER/Downloader-osx-x64.tar.gz"   | shasum -a 256 | awk '{print $1}')
   # edit Casks/downloader.rb: version "$VER", on_arm sha256 "$ARM", on_intel sha256 "$X64"; commit + push the tap
   ```
4. **Always keep winget in sync with the latest version — mandatory part of every release.** Bump the
   in-repo mirror `packaging/winget/*.yaml` (PackageVersion in all three + InstallerUrl/InstallerSha256 of
   the released `Downloader-win-x64.zip`) on `develop`, then submit a PR to `microsoft/winget-pkgs` under
   `manifests/b/bezzad/Downloader/X.Y.Z/`. `release.sh` does both via `submit_winget`. The package identifier
   is `bezzad.Downloader` (Moniker `downloader`); the installer is the portable zip (nested `Downloader.exe`).
   **Dedup rule (learned the hard way):** before opening a winget PR, check for an existing OPEN one
   (`gh pr list --repo microsoft/winget-pkgs --author @me`) — close stale/older-version PRs instead of
   stacking duplicates. winget-pkgs PRs pass automated validation then wait on a community moderator to
   merge (the CLA is already signed for `bezzad`).
5. Verify with `brew info --cask downloader` (after refreshing the local tap) that it reports the new version.
6. Record the release in the relevant OpenSpec change (or a short release note in the archive) — version, tag, commit hashes, tap commit, winget PR # — per the workflow above.

## Token-efficient builds & tests (MANDATORY)

- **`dotnet build`**: always run with `-v q --nologo` (e.g. `dotnet build Downloader.Desktop.sln -v q --nologo`). Only re-run without `-v q` if you need to inspect a specific error in detail.
- **`dotnet test`**: always run with `-v q --nologo`. On failure, re-run ONLY the failing test(s) with `--filter FullyQualifiedName~<TestName>` instead of the whole suite.
- **Long-running commands** (`dotnet test`, `dotnet build`, `npm test`, Playwright, `gh run watch`): run them with `run_in_background: true` and wait for the completion notification — never poll in a `while … sleep` loop, and never dump their full output into context. After completion, read only the tail / failure section of the output.

<!-- rtk-instructions v2 -->
# RTK (Rust Token Killer) - Token-Optimized Commands

## Golden Rule

**Always prefix commands with `rtk`**. If RTK has a dedicated filter, it uses it. If not, it passes through unchanged. This means RTK is always safe to use.

**Important**: Even in command chains with `&&`, use `rtk`:
```bash
# ❌ Wrong
git add . && git commit -m "msg" && git push

# ✅ Correct
rtk git add . && rtk git commit -m "msg" && rtk git push
```

## RTK Commands by Workflow

### Build & Compile (80-90% savings)
```bash
rtk cargo build         # Cargo build output
rtk cargo check         # Cargo check output
rtk cargo clippy        # Clippy warnings grouped by file (80%)
rtk tsc                 # TypeScript errors grouped by file/code (83%)
rtk lint                # ESLint/Biome violations grouped (84%)
rtk prettier --check    # Files needing format only (70%)
rtk next build          # Next.js build with route metrics (87%)
```

### Test (60-99% savings)
```bash
rtk cargo test          # Cargo test failures only (90%)
rtk go test             # Go test failures only (90%)
rtk jest                # Jest failures only (99.5%)
rtk vitest              # Vitest failures only (99.5%)
rtk playwright test     # Playwright failures only (94%)
rtk pytest              # Python test failures only (90%)
rtk rake test           # Ruby test failures only (90%)
rtk rspec               # RSpec test failures only (60%)
rtk test <cmd>          # Generic test wrapper - failures only
```

### Git (59-80% savings)
```bash
rtk git status          # Compact status
rtk git log             # Compact log (works with all git flags)
rtk git diff            # Compact diff (80%)
rtk git show            # Compact show (80%)
rtk git add             # Ultra-compact confirmations (59%)
rtk git commit          # Ultra-compact confirmations (59%)
rtk git push            # Ultra-compact confirmations
rtk git pull            # Ultra-compact confirmations
rtk git branch          # Compact branch list
rtk git fetch           # Compact fetch
rtk git stash           # Compact stash
rtk git worktree        # Compact worktree
```

Note: Git passthrough works for ALL subcommands, even those not explicitly listed.

### GitHub (26-87% savings)
```bash
rtk gh pr view <num>    # Compact PR view (87%)
rtk gh pr checks        # Compact PR checks (79%)
rtk gh run list         # Compact workflow runs (82%)
rtk gh issue list       # Compact issue list (80%)
rtk gh api              # Compact API responses (26%)
```

### JavaScript/TypeScript Tooling (70-90% savings)
```bash
rtk pnpm list           # Compact dependency tree (70%)
rtk pnpm outdated       # Compact outdated packages (80%)
rtk pnpm install        # Compact install output (90%)
rtk npm run <script>    # Compact npm script output
rtk npx <cmd>           # Compact npx command output
rtk prisma              # Prisma without ASCII art (88%)
```

### Files & Search (60-75% savings)
```bash
rtk ls <path>           # Tree format, compact (65%)
rtk read <file>         # Code reading with filtering (60%)
rtk grep <pattern>      # Search grouped by file (75%). Format flags (-c, -l, -L, -o, -Z) run raw.
rtk find <pattern>      # Find grouped by directory (70%)
```

### Analysis & Debug (70-90% savings)
```bash
rtk err <cmd>           # Filter errors only from any command
rtk log <file>          # Deduplicated logs with counts
rtk json <file>         # JSON structure without values
rtk deps                # Dependency overview
rtk env                 # Environment variables compact
rtk summary <cmd>       # Smart summary of command output
rtk diff                # Ultra-compact diffs
```

### Infrastructure (85% savings)
```bash
rtk docker ps           # Compact container list
rtk docker images       # Compact image list
rtk docker logs <c>     # Deduplicated logs
rtk kubectl get         # Compact resource list
rtk kubectl logs        # Deduplicated pod logs
```

### Network (65-70% savings)
```bash
rtk curl <url>          # Compact HTTP responses (70%)
rtk wget <url>          # Compact download output (65%)
```

### Meta Commands
```bash
rtk gain                # View token savings statistics
rtk gain --history      # View command history with savings
rtk discover            # Analyze Claude Code sessions for missed RTK usage
rtk proxy <cmd>         # Run command without filtering (for debugging)
rtk init                # Add RTK instructions to CLAUDE.md
rtk init --global       # Add RTK to ~/.claude/CLAUDE.md
```

## Token Savings Overview

| Category | Commands | Typical Savings |
|----------|----------|-----------------|
| Tests | vitest, playwright, cargo test | 90-99% |
| Build | next, tsc, lint, prettier | 70-87% |
| Git | status, log, diff, add, commit | 59-80% |
| GitHub | gh pr, gh run, gh issue | 26-87% |
| Package Managers | pnpm, npm, npx | 70-90% |
| Files | ls, read, grep, find | 60-75% |
| Infrastructure | docker, kubectl | 85% |
| Network | curl, wget | 65-70% |

Overall average: **60-90% token reduction** on common development operations.
<!-- /rtk-instructions -->
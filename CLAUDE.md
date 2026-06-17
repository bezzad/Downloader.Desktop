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
- **Never commit automatically**: make edits in the working tree and leave them for the author to review; only run `git commit`/`git push` when the author explicitly asks.
- The author steers and gives feedback; fold it in and keep this file current.

## Stack
- **.NET 10** (`net10.0`); macOS build target switches to `net10.0-macos` when `IsMacBuild=true` (requires the `macos` workload + Xcode; only used for the native `.app` bundle, not the CI release which builds plain `net10.0`).
- **Avalonia UI 12** with **ReactiveUI** (MVVM), Fluent theme, Inter font, Skia, DataGrid.
- **Downloader 5.8.0** NuGet package (the core download engine — not in this repo).
- DI via `Microsoft.Extensions.DependencyInjection`; logging via `Microsoft.Extensions.Logging`.
- macOS `.app` bundling via `Dotnet.Bundle`.
- `Nullable` is **disabled** in the app csproj (enabled in `Directory.Build.props` but overridden).

## Layout (`src/`)
- `Downloader.Desktop.sln` — solution.
- `Directory.Build.props` — shared props.
- `Downloader.Desktop/`
  - `Program.cs` — Avalonia entrypoint (`BuildAvaloniaApp`, classic desktop lifetime).
  - `App.axaml(.cs)` — app bootstrap, **DI registration in `ConfigureServices()`**, platform guard (desktop-only), shutdown-save hook (`DesktopOnShutdownRequested`, currently commented out).
  - `Models/` — `Config.cs` (persisted settings + theme), `DownloadItem.cs` (persisted download record).
  - `Services/` — `IFileService`/`FileService.cs` (JSON load/save of `Config`), `DialogHelper.cs` (modal dialogs + folder picker).
  - `ViewModels/` — `ViewModelBase` (has `View`), `MainViewModel`, `DownloadsViewModel`, `DownloadItemViewModel`, `AddDownloadItemViewModel`, `SettingViewModel`.
  - `Views/` — matching `.axaml(.cs)`: `MainWindow`, `DownloadsView`, `AddDownloadItemView`, `SettingView`.
  - `Assets/` — icons (`.ico`/`.icns`/`.png`), `Info.plist`, `config.json`, `Icons.axaml`.

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
   - **winget**: added `Moniker: downloader` so `winget install downloader` works (identifier must stay `bezzad.Downloader`); README updated.
   - #2 (notification green tick) was already correct on GNOME (`emblem-default`) — no change needed; reverted an unnecessary bundled-icon attempt.
   - **Tests**: still **63**, all green.

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
- Keep this file updated when structure changes to minimize re-exploration.

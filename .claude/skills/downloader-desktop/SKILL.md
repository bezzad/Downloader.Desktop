---
name: downloader-desktop
description: Build, run, test and develop the Downloader.Desktop app (Avalonia/.NET download manager). Use for any task in this repo — launching the GUI, running the test suite, regenerating screenshots, or implementing features against the architecture below.
---

# Downloader.Desktop

Cross-platform (Windows/Linux/macOS) Avalonia + .NET 10 GUI for the `Downloader` multipart-download engine. MVVM with ReactiveUI. Original modern-minimal design (ocean-blue/teal, light+dark). End-user focused: simple, stable, sensible defaults.

All commands run from the **`src/`** folder (where `Downloader.Desktop.sln` lives).

## Maintaining this skill (read first, every session)
Treat this file as a living cache. **Whenever you discover something non-obvious that a future session would otherwise have to re-derive** (an engine API shape, a gotcha, a settled design choice), append a concise boilerplate note here. The goal is *steadily fewer tokens per session*: each future run should read the answer here instead of re-grepping the codebase or the sibling `../Downloader` engine. Keep additions short and factual — a few lines, not essays. Prune notes that become wrong. This is an explicit standing instruction from the author.

**Commit policy**: follow root `CLAUDE.md` → "Workflow & progress tracking" — commit frequently directly to `develop` and push, including skill-file notes, as part of routine work. (Superseded the older "never commit automatically" rule once the cross-machine PLAN.md/TASKS.md workflow was set up.)

## Code map (read this before grepping — it's where things live)
Skip the discovery grep; jump straight to the file. `src/Downloader.Desktop/`:
- **Download lifecycle / queues / scheduler**: `Services/DownloadManager.cs` (+ `IDownloadManager.cs`). Owns `Items`, all state transitions (`Start`/`Pause`/`Cancel`/`Resume`/`Retry`/`StopAll`), `PumpQueue` (the ONLY capped start path — `Start` is the uncapped primitive), `EvaluateSchedules`, `ApplyGlobalSpeedLimit`. `Start` builds the `DownloadConfiguration` from `Settings.ToConfiguration()` **synchronously before its first `await`**, so `vm.Status=Running` + `vm.Configuration` are observable right after `Add(autoStart:true)` — that's how the headless tests assert without real I/O (unreachable IP `10.255.255.1`).
- **Per-row state/UI**: `ViewModels/DownloadItemViewModel.cs`. Model-backed props write through to `_item` (pattern: `get => _item.X; set { _item.X = value; RaisePropertyChanged(); }` — see `Status`, `HasCustomSpeedLimit`). Exposes `GetItem()`, `Manager`, live `Configuration`.
- **Persisted record**: `Models/DownloadItem.cs`. **Global settings** (mirrors the engine `DownloadConfiguration`): `Models/DownloadSettings.cs` (+ `ToConfiguration()`). **Root persisted state**: `Models/Config.cs` (`Settings`/`Queues`/`Schedules`/`Downloads`, `DefaultQueue`).
- **Settings screen**: `ViewModels/SettingViewModel.cs` — `S` = the `DownloadSettings`; setters that must "bite" live also call the manager (e.g. `MaxConcurrentDownloads`→`PumpQueue`, `MaxSpeedKbPerSecond`→`ApplyGlobalSpeedLimit`).
- **Details dialog**: `ViewModels/DownloadDetailsViewModel.cs` + `Views/DownloadDetailsView.axaml` (per-connection strip, mirror editor, speed-limit box). Reaches global config via `Item.Manager.Config`.
- **Custom window chrome**: `Views/TitleBar`, `Views/ResizeGrips.axaml.cs` (+ pure `Views/WindowResize.cs`). **Notch overlay**: `Views/NotchView.axaml(.cs)` (height constants) + `ViewModels/NotchViewModel.cs` (`MaxRows=3`, `HasOverflow`).
- **i18n**: `Assets/i18n/*.json` (16 packs) + `Services/Localizer` + `Markup/TrExtension`. **Notifications**: `Services/NotificationService`. **Tray/startup/update**: `Services/{TrayService,StartupService,UpdateService}`.
- **Tests** (`src/Downloader.Desktop.Tests/`): foldered by kind — `Unit/` (pure), `Integration/` (manager+engine, headless via `[AvaloniaFact]`), `UI/`, `Plugins/`. Build manager tests with `new DownloadManager(); Initialize(Config.New()); Add(item, autoStart)`.

## Token discipline in this repo (the author flagged over-spend on small fixes)
1. **Use the Code map above instead of grepping** for where a thing lives. Only grep for a specific symbol you can't place from it.
2. **Read the method, not the file** — use `Read` with `offset`/`limit` (or `grep -n` the symbol first) rather than dumping 400-line files. VMs/`DownloadManager` are large.
3. **Batch independent reads/greps into one message** (multiple tool calls per turn) — don't serialize discovery.
4. **Build once per logical chunk**, not after every edit; `Edit` already fails loudly on a bad match, so don't re-`Read` a file just to confirm an edit landed.
5. For a small, well-scoped fix, target the one file the Code map names, edit, then one build+filtered-test — that's the whole loop.

## Engine (`Downloader` 5.9.5) quick reference
- `DownloadBuilder` is **single-URL only** (`WithUrl(string)`) and its `IDownload` **cannot take a logger** (no `AddLogger` on `IDownload`). For mirrors and logging, use `DownloadService` directly instead of the builder.
- `DownloadService(DownloadConfiguration cfg, ILoggerFactory factory = null)` — implements `IDownloadService`: same events (`DownloadStarted/DownloadProgressChanged/ChunkDownloadProgressChanged/DownloadFileCompleted`), plus `Package`, `Pause()`, `Resume()`, `CancelAsync()`/`CancelTaskAsync()`, `Clear()`, and `AddLogger(ILogger)`.
- **Multi-URL / mirrors** are first-class: `DownloadFileTaskAsync(string[] urls, DirectoryInfo folder, ct)` (auto-resolves name), `(string[] urls, string fileName, ct)`, and package overloads. `DownloadPackage.Urls` is `string[]`. So the data model should carry `List<string> Urls` (first = primary, rest = mirrors), not a separate `Url` + `Mirrors`.
- Filename still auto-resolves from URL/Content-Disposition; read it from `DownloadStartedEventArgs.FileName` (full path).

## Localization (i18n) — how it works here
- `Services/Localizer` (singleton) loads `Assets/i18n/{lang}.json` (en, fa, es, fr, ar, eo) via `AssetLoader`; English is the fallback. Active language persists in `DownloadSettings.Language`; load it at startup in `MainViewModel` and switch it from Settings (`SelectedLanguage`).
- **XAML usage:** `Text="{i18n:Tr Some_Key}"` (xmlns `i18n="clr-namespace:Downloader.Desktop.Markup"`). VM strings: `Localizer.Instance["Key"]`. Format strings use `{0}` + `string.Format`.
- **Live-switch gotcha (important):** Avalonia indexer-change notifications (`"Item[]"`/empty `PropertyChanged`) do NOT reliably refresh already-rendered `[key]` bindings. Instead `{i18n:Tr}` binds to `Localizer.Tick` (a normal int bumped each `Load`) through `TrConverter`, which DOES refresh. Don't revert to a raw indexer binding.
- **RTL:** `Localizer.FlowDirection` is RightToLeft for fa/ar; each Window binds `FlowDirection="{Binding FlowDirection, Source={x:Static services:Localizer.Instance}}"` (UserControls inherit it). 
- VM-computed localized strings (row StatusText/DisplayName/Group, details headers) subscribe to `Localizer.PropertyChanged` and re-raise; `DownloadItemViewModel.Detach()` unsubscribes on removal (called by the manager) to avoid leaks.
- Adding a key: add to `en.json` first (it's the fallback), then translate into the other 5. Missing keys fall back to English gracefully.

## Engine/behavior gotchas worth caching
- **Plan-part completion must NOT gate on `DownloadPart.ExpectedSize`** (`DownloadManager.Plans.cs`): for extracted streams (progressive/video/audio via yt-dlp) `ExpectedSize` is `filesize_approx` — an *estimate* (e.g. x.com reported 5.36 MB, real file 3.66 MB). An exact `len==ExpectedSize` gate made a finished part look unfinished → "Part 1/1 did not finish downloading" and an infinite re-download. `IsPartComplete`/`PartDownloadedOk`/`MarkPartDone` now rely on the `.done` marker + non-empty file (the engine already validates the full download and reports errors via the completion event). `ExpectedSize` is kept only for progress display. **x.com/YouTube page-URL downloads work directly** — the HLS plugin (`com.bezzad.hls`, now **in-repo** at `src/Downloader.Desktop.Plugins/Downloader.Desktop.Plugins.Hls`, an **optional/catalog-tier** plugin — was the separate `../Downloader.Plugins` repo, consolidated in; see the "Plugin consolidation" note near the end) runs yt-dlp, no browser-extension .m3u8 hunting needed. yt-dlp runs **bare (no cookies)** so public content works but login-gated/age-restricted media would need `--cookies-from-browser` (future work). Two plugin bugs fixed in v1.1.1 (root-relative segments becoming `file://` on Unix; codecless progressive MP4s skipped — see `Downloader.Plugins` issue #2). **YouTube (plugin v1.1.2)**: needs BOTH browser cookies (bot check — plugin retries `--cookies-from-browser` per installed browser) AND a **deno** JS runtime (`--js-runtimes deno:<path>`, auto-provisioned like yt-dlp/ffmpeg) — without deno yt-dlp can't solve the "n challenge" and returns ONLY storyboard images (no formats). Node ≤20 is "unsupported" by yt-dlp's EJS solver — don't bother with it; deno is the supported default (see `Downloader.Plugins` issue #3).
- **`HttpClientTimeout` is the WHOLE-request timeout** (`HttpClient.Timeout`), incl. reading a chunk's body — keep it large (default 100 s). Setting it small (e.g. 10 s) makes longer chunks fail with "Operation Cancelled" after retries (~1 min). Per-block stalls are handled by `BlockTimeout`, not this.
- **Cancellation vs failure status**: the engine raises `DownloadFileCompleted` with `Cancelled=true` for BOTH a user pause/stop and an internal abort (e.g. timeout). Disambiguate by the status we set *before* calling the engine: if it's already Paused/Stopped it was the user; a cancel while still Running = real failure → mark Failed.
- **"File already exists" is NOT a failure** (FileExistPolicy=IgnoreDownload, the app default): when the target already exists the engine **skips** the download — it `SendDownloadCompletionSignal(Stopped)` (so `DownloadFileCompleted` arrives `Cancelled=true, Error=null`) and **never fires `DownloadStarted`**. That used to be misread as a timeout failure. The manager now calls `TryMarkAlreadyExists` in the cancelled branch: if `DownloadManager.LooksAlreadyDownloaded(policy, path)` (policy==IgnoreDownload && the resolved file exists on disk), it backfills name/folder/size from that file and marks the row **Completed** with `DownloadItemViewModel.AlreadyExisted=true` (StatusText → `State_Exists` "Already downloaded"; still IsCompleted, green bar). The final path comes from `(e.UserState as DownloadPackage)?.FileName` (the event's userState IS the `DownloadPackage`) or `vm.Download.Package.FileName`. `AlreadyExisted` is reset in `Start`. No new enum state was added (would ripple through filters/converters) — it's a Completed row with a display flag.
- **Queued-item file names**: the engine only resolves the name once a download starts, so queue-capped items show no name. `UrlResolver.ResolveFileNameAsync` (Content-Disposition → URL path) fills a VM-only `PreviewName` in the background; don't write it to `DownloadItem.FileName` or it gets forced on the engine.
- **Integration test pattern**: spin up a loopback `HttpListener` with Range/206 support and download through a real `DownloadService` — no external network, CI-safe (see `IntegrationTests`).
- **UI progress coalescing (perf — main-thread budget)**: do NOT marshal each engine `DownloadProgressChanged` to the UI (with N downloads × M connections that floods the dispatcher and makes the grid lag). Handlers call `vm.StageProgress(...)` (plain fields, any thread, no UI touch); a single `DispatcherTimer` in `DownloadManager` (`EnsureUiPump`, 250 ms) flushes all rows via `vm.FlushProgress()` and fires `StatsChanged` once per tick. The pump self-stops when no row is `Running`. `FlushProgress` drops staged values unless `Status==Running`, so a paused row keeps its last fill. This bounds main-thread work regardless of download count — keep it; don't re-add per-event `Dispatcher.UIThread.Post`.
- **Queue concurrency cap — single choke point**: `Start(vm)` is the *uncapped* primitive and must only be reached via `PumpQueue`. Every user-facing start path (`Resume`, `Retry`, `StartAll`, bulk `StartSelected` → `Resume`, `Add(autoStart)`, completion's `TryStartNextInQueue`) must re-queue the item (set `Status=Created`) and call `PumpQueue`, which starts/resumes only while `running < MaxConcurrent`. `PumpQueue` handles both Paused (resume in place) and Created/None (start fresh), paused first. Regression to watch: making `Resume`/`StartAll` call `Start` directly bypasses the cap (e.g. select 10 with cap 2 → all 10 ran). `Start` sets `Status=Running` synchronously before its first `await`, so `PumpQueue`'s running recount is correct mid-loop.
- **Cap value lives on `DownloadQueue.MaxConcurrent`, but the user sets it via Settings** (`DownloadSettings.MaxConcurrentDownloads`). These are TWO fields — `MaxConcurrentDownloads` historically only *seeded* new queues, so changing it never limited anything (the real bug behind "I set max 2 but 10 ran"). They're now kept in lockstep for the **primary/default queue** (`Config.DefaultQueue` = `Queues[0]`): `SettingViewModel.MaxConcurrentDownloads` setter writes it through to `DefaultQueue.MaxConcurrent` + `PumpQueue`; `DownloadManager.Initialize` re-syncs the default queue from the setting on load (fixes stale saved configs); and the Queues page (`QueueRowViewModel.MaxConcurrent`) mirrors edits of the default queue back into the setting. Extra (non-default) queues keep their own caps. So enforcement reads `queue.MaxConcurrent`, but the default queue's value always equals the Settings number.

- **State transitions must be guarded in the manager, not just the buttons**: per-row buttons gate via `IsVisible`/`Can*`, but **bulk** actions (`StopSelected`→`Cancel`, `StartSelected`→`Resume`) apply to *every* selected row regardless of state. So the guards live in `DownloadManager` (the single choke point that all callers — buttons, bulk, scheduler, pump — go through):
  - `Pause` no-ops unless Running.
  - `Cancel` (= "Stop") acts on Running/Paused **and queued (Created/None)** → all become Stopped; it no-ops only for terminal/idle states (Completed/Failed/already-Stopped). **Stopping the queued rows too is essential**: otherwise stopping the running rows fires `DownloadFileCompleted`→`TryStartNextInQueue` and the pump immediately starts the next queued rows ("select all → Stop: 3 stop, 3 start"). The `StopSelected` loop runs synchronously before any completion callback is posted, so by the time the pump runs there are no queued rows left to start. (Do **not** restrict `Cancel` to Running/Paused only — that reintroduces the bug.)
  - `Resume`/`Start` no-op if Running/Completed; `Retry` only acts on Failed/Stopped. Prevents re-running a completed download from 0% and stray double-`Start` (a second engine reporting from 0% → "100% then begins again from 0").
  Keep state-machine rules in the manager methods, not scattered across VMs/views.
- **`StartQueue` must re-queue Stopped/Failed**: `PumpQueue` only picks up Paused/Created/None, so `StartQueue`/`StartAll` must first flip Stopped (and Failed, for StartQueue) rows to Created — otherwise "Start queue → <name>" after a Stop/Stop-all does nothing (rows are Stopped). `StartQueue` does this in a `RunBatch` then pumps.
- **Completed rows always show 100%**: don't compute a completed row's bar from `Downloaded/Size` — a file that already existed on disk is Completed with `Downloaded=0`, and that read 0% (esp. after restart). The `DownloadItemViewModel` ctor forces `_progress=100` when `Status==Completed`, the `Status` setter sets `Progress=100` on transition to Completed, and the already-exists handler persists `Downloaded=fileLength`.
- **Status badge colors** live in `Converters/StatusToBrushConverter`: Running teal, Completed green, Failed red, Paused amber, Stopped neutral-gray, Queued steel-blue (`#4F6D9C` — deliberately distinct from Stopped's gray so a waiting vs stopped row is tellable apart). Badge shows for every state except Running (which shows live `%`); `DownloadItemViewModel.ShowStatusBadge` = `Status != Running`.
- **Queues page = a real queue manager** (`Views/QueuesView.axaml`, `ViewModels/QueuesViewModel.cs`): per-queue card shows live aggregate stats (`RunningCount/WaitingCount/DoneCount/FailedCount`, `TotalSpeedText`, `SummaryText`) + a combined `OverallProgress` bar (average of item `Progress`), a run/pause `ToggleSwitch`, the concurrency cap, and the queue's downloads with per-item progress + pause/resume/retry/cancel/remove + reorder (`ChevronUp/Down` → `manager.MovePriority(vm, ±1)`) + move-between-queues (a `MenuFlyout` of `QueueMoveTarget`s → `manager.MoveToQueue(vm, queueId)`, hidden when only one queue). Rows are wrapped in `QueueItemViewModel` (holds the real `DownloadItemViewModel` as `Item` + the reorder/move commands); the card's `Items` is an `ObservableCollection<QueueItemViewModel>` rebuilt on `ListChanged` (order = master `Items` order = pump priority), and aggregates refresh on both `ListChanged` and `StatsChanged` (live). `QueueRowViewModel.Detach()` unsubscribes both events. **Pump order follows master-list order**, so `MovePriority` just `Items.Move`s past the same-queue neighbour. `Initialize` now backfills `QueueId=DefaultQueue.Id` for items saved without one (older configs) so they always appear on a queue. `DownloadItemViewModel.FormatBytes` is now `public static` for reuse. **Queue set changes sync via `IDownloadManager.QueuesChanged`**: queues can be created/removed OUTSIDE the Queues page (the Add-download dialog's inline "new queue" box → `manager.AddQueue`), so `QueuesViewModel` subscribes to `QueuesChanged` and reconciles its rows from `_config.Queues` (`SyncFromConfig` — drops gone rows, inserts new in config order, keeps existing rows/UI state). The page's own Add/Remove buttons just call the manager and let the event add/remove the card — don't mutate `Queues` directly or a queue made elsewhere won't show until restart (the original bug).

## System tray / startup / auto-update (Round 11) — patterns worth caching
- **Tray** (`Services/TrayService`, static): create `TrayIcon` in code and register via `TrayIcon.SetIcons(Application.Current, new TrayIcons { icon })`. **`NativeMenuItemToggleType` does NOT exist in this Avalonia 12** — don't use `ToggleType`/`IsChecked` on `NativeMenuItem`; reflect state by swapping the item's `Header` ("Disable/Enable notifications"). Wrap creation in try/catch — headless/no-session platforms throw; on failure leave `_tray=null` so close-to-tray fails soft. **Linux tray (Ubuntu GNOME/AppIndicator) — DO NOT make speculative changes here. Ever.** This code has flip-flopped three times (`2654f9a` removed the Clicked handler, `3aac545` re-added it, `fa6b925` gated it off for Linux) and each theory about the `TrayIcon.Clicked → ShowWindow` handler was later contradicted by on-device behavior: after `fa6b925` gated the handler off, the author reported the tray **icon stopped appearing at all** (previously it appeared and only the right-click menu was broken) — so the handler was reverted to unconditional, which is the configuration of every build where the icon did show. Conclusions that ARE settled: (a) keep the unconditional `_tray.Clicked += → ShowWindow` subscription; (b) **use a SMALL tray icon** (downscale to 64×64 via `Bitmap.CreateScaledBitmap` — the 1080×1080 PNG is a ~4.6 MB pixmap over DBus and can make the SNI item render while its menu fails to attach). The right-click-menu-doesn't-open bug is still OPEN and **cannot be diagnosed from this headless box** (no desktop session, no DBus StatusNotifierWatcher): any further change requires on-device evidence first — e.g. run the app on the Ubuntu box with logging enabled, `dbus-monitor` the `org.kde.StatusNotifierItem` traffic, or test a minimal Avalonia tray repro — never another code-only guess.
- **Close-to-tray**: handle `window.Closing`, `e.Cancel=true; window.Hide()` — but gate on **`TrayService.IsActive`**, NOT the setting, or a failed tray strands the window with no way back. Real quit sets a `_quitting` flag then `window.Close()` (ShutdownMode is OnMainWindowClose → App.ShutdownRequested still saves). Wired in `MainViewModel.SetupAppShell()` after config loads (needs `View as Window`).
- **Run-at-startup** (`Services/StartupService`): no extra deps — Windows via `reg.exe add/query/delete HKCU\...\Run` (avoids `Microsoft.Win32.Registry` package on the non-windows TFM), Linux `~/.config/autostart/downloader.desktop`, macOS `~/Library/LaunchAgents/*.plist`. Launches with `--minimized`; `MainViewModel` hides the window at startup if that arg is present AND tray active. Coupling lives in `SettingViewModel`: disabling tray disables startup; enabling startup enables tray.
- **Auto-update** (`Services/UpdateService` + `UpdateFlow`): **version compare uses `Assembly.GetName().Version` (`CurrentVersion` = `Major.Minor.Build`), NOT `InformationalVersion`** (that has the date-derived revision and would never compare sensibly to a `v1.1.0` tag). **Versioning fix (#update-false-alarm, 2026-06-19):** `VersionPrefix` is now the FULL 3-part semver (e.g. `1.1.2`) and `AssemblyVersion=$(VersionPrefix).0` so the app reports its real patch — the old `major.minor.0.0` pin made it always report `x.y.0`, so every patch release (e.g. `v1.1.1`) looked "newer" forever → false "update available". `release.yml` stamps `-p:VersionPrefix=<tag-without-v>` from the tag so a released build reports exactly the tag; About card (`SettingViewModel.AppVersion`) shows `UpdateService.CurrentVersion` so About + update status + release tag all agree. **Keep `VersionPrefix` three-part.** `UpdateService.IsNewer(tag, current)` + `Normalize(tag)` are pure/tested. Flow: GitHub `releases/latest` → if newer, the in-app `UpdateFlow.PromptUpdate` dialog (Download/Later) + a passive OS notification (NOT a clickable toast — see the OS-only note below) → download the per-RID asset (`ExpectedAssetName()` matches release.yml names) via a throwaway `DownloadService` → `ApplyDownloadedArchive` spawns a detached unix `.sh`/win `.cmd` that waits for the PID to exit, extracts over the app dir, relaunches → `UpdateFlow.RequestShutdown` (= MainViewModel.Quit). The self-swap is untestable here; only the version logic has tests.
- **Notifications are OS-only (2026-07-10)**: `NotificationService` now shows EVERY message as a native OS notification (Linux `notify-send`, macOS `MacNotifier` in-process banner, Windows `WindowsNotifier` toast) on all platforms, **regardless of window focus**. There are NO in-app toasts and NO focus tracking — the whole focus-aware routing model (`Attach`/`SetFocused`/`AppFocused`/`PreferOsChannel`, `WindowNotificationManager`, `ShowAction` + its pending-action replay queue) was **removed** (it caused the macOS "in-app toast from the tray" bug: hide-to-tray doesn't fire `Deactivated`). Surface is just `Notify(title,msg,isError)` (gated by the on/off switch) + `Inform(...)` (always). **Native channels can't carry a click callback**, so any actionable prompt keeps its action IN THE WINDOW: app update = `UpdateFlow.PromptUpdate` dialog + "Update Downloader" nav button; plugin update = Settings→Plugins row "Update" button (`PluginRowViewModel.UpdateAvailable`); post-download action = the completed-row action button (`PostDownloadActionLabel`). On native failure (no notify daemon / blocked toast API) the message is just skipped + logged — no fallback. Don't reintroduce in-app toasts or `ShowAction`.

## Avalonia 12 UI patterns (Round 11)
- **Rounded window corners** (now **10px**, all three windows): set Window `Background="Transparent"` + `TransparencyLevelHint="Transparent"`, move the real background onto the root `Border` with `CornerRadius="10" ClipToBounds="True"`. **The root border's `Background` MUST be an opaque resource** — `ThemeBackgroundColor` is NOT defined by the Fluent theme here, so it resolved to nothing and the transparent window showed the desktop/window behind through dialogs. Use `{DynamicResource SystemRegionColor}` (what MainWindow uses). Caveat: on Linux WMs without a compositor the corners/shadow vary — accepted.
- **Per-fragment colors in the details strip** (#7): give `ChunkProgressViewModel` a stable `IBrush Brush` from a curated palette indexed by `Index` (no reshuffle on update), and bind the segment `ProgressBar Foreground="{Binding Brush}"` (the plain-track+fill ProgressBar's fill is its `Foreground`). Palette stays within **one blue→teal family** (deep blue → sky → cyan → teal), not a rainbow — author preference.
- **Theme-aware README images**: GitHub honors `<picture><source media="(prefers-color-scheme: dark)" srcset="…dark.png"><img src="…light.png"></picture>` — dark shot in dark mode, light otherwise. Needs both light+dark captures (added a `settings-light.png` capture alongside the dark one). **GitHub renders `.svg` images** referenced from markdown, so the README banner is a hand-authored `docs/banner.svg` (no PNG rasterizer needed; use web-safe font-family so text renders through camo).
- **Notification success icon**: Linux `notify-send -i emblem-default` (green check) for success, `dialog-error` (red) for failure — don't use `dialog-information` (blue "i") for a completed download (#3).
- **Icon inside a TextBox**: `<TextBox.InnerLeftContent><PathIcon .../></TextBox.InnerLeftContent>` (search = `SearchRegular`, link = `LinkRegular`, both added to `Icons.axaml`).
- **Nav count pill on a selected (accent-filled) item**: the selected nav sets descendant text white via `TextElement.Foreground`, which made the pill number invisible on the light pill. Fix with a more-specific style: `Button.nav.selected Border.pill` → opaque white bg, `Button.nav.selected Border.pill > TextBlock` → accent foreground (a style setter on the TextBlock beats the inherited attached value). Pills also get `Margin="8 0 0 0"`.
- **Linux taskbar icon**: set `X11PlatformOptions.WmClass = "Downloader"` in `Program.cs` and make the installed `.desktop` `StartupWMClass=Downloader` match — that's what makes the DE use our icon instead of a generic/host (e.g. IDE) one. A raw ELF can't carry a file-manager icon; the `.desktop` from `scripts/install.sh` provides it.

## Round 15 patterns (shutdown-on-complete, queue buttons, granular notifications, browser, i18n, icon)
- **Dynamic MenuFlyout of runtime items**: bind `MenuFlyout ItemsSource="{Binding Targets}"` + an `ItemContainerTheme` `ControlTheme TargetType=MenuItem x:DataType=<wrapper>` `BasedOn="{StaticResource {x:Type MenuItem}}"` with `Header`/`Command` setters. The wrapper is a tiny `{ string Name; ICommand Command }` built in the VM. Live examples: `QueueActionTarget` (Start/Stop queue buttons in `DownloadsView`) and `QueueMoveTarget` (move-to-queue in `QueuesView`). Don't try to bind a generated `MenuItem`'s Command back to the page VM — its DataContext is the item.
- **All-downloads-complete + shutdown-on-completion**: `DownloadManager` raises `AllDownloadsCompleted` once when a completion drains the list (`ActiveCount==0 && QueuedCount==0 && CompletedCount>0`), guarded by `_allCompleteFired` (re-armed in `Start`). **CRITICAL: the trigger must only fire when a download actually COMPLETED, never on a stop/cancel/fail** — else "Stop All" (which cancels rows → Stopped) would arm a shutdown whenever a finished item sits in the list. The terminal handler routes through `FinishTerminal(vm)` which calls `MaybeAllCompleted()` only `if (vm.Status == DownloadStatus.Completed)`. Test seams `RaiseCompletedForTest`/`RaiseStoppedForTest` both go through `FinishTerminal`. `MainViewModel.OnAllDownloadsCompleted` does the UI/OS parts (all-complete notification + `ShutdownService.Schedule`) so the manager stays UI/OS-free + testable.
- **`ShutdownService` (the cancel UX matters)**: shows a **Topmost standalone `ShutdownView` countdown dialog** (`ShutdownViewModel`, 30 s, "Cancel" + "Shut down now" + Esc=cancel) — a top-level window so it's visible **even when the app is minimized to the tray** (an in-app `WindowNotificationManager` toast is NOT, since its host is the hidden main window — don't use it here). It ALSO fires a **native OS notification** (`NotificationService.Notify`, prefers `notify-send`/`osascript`) as a heads-up when `NotifyOnShutdown` is on. The dialog is always shown (it's the safety/cancel mechanism); `notify` only gates the extra native alert. Power-off (`shutdown /s` / `osascript … shut down` / `systemctl poweroff`) has a `PowerOffOverride` test seam.
- **Granular notifications**: master `EnableNotifications` gates inside `NotificationService.Notify`; per-event toggles (`NotifyOnComplete/Failed/AllComplete/Shutdown`) are checked in the manager / MainViewModel *before* calling NotificationService (they read `_config.Settings` live). Settings UI: a "NOTIFICATIONS" card with sub-toggles `IsEnabled="{Binding EnableNotifications}"`.
- **StopAll** now *cancels* every Running/Paused/queued item (→ Stopped) via `Cancel` (was: pause running). `Cancel` already guards terminal states, so completed/failed rows are untouched.
- **Browser integration** (`Services/BrowserIntegrationService`, opt-in, default off): `HttpListener` on `http://127.0.0.1:15151/`, permissive CORS, reads `?url=`; `OnUrlCaptured` → `MainViewModel.CaptureUrl` surfaces the window + opens Add pre-filled. Parse the query manually (don't pull in `System.Web`). App side only — the extension ships separately.
- **Test seams for OS/engine side-effects**: give production code an override so tests don't hit the network/OS — `ShutdownService.PowerOffOverride` (Action), `DownloadManager.RaiseCompletedForTest(vm)` (post-completion bookkeeping without a real download). In completion tests set `config.DefaultQueue.IsRunning=false` so `PumpQueue` doesn't kick off background (network) starts.
- **Adding a UI language**: add `new LanguageOption(code,name)` to `Localizer.Languages` + create `Assets/i18n/{code}.json` (auto-embedded by `AvaloniaResource Include="Assets\**"`). Only `en.json` strictly needs every key (fallback); full packs carry all keys. Bulk-add new keys to existing locales with a small `python3` json script (`object_pairs_hook=OrderedDict`, `json.dump(…, ensure_ascii=False, indent=2)`). Shipped locales (16): en, fa, es, fr, ar, eo, tr, az, de, it, pt, ru, hi, zh, ja, ko.
- **Linux app icon** (a raw ELF can't carry one): `scripts/install.sh` fetches `downloader.png` from the repo raw URL when the tarball lacks it, installs to `hicolor/{512,256,128}x*/apps` + `~/.local/share/pixmaps`, then `gtk-update-icon-cache -f`. `publish.sh` + `release.yml` also copy `Assets/downloader.png` into the linux tarball so future installs find it locally. `.desktop` keeps `Icon=downloader` + `StartupWMClass=Downloader`.

## Round 16 patterns (About dialog, donate, grid toolbar, time-left, browser extension)
- **About dialog**: `Views/AboutView` + `ViewModels/AboutViewModel`, opened via `DialogHelper.ShowAbout()` (own Window, transparent+rounded like `DownloadDetailsView`, Esc closes). Left = logo/title/version/donate/website; right = three clickable `Button.about` section cards + GitHub/Telegram/Email contact `Button.icon`s. All links open with `Process.Start(UseShellExecute=true)`. Canonical links are `const`s on `AboutViewModel` (RepoUrl/EngineRepoUrl/DonateUrl/TelegramUrl/Email…) so they're testable without constructing the VM (its `VersionText` touches `Localizer` → needs headless). **Original layout — never name or clone another download app** (repo design rule).
- **Top-bar Donate(♥)/About(i)**: small `Button.icon`s in `MainWindow` top bar; `MainViewModel.DonateCommand` opens `AboutViewModel.DonateUrl` (repo `Donate.md`), `ShowAboutCommand` → `DialogHelper.ShowAbout`. Tether/Liberapay addresses live in repo-root `Donate.md` (sourced from the sibling `../Downloader` README).
- **Square toolbar buttons** (`Button.tool` in App.axaml): icon-on-top/label-below (vertical StackPanel), `:disabled` → Opacity .4. Per-row bulk actions (Start/Pause/Stop/Remove) pass an `IObservable<bool>` canExecute (`this.WhenAnyValue(x => x.HasSelection)`) so they grey out when nothing is checked; Stop-All / Start-Queue / Stop-Queue take no canExecute (always enabled).
- **Tri-state select-all in the grid header**: `bool? SelectAllState` on `DownloadsViewModel` (true=all / false=none / null=some). **GOTCHA: this Avalonia 12 DataGrid does NOT render a *control* placed in a column header** — only string `Header="..."` shows; a `<DataGridTemplateColumn.Header><CheckBox/></...>` (any control, even a plain TextBlock) renders blank, regardless of compiled-vs-ReflectionBinding. So the select-all `CheckBox` is **overlaid** over the first column's header band: it's a sibling of the DataGrid inside the wrapping `Panel`, `HorizontalAlignment=Left VerticalAlignment=Top Margin="14 9 0 0"`, with the DataGrid pinned to `ColumnHeaderHeight="38"` and the checkbox column `Width=44 CanUserResize=False` so the overlay stays aligned. Bound normally (page DataContext) since it's outside the column scope. Keep selection state live by subscribing to each row's `PropertyChanged` (IsChecked) + `manager.Items.CollectionChanged`. There is NO toolbar select-all checkbox.
- **Toolbar acts on selected rows = checked OR highlighted**: the bulk buttons enable when a row is *either* checked *or* highlighted in the DataGrid. The view's `SelectionChanged` pushes `grid.SelectedItems` into `DownloadsViewModel.SetGridSelection(...)`; `SelectedTargets()` = `Items.Where(IsChecked || _gridSelection.Contains(i))`; `HasSelection` drives the commands' `WhenAnyValue` canExecute. (Headless screenshot capture can't reproduce real row-selection — a click reads as hover — so verify this via a unit test calling `SetGridSelection`, not a screenshot.)
- **Time-left column**: `DownloadItemViewModel.TimeLeftText` = remaining ÷ Speed (only while Running), formatted by `public static FormatDuration(double seconds)` ("45s"/"1m 23s"/"2h 5m"; "—" for non-finite/idle). Re-raised from the Speed and Status setters.
- **State → only the progress bar recolors**: bind `ProgressBar.Foreground="{Binding Status, Converter=StatusToBrushConverter}"` and show `StatusText` ("62% · Paused") under it. The row **name keeps one consistent style** across states (removed the old `TextBlock.failed`/`.pending` name classes + the colored badge) — author preference.
- **Browser extension** lives at `src/browser-extension/` (NOT a .NET project, excluded from build/tests): cross-browser **MV3**, `manifest.json` (Chrome/Edge, `background.service_worker`) + `manifest.firefox.json` (`background.scripts` + gecko id). `background.js` guards `importScripts` (`if (typeof importScripts === "function")`) so the same file works as a Chrome SW (imports `common.js`) and a Firefox event page (manifest loads `common.js` first). It does context menus + `webRequest.onHeadersReceived` media sniffing (video/audio/HLS `.m3u8`; YouTube/DRM unsupported) + forwards to the app via `GET http://127.0.0.1:15151/add?url=…`. The app listener answers `/ping` (200) for the popup's status dot. Resize icons with Pillow (`Image.LANCZOS`) — no ImageMagick on this box. Store deploy is the author's (needs dev accounts); Safari intentionally skipped.

## Round 17 patterns (CI flake, single-instance, Telegram-update, snap, queue scroll)
- **Tests that read `Localizer` MUST be `[AvaloniaFact]`** (in `AppTests`), not plain `[Fact]` (in `LogicTests`). The i18n maps only load under the Avalonia headless runtime (AssetLoader); a plain Fact gets the raw key back (e.g. `"State_Pending"` instead of `"Pending"`) and is order-dependent → **flaky on CI** (it passed only if an AvaloniaFact had loaded assets first). Start such a test with `Localizer.Instance.Load("en")`. This was the cause of the intermittent macOS CI failures.
- **Single instance + IPC** (`Services/SingleInstanceService`, called first thing in `Program.Main`): a fixed loopback **lock-port** (`SingleInstanceService.LockPort` = **15150** — moved from 15152, see next note) doubles as the mutex.
- **The single-instance lock port MUST stay OUTSIDE `LocalApiService.PortRange` (15151–15155).** It was 15152 — inside the API range — so the app's own single-instance lock permanently held 15152 and the API's fallback silently skipped it (verified live: with 15151 blocked the API landed on 15153, not 15152). Moved `LockPort` to **15150**. Invariant guarded by test `SingleInstance_lock_port_is_outside_the_api_range`. If you ever change either the range or the lock port, keep them disjoint. `LockPort` is a `public const` (exposed for that test). First instance binds it (primary) + runs an accept loop; a later launch fails to bind → forwards its args (the first http(s) URL) to the primary and `return`s from Main (exits). `MainViewModel.SetupAppShell` calls `SetMessageHandler(...)` → `BringToFront()` + `CaptureUrl(msg)`. Other bind errors fail open (run normally). This is the cross-platform replacement for the Windows named-mutex + WM_COPYDATA trick.
- **"App installed but clicking it does nothing" (macOS, 2026-08-21) = a FOREIGN process on the lock port.** `AddressAlreadyInUse` used to be read as "another Downloader is running", so the app forwarded its args into that stranger's socket and `return`ed from Main: **exit 0, no window, no error, no crash report** — indistinguishable from a broken install (we burned a long session chasing Homebrew and code signing first). Real culprit on the author's Mac: **the Cursor editor listens on 127.0.0.1:15150**. Diagnose ANY "won't open" report with `lsof -iTCP:15150 -sTCP:LISTEN -n -P` FIRST, then `/Applications/Downloader.app/Contents/MacOS/Downloader; echo "exit=$?"` (`exit=0` + no output ⇒ this bug). Fixed: the primary writes a `downloader-ipc/1` greeting on accept, and `TrySendTo` only forwards after that handshake; `LockPorts` is now `{15150, 15156, 15157, 15158}` (all still outside the API range) and a foreign holder makes it try the next one; all-unusable ⇒ run WITHOUT the lock, never exit. `TryClaim(args, ports)` is an `internal` overload so tests use ephemeral ports (`FreePorts`) — never bind the real 15150 in a test, it collides with itself. Assign the static `_listener` ONLY on success (the failing path nulling it would clobber a live primary). Tests: `A_foreign_listener_on_the_lock_port_does_not_make_the_app_exit`, `A_real_second_instance_is_still_detected_and_bows_out`.
- **macOS `.app` bundle signature**: `make-macos-app.sh` now ad-hoc signs the assembled bundle (`codesign --force --deep --sign -`). Without it `codesign -v` reports *"code has no resources but signature indicates they must be present"* — the SDK signs the apphost as a STANDALONE Mach-O, but with an `Info.plist` present macOS validates it as a BUNDLE and wants `Contents/_CodeSignature/CodeResources`. The cask's quarantine strip had masked this. **It was NOT the cause of the v2.3.0 "won't launch" report** (v2.2.1 and v2.3.0 bundles are unsigned identically — verify a suspected packaging regression by diffing the two tarballs' file lists + apphost size before blaming the build). Only `osx-arm64` gets signed: `osx-x64` is cross-published on `ubuntu-latest` where `codesign` doesn't exist (it takes the warning branch).
- **`release.sh` builds NOTHING locally** — it bumps/merges/tags/pushes, then waits on CI assets and updates the tap/winget mirrors. Every binary comes from `release.yml` on a clean `actions/checkout`, so "I ran the release from a different machine" can NEVER explain a bad artifact. Don't chase that theory; diff the release tarballs instead.
- **Telegram-style auto-update** (`Services/UpdateFlow`, stateful): check → **auto-download in background** → on ready, show a persistent "Update Downloader" button at the **bottom of the nav rail** (`MainViewModel.IsUpdateReady` + `ApplyUpdateCommand`) AND a **native** system notification (`NotificationService.Notify`, not an in-app toast). The swap is applied **on app exit** via `App.DesktopOnShutdownRequested → UpdateFlow.ApplyPendingOnExit()`, so clicking the button (which quits) OR just closing the app both install it; close-to-tray is bypassed when `UpdateFlow.IsReady`. Settings shows live download progress then a "Restart to update" button. **All `UpdateFlow` state changes marshal to the UI thread** — the old bug was calling quit from a background `ConfigureAwait(false)` continuation, which hung the window + close button. Disabled under snap (`UpdateFlow.IsManagedExternally` ⇐ `SNAP` env).
- **Snap** (`snap/snapcraft.yaml`, core22 + `extensions: [gnome]`, strict confinement): the part is `plugin: dump` over the **pre-published** `publish/linux-x64` self-contained single-file (built by `scripts/build-snap.sh` / `.github/workflows/snap.yml`), `organize: { Downloader: bin/Downloader }`; `stage-packages: libicu70, libssl3` (gnome ext provides the rest). Desktop+icon live in `snap/gui/`. **The Store / `snap info` icon needs a top-level `icon: snap/gui/downloader.png` key** (or a file literally named `snap/gui/icon.png`) — the `.desktop` `Icon=` line only sets the *launcher* icon, not the Store icon (fixed in `b5d00a1`). **The Store icon must be ≤512×512** — `snap/gui/downloader.png` is the 512×512 `Assets/downloader512.png` (the 1080×1080 `Assets/downloader.png` was rejected by the Store for size; fixed in `fb53047`). Version stamped from `snap/local/VERSION` via an `adopt-info` `nil` part. **Single-file extraction under strict confinement**: the single-file build self-extracts native libs (`libSkiaSharp.so` …) to `~/.cache/dotnet_bundle_extract` by default, but the `home` interface DENIES hidden dot-dirs like `~/.cache` → `Failure processing application bundle … Error code: 13` (EACCES) and the app never opens. Fix = `apps.downloader.environment.DOTNET_BUNDLE_EXTRACT_BASE_DIR: $SNAP_USER_COMMON/.dotnet_bundle_extract` (snapd expands `$SNAP_USER_COMMON`; always writable). Fixed in `b289fd8`. Snaps auto-update via the Store, so the in-app updater self-disables under `SNAP`. Publishing needs the author's `snapcraft login` (or a `SNAPCRAFT_STORE_CREDENTIALS` repo secret for CI auto-publish).
- **Browser-extension store packaging**: `scripts/build-extension.sh` makes two zips from `src/browser-extension` — Chrome/Edge (uses `manifest.json`) and Firefox (swaps in `manifest.firefox.json` as `manifest.json`). Listing copy + step-by-step submission in `PUBLISHING.md`; `PRIVACY.md` is the required privacy-policy URL. Store submission needs the author's dev accounts.
- **Queue page ScrollViewer**: set `HorizontalScrollBarVisibility="Disabled"` so cards stay within the viewport (they overflowed behind the window when narrow), and put the page padding on the inner content's `Margin` (NOT `ScrollViewer.Padding`) so the bottom gap is part of the scroll extent and the last row is reachable at scroll end.
- **Stop vs Pause a queue**: `PauseQueue` only pauses *running* items (the Queues-page Run/Pause toggle); `StopQueue` (toolbar "Stop queue") **cancels every** running/paused/queued item → Stopped. Two distinct manager methods.

## Packaging / publish
- **macOS must NOT use single-file/compression** (`PublishSingleFile`/`EnableCompressionInSingleFile`/`IncludeNativeLibrariesForSelfExtract`). On Apple Silicon the compressed bundle crashes the first time a managed assembly is loaded — `inflate` hits a "(Data Abort) byte write Translation fault" → `FailFastIfCorruptingStateException` → `abort()`/SIGABRT (seen as a crash on **Start download**, when the `Downloader` engine assembly is first bound). Fix: macOS publishes plain `--self-contained true` (loose assemblies + dylibs); `make-macos-app.sh` already copies the whole publish dir into `Downloader.app/Contents/MacOS`, so the apphost rename (`Downloader.Desktop`→`Downloader`) still works. Windows/Linux keep single-file+compression (they work). Applied in both `release.yml` and `scripts/publish.sh` (gated on `osx-*`).
- Self-contained, dependency-free single file: `dotnet publish -r <rid> --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true` (the last flag is required so Skia/native libs are bundled). Validated ~49 MB ELF.
- The output binary is `Downloader.Desktop[.exe]` (= project name). Renaming the *file* to `Downloader` is safe — `avares://Downloader.Desktop/...` uses the embedded assembly name, not the file name, so don't change `AssemblyName` (that WOULD break every avares URI).
- CI/release live in `.github/workflows/` (`dotnet-desktop.yml` = build+test on push/PR, `release.yml` = matrix publish on `v*` tag → **creates** the GitHub Release for the tag and attaches zip/tar.gz). Local: `scripts/publish.sh [rid ...]`.
- **winget**: the package identifier MUST be `Publisher.Package` (winget rejects a bare name), so it stays `bezzad.Downloader`. To let users run the short `winget install downloader`, add `Moniker: downloader` to the locale manifest — don't try to rename the identifier. **Submitting to `microsoft/winget-pkgs` (no `wingetcreate` on Linux):** fork (`gh repo fork`), sync the fork's `master` (`gh api -X POST repos/<you>/winget-pkgs/merge-upstream -f branch=master`), create a branch, PUT the 3 manifests under `manifests/b/bezzad/Downloader/<ver>/` via the Contents API, then `gh pr create --repo microsoft/winget-pkgs`. The PR auto-validates (Azure pipeline) then **waits on a community moderator** to merge; the CLA is already signed for `bezzad`. **`scripts/release.sh` does this automatically (`submit_winget`) each release and is dedup-safe** — ALWAYS check `gh pr list --repo microsoft/winget-pkgs --author @me` for an existing open PR before opening another (we once stacked a 1.1.0 + 1.3.3 dup; close the older one). Installer manifest = `InstallerType: zip` + `NestedInstallerType: portable` (the win zip ships `Downloader.exe` at its root).
- **`winget install bezzad.Downloader` "fails" on some machines — it's NOT the package.** Symptom: `Failed when searching source: msstore` + `SSL Error: WINHTTP_CALLBACK_STATUS_FLAG_CERT_REV_FAILED / INVALID_CA`, then winget LISTS `bezzad.Downloader` (source `winget`) and says "Please specify one of them using the --source option". winget found our package fine; it refuses to auto-pick because one source errored, so the match isn't provably unambiguous. The `msstore` SSL failure is the user's network/machine (corporate TLS inspection, VPN MITM, blocked CRL/OCSP endpoint). **Answer: `winget install bezzad.Downloader --source winget`** (skips msstore entirely). Confirmed working by the author, 2026-07-22. README + `packaging/winget/README.md` now document `--source winget` as the recommended form. Don't chase this as a manifest bug.
- **`packaging/winget/*.yaml` is a MIRROR** of `manifests/b/bezzad/Downloader/<ver>/` in winget-pkgs, not the source of truth; `release.sh submit_winget` bumps both. To check the mirror against what's actually published: `gh api "repos/microsoft/winget-pkgs/contents/manifests/b/bezzad/Downloader/<ver>/bezzad.Downloader.installer.yaml" --jq '.content' | base64 -d`.
- **Session gotcha: `git status` in the CLAUDE.md preamble is a SNAPSHOT taken at session start and `develop` may already be far ahead of it** (a session opened at `ab797a1` found `origin/develop` 31 commits later, incl. the whole v2.2.0 release). A fresh worktree branches from that stale base, so files can look out of date when they aren't. **Always `git fetch origin develop && git rev-list --left-right --count HEAD...origin/develop` before concluding something is stale or unreleased**, and rebase the worktree onto `origin/develop` before editing.
- **Release matrix RIDs**: `win-x64`, `linux-x64`, `osx-x64`, `osx-arm64` (only macOS ships both arches; Windows/Linux are x64-only). Versioning — `VersionPrefix` is the full 3-part semver in the csproj (currently `1.1.2`); `release.yml` overrides it from the tag (`-p:VersionPrefix=<tag-without-v>`). **To release**: bump `VersionPrefix` to the new 3-part version, ensure green + pushed, then `git tag vMAJOR.MINOR.PATCH && git push origin <tag>` (tag must equal `VersionPrefix`). NOTE this repo currently releases off `develop` (the working branch), not `main`. `softprops/action-gh-release` creates the Release if absent; re-running needs the tag + Release deleted first.

## Avalonia 12 gotchas worth caching
- **DataGrid cell focus/current border**: there is NO named `FocusVisual`/`CurrencyVisual` element in v12. The current/focus outline is on an unnamed template `Border`; kill it with `DataGridCell:current /template/ Border` + `DataGridCell:focus /template/ Border` → `BorderThickness=0`/`BorderBrush=Transparent` (and `Rectangle` Stroke for safety). `Focusable=False` does NOT remove it. Full-row selection highlight comes from the Fluent theme's `:selected` default.
- **DataGrid grouping kills row virtualization** → janky scroll/UI past ~10 rows. Keep the `DataGridCollectionView` flat (no `GroupDescriptions`) for performance.
- **`DataGrid.FontSize` does NOT cascade to cell/header content** — setting it on the `<DataGrid>` has no visible effect on rows. To resize cell text: set `FontSize` on each template-column `TextBlock`, set `FontSize` on `DataGridTextColumn` (it has its own), and/or add scoped styles `DataGrid.Styles` → `Selector="DataGridCell TextBlock"` and `"DataGridColumnHeader TextBlock"`.
- **Single-line `TextBox` strips newlines on paste** (`AcceptsReturn=false`), merging pasted multi-line input. For multi-URL paste, set `AcceptsReturn="True"` + a `KeyDown` handler that fires the action on Enter (Shift+Enter = newline).
- **To intercept Enter on an `AcceptsReturn="True"` TextBox you MUST use the TUNNEL phase, not a bubble `KeyDown=`.** The TextBox inserts the newline in its own bubble-phase key handler which runs *first* and marks the event `Handled`, so a bubble-phase XAML `KeyDown="…"` handler never fires (bug: "Enter just adds a new line" in the Add dialog's clipboard-suggestion accept). Fix = code-behind `box.AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel)` (tunnel runs root→target before the TextBox's bubble handler; set `e.Handled=true` there). Headless-testable: `view.Show()` + focus the box + `view.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r")` (from `using Avalonia.Headless;`); find named controls in tests via `GetVisualDescendants().OfType<T>().First(t => t.Name == "…")` — `FindControl<T>` is not available cross-assembly in Avalonia 12.
- **Reveal-a-file-in-folder** cross-platform: Windows `explorer /select,"path"`, macOS `open -R path`, Linux `dbus-send … org.freedesktop.FileManager1.ShowItems array:string:file://path string:` (fallback: open the directory). **For an in-progress row the final file doesn't exist yet** — the engine writes `<name>.download` — so `OpenContainingFolder` reveals the final file if present, else `<final>.download`, else just opens the folder (completed rows already selected correctly).
- **Only ONE modal on screen — a dialog opened from another dialog appears UNDERNEATH it.** Every modal here is shown with `view.ShowDialog(MainWindow)`, so a dialog opened from inside another one (Donate from About) is the first one's **sibling**, not its child; the shared owner raises the earlier dialog back on top and the new one looks like it opened behind. Do NOT "fix" this by re-parenting to `ActiveWindow` (that nests windows and makes Esc/close order confusing) — instead every modal entry point in `DialogHelper` calls **`BeginModal(view)`** before `ShowDialog`, which closes whatever modal is still open and tracks the new one (`CloseOpenModals()`/`OpenModals`). `Confirm` deliberately SKIPS `BeginModal` — a confirmation must sit on top of whatever asked for it, not close its caller. Any new modal added to `DialogHelper` must call `BeginModal`. Regression tests: `UI/DialogHelperTests.Opening_a_modal_closes_the_modal_that_was_already_open` (+2). Note headless can't exercise `ShowAbout`/`ShowDonate` themselves (no classic desktop lifetime ⇒ `MainWindow` is null ⇒ they early-return), so the tests target the `BeginModal` seam they all funnel through.
- **Custom window chrome**: `ExtendClientAreaChromeHints` was **removed in Avalonia 12** (compile error AVLN2000). Use only `ExtendClientAreaToDecorationsHint="True"` + `ExtendClientAreaTitleBarHeightHint="-1"`, then draw your own bar (see `Views/TitleBar`). OS resize/snap still works. Drag = `host.BeginMoveDrag(e)` on left-button `PointerPressed`; get the window via `TopLevel.GetTopLevel(this) as Window`.
- All three windows (MainWindow, AddDownloadItemView, DownloadDetailsView) use `TitleBar`; dialogs set `ShowMinMax="False"`.
- **`CanResize="True"` is NOT enough to edge-drag-resize these windows** — they set `WindowDecorations="None"` (+ transparent rounded chrome), which removes the OS resize border, so `CanResize` only drives maximize/restore. To get edge/corner dragging you MUST wrap the root border in a `<Panel>` and add `<v:ResizeGrips />` as the last child (an 8-zone transparent overlay in `Views/ResizeGrips`; it resizes manually via pointer-capture + `Window.Position/Width/Height` because `Window.BeginResizeDrag` is a **no-op on macOS** for borderless windows). MainWindow + DownloadDetailsView already had it; a resizable dialog WITHOUT `ResizeGrips` silently only maximizes (the `resizable-persisted-dialog-sizes` regression: Add-link + PageDialog were missing it). Any new custom-chrome resizable window needs this overlay.
- **Esc-to-close dialogs**: with `WindowDecorations="None"` there's no native close-on-Esc. Override `OnKeyDown` on the dialog window and `Close()` on `Key.Escape` (see `DownloadDetailsView`). A focused `TextBox` doesn't swallow Esc, so the window-level override is enough.
- **`{Binding #ElementName.Bounds.Width}` is unreliable inside an `ItemsControl`/`DataTemplate`** — the element-name reference silently fails to resolve per-item, so a control bound to it (e.g. `Width="{Binding #Sibling.Bounds.Width}"`) gets no width and **stretches to fill** instead of matching the sibling (bit the PluginsView catalog-row busy pill: the progress bar rendered full-width, not button-width). To make sibling controls share a width in a template, give them the same **explicit** `Width`, OR (see next) fix the *parent panel* width and let children stretch.
- **`ProgressBar` ignores an explicit `Width` that's smaller than the Fluent template's own minimum** — setting `Width="90"` on a `<ProgressBar>` did NOT shrink it (it still rendered ~200px wide) while the same `Width="90"` on a sibling `Button` worked. Fix: don't size the ProgressBar itself — put it in a fixed-width parent (e.g. the enclosing `StackPanel Width="90"`) and set the bar `HorizontalAlignment="Stretch" MinWidth="0"` so it fills exactly the parent width. (PluginsView busy panel: parent panel `Width="90"`, bar stretches to match the Add button.)

## Build / run / test
```bash
dotnet build Downloader.Desktop.sln                                   # 0 warnings / 0 errors expected
dotnet run  --project Downloader.Desktop/Downloader.Desktop.csproj    # launch the GUI (needs a desktop session)
# ALWAYS run the suite bounded (standing rule — see note below):
timeout -k 30 900 dotnet test Downloader.Desktop.Tests/Downloader.Desktop.Tests.csproj -v q --nologo \
  --blame-hang --blame-hang-timeout 180s --blame-crash   # all tests (Unit/ Integration/ UI/ Plugins/ + Plugins/Hls/)
```
**Test runs MUST be bounded (learned the hard way, 2026-07-16):** a `dotnet test` host can hang/die silently
right after "A total of 1 test files matched" and sit alive for HOURS; every subsequent `dotnet test` then
contends with it (same bin/obj + MSBuild node locks) and freezes at the same spot — looking like "tests
hang" when it's really a stale sibling process. Three layers, all standing: (1) every test attribute carries
`Timeout = TestTimeouts.DefaultMs` (60s; `TestTimeouts.SlowMs`=180s for genuinely slow ones — the port-range
binder needs it on macOS CI ~1m17s), see `TestSupport/TimedAttributes.cs`; (2) `--blame-hang
--blame-hang-timeout 180s --blame-crash` makes VSTest kill+dump a stuck host and NAME the culprit test;
(3) `timeout -k 30 900` hard-caps the whole command. Before re-running a "hung" suite, first
`pkill -f "dotnet test"; pkill -f testhost` — a leftover host is the usual cause of the next freeze.

**Root cause of the in-host hang itself (diagnosed from the hang dump, 2026-07-17):** xunit.v3 ran test
COLLECTIONS in parallel; 8 workers from 8 classes sat blocked in `AvaloniaTestCase.Run` awaiting the shared
headless dispatcher while NO dispatcher thread existed anymore — a parallel-collection race on the suite's
shared statics killed the session thread, and per-test `Timeout` can't fire when the dispatcher that would
run the test is dead. Fixed by `[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]` in
`TestSupport/TestAppBuilder.cs` — parallelism bought nothing (AvaloniaFacts serialize through the one
dispatcher; the suite runs in seconds). Don't re-enable it. Analyze future hang dumps with
`dotnet-dump analyze <dmp> -c pstacks`; the in-flight tests are the `Completed="False"` rows in the blame
`Sequence_*.xml`.
Headless smoke check (no display interaction): `timeout 10 dotnet run --project Downloader.Desktop/Downloader.Desktop.csproj` — a clean 10s run (SIGTERM/143) with no exceptions means it launched OK. Note: empty-list startup does NOT exercise row/file-kind icons.

## Regenerate README screenshots
A gated headless test renders real PNGs to `docs/screenshots/` (home-dark, home-light, settings-dark):
```bash
DLDESKTOP_CAPTURE=1 dotnet test Downloader.Desktop.Tests/Downloader.Desktop.Tests.csproj --filter FullyQualifiedName~CaptureScreenshots
```
Then verify the PNGs by viewing them. Capture uses the real `App` with `.UseSkia().UseHeadless(UseHeadlessDrawing=false)`.

## Architecture (where things live)
- `Services/DownloadManager` (DI singleton `IDownloadManager`): owns the master `ObservableCollection<DownloadItemViewModel>`, builds engine `DownloadService` instances, coalesces engine progress onto the UI via the shared `EnsureUiPump` `DispatcherTimer` (see perf note above), enforces queue concurrency through `PumpQueue`, runs the `DispatcherTimer` scheduler, raises `StatsChanged`/`ListChanged`.
- `ViewModels/`: `MainViewModel` (nav rail, filters, status bar, autosave, sidebar collapse), `DownloadsViewModel` (filterable `DataGridCollectionView` + multi-select bulk actions), `DownloadItemViewModel` (live row: progress/speed/status/FileKind/error), `QueuesViewModel`, `SchedulerViewModel`, `SettingViewModel` (full engine options), `AddDownloadItemViewModel` (multi-URL), `DownloadDetailsViewModel` (per-connection parts).
- `Models/`: `Config` (persisted), `DownloadSettings` (all engine options + `ToConfiguration()`), `DownloadItem`, `DownloadQueue`, `DownloadSchedule`.
- `Views/` axaml + `Converters/FileKindToIconConverter`. App-wide styles/theme palettes in `App.axaml`; icon geometries in `Assets/Icons.axaml`.
- Persistence: `Services/FileService` → JSON at `%AppData%/Downloader/config.json` (Linux `~/.config/Downloader`). Saved on shutdown + autosaved every 20s.

## Conventions / gotchas
- **Filename auto-resolve**: pass only URL+folder to the engine when the user gives no name; read the resolved name from `DownloadStartedEventArgs.FileName` (NOT `IDownload.Filename`, which stays empty).
- **DataGrid bindings**: `DataGridTextColumn.Binding` must use `{ReflectionBinding ...}` (compiled bindings resolve against the page VM); template columns set `x:DataType`.
- **New icon geometries** are parsed at runtime — validate by adding to the converter/icons and relying on the headless geometry tests, or by viewing a screenshot.
- **Tests**: xUnit **v3** (don't add v2); `[assembly: AvaloniaTestApplication]` is in namespace `Avalonia.Headless`; the test csproj must be `SelfContained=true` with `RuntimeIdentifier=$(NETCoreSdkPortableRuntimeIdentifier)` because the app project is self-contained.
- **Shutdown save** uses `.Wait()` on the UI thread — keep `ConfigureAwait(false)` on the save path to avoid deadlock.
- Keep each commit green (build + tests). Commit messages end with the Co-Authored-By line. Don't reference other download-manager apps anywhere — this is an original design.

See `CLAUDE.md` at the repo root for product vision, locked decisions, and the full roadmap.

## Snap publishing (done — `downloader` is live on the Snap Store)
- **Store name `downloader` is registered** to bezzad (public); `latest/stable` carries the release. Publisher login: `snapcraft whoami` (token expires 2027-06). Verify: `snapcraft status downloader` / `snap info downloader`.
- **Do NOT build the snap locally on this dev box with `--destructive-mode`**: the host is Ubuntu **26.04 (resolute)** but the snap targets `base: core22` (22.04). Destructive mode fetches stage-packages (`libicu70`, `libssl3`) from the *host* archive, which 26.04 doesn't have → "Stage package not found: libicu70". A real build needs an isolated core22 env (LXD/multipass, both need sudo) **or** just use CI.
- **Easiest publish path = reuse the CI-built `.snap`**: the `Snap` workflow (`.github/workflows/snap.yml`) builds correctly via `snapcore/action-build` (clean core22) and uploads a `downloader-snap` artifact on every v* tag. To publish: `gh run download <id> -n downloader-snap` then `snapcraft upload --release=stable downloader_<ver>_amd64.snap` (uses the local login; ~processing 1-2 min → "released to 'stable'").
- **CI auto-publish caveat (fixed)**: the publish step runs only on `refs/tags/`. The v1.3.1 run built+uploaded the artifact but **died at "Attach snap to the GitHub Release"** (`Resource not accessible by integration` — default GITHUB_TOKEN can't update a Release another workflow created), which skipped "Publish to the Snap Store". Fixed by `continue-on-error: true` on the attach step + `always() && …` on the publish step. Re-running an OLD tag run won't pick up the fix (tag runs use the workflow at the tag commit) — the fix applies to the next tag. The `SNAPCRAFT_STORE_CREDENTIALS` repo secret is already set (from `snapcraft export-login`).
- **Never commit `snap-creds.txt`** (the export-login token) — gitignored, along with `*.snap` and `parts/ stage/ prime/`.
- **In-app updater self-disables under snap** (`SNAP` env) — the Store handles updates.

## Round 18 patterns (themes/accent, selection, tray threading, numeric, flags)
- **Accent picker (Light/Dark + accent)**: `Services/ThemeService` holds `Accents` (Teal/Blue/Purple/Green/Amber) and `ApplyAccent(key)` overrides the Fluent accent **color** resources at the **Application** level — `SystemAccentColor` + `SystemAccentColorLight1/2/3` + `Dark1/2/3` (shades computed by mixing toward white/black) — which beats the per-theme palette `Accent` and recolors every `{DynamicResource SystemAccentColor}` consumer (nav selection, accent buttons, links, pill) in both themes. `ThemeService.Apply(config)` sets the variant + accent together; call it at startup (`MainViewModel`) and on Reset. Persisted as `DownloadSettings.AccentColor`. Status colors stay semantic (NOT accent-driven). Selected accent VM: `SettingViewModel.SelectedAccent`/`Accents` with a swatch+name ComboBox (`AccentOption.Brush`).
- **Selected-row contrast (#row-select)**: the Fluent default fills a selected DataGrid row with the SOLID accent → dark text becomes unreadable. Fix in `App.axaml` `DataGridCell:selected`: `Background={DynamicResource RowSelectionBrush}` (a translucent accent, alpha ~0.28, kept in sync by `ThemeService.ApplyAccent`) + `Foreground={DynamicResource SystemBaseHighColor}` (normal text). Verify by programmatically selecting `grid.SelectedIndex=1` in CaptureScreenshots (a headless *click* only reads as hover) → `home-selected-{dark,light}.png`.
- **Tray/relaunch "does nothing" = wrong thread**: `SingleInstanceService.Dispatch` runs on the background TCP accept thread and `TrayIcon.Clicked` can fire on a DBus thread; calling `window.Show()/Activate()` off the UI thread silently no-ops (and the off-thread throw can wedge the tray event pipeline, plausibly killing the right-click menu too). Both now marshal via `Dispatcher.UIThread.Post` (Dispatch wraps `_onMessage`; `TrayService.ShowWindow` wraps its body + a topmost flip). Linux tray menu behavior is DE-specific and NOT verifiable headlessly — keep both `_tray.Menu` (right-click) and `_tray.Clicked→ShowWindow` (left-click); the menu's "Open" is the reliable restore.
- **NumericUpDown empty → null crash**: clearing a `NumericUpDown` sets `Value=null`, which a binding to a non-nullable `int`/`long` setting can't convert → a "value cannot be null" validation error in the view. `Behaviors/NumericCoerce.EmptyToMinimum` (attached prop, enabled globally by a `NumericUpDown` style in App.axaml) snaps an empty box back to its `Minimum`. Covers Settings/Queues/Details numerics at once.
- **Interrupted downloads load as Stopped**: `DownloadManager.Initialize` normalizes BOTH saved `Running` AND `Paused` → `Stopped` (a live/paused server connection can't survive a restart). Terminal states (Completed/Failed/Stopped) are kept.
- **Language flags**: `Assets/flags/{code}.png` (auto-embedded by `AvaloniaResource Include="Assets\**"`), generated by a one-off PIL script (no SVG rasterizer on this box). `LanguageOption.Flag` lazy-loads the bitmap via `AssetLoader` (null if missing). Picker ComboBox shows `<Image Source="{Binding Flag}">`+name. Mapping: en→US, pt→Brazil, ar→**UAE**, eo→Esperanto star, **fa→green/white/red Iran tricolor WITH a simplified gold Lion & Sun emblem** (this line previously said "no emblem" — that was stale/wrong; the shipped asset has always carried the emblem, see the "Round 19" flag note below — never the official Sun&Lion/takbir version, just a crude sun-disc+lion silhouette). New i18n keys only strictly need `en.json` (others fall back to English).

## Round 19 patterns (queue-stuck bug, update redesign, flags, banner, release notes)
- **"Start just queues, never downloads" (1.3.2/1.3.3 regression)**: `DownloadQueue.IsRunning` is **persisted** and `PumpQueue` early-returns when `!IsRunning`. Since every per-item start (`Resume`/`Retry`, row button, bulk) funnels through `PumpQueue`, a queue saved with `IsRunning=false` (after Stop-queue / Pause-queue) silently swallows all starts — items sit as `Created` ("Queued") until the scheduler's `StartQueue` flips `IsRunning=true`. Fix: an explicit user start calls `EnsureQueueRunning(queueId)` (sets `IsRunning=true`) before `PumpQueue`. Don't remove the `IsRunning` gate from `PumpQueue` itself — completion's `TryStartNextInQueue` still needs it so a paused queue doesn't auto-advance. Regression test: `Start_runs_item_even_when_its_queue_was_paused`.
- **Removing a queue deactivates its schedules**: `RemoveQueue` now disables (`Enabled=false`) + unbinds (`TargetQueueId=null`) any `Schedules` pointing at it, so the scheduler can't act on a deleted target. Test: `Removing_a_queue_deactivates_its_schedules`.
- **Auto-update is now user-initiated (was Telegram-style silent auto-download)**: `UpdateFlow` gained an `Available` state + `PromptUpdate` callback (wired in `MainViewModel` → `DialogHelper.ShowUpdatePrompt`). `CheckAsync` no longer auto-downloads — it raises `Available` and shows the in-app **`UpdatePromptView`** (Download / Later, modeled on `ShutdownView`: Topmost, Esc=Later). `StartDownloadAsync()` runs only on Download (so the Settings progress bar is actually seen). Settings button flows Check → Download update (`Available`) → Restart to update (`Ready`). The "invisible progress" was because the old flow downloaded silently at startup.
- **Update restart relaunch**: the Unix swap script (`UpdateService.WriteUnixScript`) now `trap '' HUP` + relaunches via `setsid` (fallback `nohup`) so the NEW app runs in its own session and isn't torn down with the old process group — that detachment is what makes "restart to update" actually relaunch. (Self-swap still can't be tested headlessly.)
- **Flag rendering (PIL, ~30×20)**: real 5-point `star()` polygons (not dots) for the US canton; Korea taeguk = full red circle → blue bottom-semicircle → left small circle red + right small circle blue (the S-curve) + 4 corner trigram bars — without the two small circles it looks like Japan's disc. Iran (per author's reversal) = green/white/red + a **simplified gold Lion & Sun** emblem on the white stripe (sun disc+rays + crude lion silhouette); a detailed emblem isn't legible at this size.
- **Flags are SVG now (`Assets/flags/{code}.svg`), NOT PNG** (author request 2026-07-04, superseding the PIL/PNG notes above): hand-authored vector SVGs (viewBox 90×60), rasterized at load time by `Localizer.RenderSvg` via the **`Svg.Skia` 5.1.1** package — the Avalonia-independent core (`Avalonia.Svg.Skia` has NO Avalonia-12 build, but plain `Svg.Skia` only needs SkiaSharp ≥3.119.2 and Avalonia.Skia 12.0.4 ships 3.119.4). `LanguageOption.Flag` stays a `Bitmap` so the XAML is unchanged: `SKSvg.Load` → scale to 45px height (3x the 22×15 display) → `SKSurface` → PNG-encode → Avalonia `Bitmap`. **`fa.svg` embeds the author's provided Lion & Sun (Naval flag of Iran) JPEG** as a base64 `<image>` data URI (resized 360×240) — do NOT redraw it procedurally; SKSvg renders embedded raster images fine. eo now has the correct GREEN Esperanto star (the old PNG had red); az the real 8-point star. Preview SVGs on macOS with `qlmanage -t -s 300 -o <dir> <files>`. Test `Every_language_has_a_loadable_flag` asserts non-null + 45px height for all 16.
- **README `docs/banner.svg`**: embed the real app icon as a base64 **`data:image/png`** `<image>` (SVG2 plain `href`, NOT `xlink:href`) so GitHub renders it — don't hand-draw a stand-in logo. Source PNG = `Assets/downloader512.png`.
- **Release notes MUST be pretty Markdown** (CLAUDE.md release routine): one-line summary + emoji section headers (`### ✨ New` / `### 🐛 Fixes` / `### 🔧 Under the hood`), short end-user bullets, no commit hashes. Backfill empty/plain releases with `gh release edit <tag> --notes-file`. Good examples on GitHub: v1.0.0 / v1.1.0 / v1.2.0. **CI gotcha:** never put `generate_release_notes: true` on `release.yml`'s matrix `action-gh-release` steps — 4 concurrent creates race → `tag_name already_exists` and one asset (osx-x64) fails to upload (bit v1.4.0). A single post-build `notes` job fills auto-notes only if the body is empty instead.

## Plugin system (Phase 1 — foundation) — patterns
- **SDK assembly:** `src/Downloader.Desktop.Plugins.Abstractions` (net10.0, nullable on) holds ONLY interfaces + POCO types — the stable surface external plugins reference. App + tests reference it via ProjectReference. Design: `docs/plugins-architecture.md`. Pipeline = **Resolve (`ILinkResolver`) → Transfer (`ITransferProvider`/`ITransfer`) → Post-process (`IPostProcessor`)**; a plugin implements only the phases it needs (`IDownloaderPlugin.Initialize(IPluginContext)` registers contributions).
- **Loader:** `Services/PluginManager` (DI singleton, UI-free → unit-testable). Loads each plugin DLL in a collectible `AssemblyLoadContext` + `AssemblyDependencyResolver`. **Critical:** the load context MUST return `null` for the `Downloader.Desktop.Plugins.Abstractions` assembly name so it resolves from the host → shared type identity (else `IsAssignableFrom`/`is IDownloaderPlugin` fails). `AssemblyDependencyResolver` method is `ResolveAssemblyToPath` (not `ResolveAssemblyPath`). Only ENABLED plugins' contributions are returned by `FindResolver/FindPostProcessor/FindTransferProvider/ResolveAsync`. Disabled ids persist in `Config.DisabledPlugins`. Plugins live in `PluginManager.PluginsRoot` (`~/.config/Downloader/plugins`).
- **Plugin projects** set `<EnableDynamicLoading>true</EnableDynamicLoading>` (emits the deps.json the ADR needs) and reference Abstractions with `<Private>false</Private><ExcludeAssets>runtime</ExcludeAssets>` (don't ship a 2nd SDK copy). Example: `samples/Downloader.Desktop.SamplePlugin`.
- **TDD:** `PluginTests.cs` (plain `[Fact]`, in-process fakes) covers register/route/enable-disable/idempotency/safe-missing-dir, PLUS a real external-DLL load — the test csproj builds the sample plugin and stages its DLL into `<testout>/plugins-sample` (MSBuild `StageSamplePlugin` target + `ReferenceOutputAssembly=false` ProjectReference), and the test asserts `LoadFromDirectory` loads it. This validates the ALC + shared-SDK identity end-to-end.
- **UI:** `Views/PluginsView` + `ViewModels/PluginsViewModel` — nav (MANAGE) → Plugins; lists installed plugins (name/version/author/description + enable toggle), Install (file picker → copy DLL to PluginsRoot → reload) + Open-folder. `DialogHelper.OpenFilePicker(title,filterName,ext)` added.
- **NOT YET (Phase 2):** the download-pipeline integration (`JobCoordinator` + multi-part download + the `ITransfer` refactor so the queue/UI drive torrent/HLS uniformly) and the official HLS (yt-dlp+ffmpeg)/torrent plugins. `PluginManager.ResolveAsync` is the ready hook.

## Round 20 patterns (no-left-nav UI, dialogs, update fixes, expired-link, plugin docs)
- **NO left nav rail; pages open IN the main window (2026-07-10, superseding the earlier page-dialog model).** `MainWindow`'s central `ContentControl` binds `MainViewModel.CurrentPage`; the three Show*Commands + `ShowDownloadsCommand` call `Navigate(NavSection.X)` (no dialogs — `PageDialogView` + `DialogHelper.ShowPage` + `PageDialogWindowKey` were DELETED). The **action toolbar lives in `MainWindow`** (docked under the top bar, visible on every page): bulk buttons bind through `Downloads.*` (e.g. `{Binding Downloads.StartSelectedCommand}` — null-safe until init, refreshed by `RaisePropertyChanged(nameof(Downloads))`), nav buttons bind directly and highlight the current page via `Classes.selected="{Binding Is*Selected}"` + the `Button.tool.selected` style in App.axaml (translucent `RowSelectionBrush` fill + accent icon/text). The toolbar is a `Grid "Auto,*,Auto"`: **page nav is pinned LEFT** (Grid.Column=0) — Home (icon-only `HomeRegular`, tooltip `Nav_Downloads` — key in all 16 packs), then Settings, Scheduler, Queues (icon+label); the **downloads-list action cluster** (Start/Pause/Stop/Remove + queue buttons) is **pinned RIGHT** (Grid.Column=2) in a StackPanel `IsVisible="{Binding IsDownloadsSelected}"` so it only shows on the Downloads page. The `*` middle column is the spacer. RTL mirrors both sides correctly. The Add-link dialog closes on **Esc** (same `OnKeyDown` override as DownloadDetailsView; the inline queue-name editor's own Esc handler wins because it marks the event handled first). Only Add-link, Details, About, Update-prompt remain separate windows. Plugins stay a collapsible `Expander` in `SettingView`.
- **Capture gotcha:** management pages render inside MainWindow — in CaptureScreenshots just `vm.ShowSettingViewCommand.Execute(null)` (etc.) then `Save(window, …)`; return with `vm.ShowDownloadsCommand`. No page dialog to Show/Close anymore.
- **Auto-update macOS restart loop:** the old swap extracted `Downloader.app` INTO `Contents/MacOS` (nesting) and relaunched the OLD binary → re-detect → loop. `UpdateService.WriteMacScript` now replaces the whole `.app` bundle (`appDir`=`<bundle>.app/Contents/MacOS` → bundle = `appDir/../..`) and relaunches via `open`. NOTE: an update FROM a buggy build still uses that build's broken swap — the fix only helps updates initiated from a fixed build (tell users to manually install once to break the loop). Unverifiable headless.
- **Update cancel + version:** `UpdateFlow.CancelDownload()` (CancellationTokenSource in `DownloadAsync`; cancel → back to `Available`). Settings shows a × (`DismissRegular`) on the progress bar + `AvailableVersionText` ("vX.Y.Z available", from `UpdateFlow.AvailableTag`).
- **Expired-link detection:** `DownloadManager.Start` captures `vm.PreAttemptSize = item.Downloaded>0 ? item.Size : null` (the known size BEFORE progress events overwrite `vm.Size`, which writes through to `_item.Size`). On a successful completion, `ExpiredLinkHeuristic(known, finalBytes)` (pure, tested) flags a RESUME that finished at <half the known size as Failed ("re-add with a fresh link") instead of Completed. `finalBytes` from `(e.UserState as DownloadPackage)?.ReceivedBytesSize ?? vm.Download?.Package?.ReceivedBytesSize`.
- **Stop All vs Stop icon:** were identical (`StopRegular` square). Stop All now uses `StopAllRegular` (a solid octagon stop-sign). Added `DismissRegular` (×) too.
- **Plugin developer docs:** `docs/writing-plugins.md` (how to write/build/install a plugin). The sample `samples/Downloader.Desktop.SamplePlugin` is now **GitHub Releases** (`com.bezzad.github-releases`) implementing ALL interfaces (resolver = repo→latest asset; `IPostProcessor` = .sha256 sidecar; `ITransferProvider` = file:// copier). The integration test loads it and checks the GitHub resolver + file:// transfer.

## Plugin SDK refinements (naming + logging)
- **`IMediaResolver` → `ILinkResolver`, `MediaPart` → `DownloadPart`** (author: the app downloads any file, not just media). `PartKind` (Combined/Video/Audio/Segment/Subtitle) stays — it's a post-processing hint, default `Combined`.
- **Logging is `Microsoft.Extensions.Logging.ILogger`, NOT a custom `Log(string)`.** `IPluginContext.Logger` is an `ILogger` (the SDK references `Microsoft.Extensions.Logging.Abstractions`). `PluginManager`'s context builds it via `AppLog.Factory.CreateLogger($"plugin:{id}")`. CLAUDE.md Conventions now mandates ILogger everywhere (engine/app/plugins → one log via `AppLog.Factory`).

## UX batch 4 patterns (footer filters, plugin install feedback, settings sizing, corrupted-resume)
- **Footer status pills double as the list filter.** The orphaned `StatusFilter` enum + `Show{All,Active,Queued,Completed,Failed}Command` + `Is*Selected` flags + `*FilterCount` (left over from the removed nav rail) are now reused by clickable footer buttons in `MainWindow.axaml` (`Button.filterpill` style + `Classes.active="{Binding Is*Selected}"`; accent fill when active). Filters are **disjoint**: `Active`=Running/Paused, `Queued`=Created/None, `Completed`, `Failed`=Failed/Stopped, `All`. Counts (`ActiveFilterCount` etc.) match each bucket exactly and are re-raised in `OnStatsChanged`/`RaiseNavFlags`. To add a filter: enum value + `DownloadsViewModel.Matches` case + command/flag/count + footer button.
- **File pickers must parent to `DialogHelper.ActiveWindow`, not `MainWindow`.** Management pages (Settings/Queues) are modal dialogs; opening a picker from the background MainWindow opens behind the modal / fails on some Linux WMs. `ActiveWindow` = `AppLifetime.Windows.LastOrDefault(w => w.IsActive) ?? MainWindow`. `OpenFilePicker` now uses it.
- **Plugin Install must give feedback** (it silently swallowed failures → "nothing happened"). `PluginsViewModel.InstallAsync` now diffs `_manager.Plugins` before/after and shows an always-on in-app toast: installed plugin name / "no plugin in that file" / the exception. Use `NotificationService.Inform(title,msg,isError)` (added) — it always shows the in-app toast regardless of the notifications on/off switch (direct action feedback). Also copies the `.deps.json` sidecar next to the DLL so plugins with their own deps resolve (the sample has none, so a bare DLL still loads — proven by `Loads_a_real_external_plugin_DLL_from_disk`).
- **Settings double-border:** wrap nothing extra around an `Expander` — the Plugins section was `Border.card` > `Expander` (two borders); a bare `<Expander>` matches the Advanced section. Right-hand inputs: `ComboBox.ctrl`/`TextBox.ctrl` set `Width=148 Height=34 MinHeight=34` to line up with the global `NumericUpDown` (`MinHeight=34`, `.ctrl` Width 148). Proxy is a single-line `Grid ColumnDefinitions="Auto,*"` (label left, box fills) not a label-over-box StackPanel.
- **`ExpiredLinkHeuristic` → `LooksCorruptedAfterResume`** (author spec): a first-time download finishing small is FINE (never flagged — `PreAttemptSize` is null when `item.Downloaded==0`); only a RESUME (`PreAttemptSize>0`) that finishes **smaller than the known size** is flagged → Failed with a "file looks corrupted / incomplete" message (threshold is `< knownSize`, not `< knownSize/2`). Same `PreAttemptSize` capture + `ReceivedBytesSize` read as before.

## Drag-to-reorder rows (main grid)
- **6-dot grip = first column** of `DownloadsView` (left of the select-all checkbox). Dragging a row reorders the **master `Items`** list = queue pump priority (same ordering the Queues-page chevrons drive). Dropping onto a row in **another queue moves the dragged item into that queue** (adopts the target's `QueueId`).
- **Manager**: `DownloadManager.ReorderTo(vm, target, placeAfter)` (on `IDownloadManager`) — `Items.Move` to the drop index (decrement when source was above target), adopt `target`'s `QueueId` if different (+ `vm.RaiseQueueNameChanged()`), `NotifyList()`, then `PumpQueue` the old and new queues. Keep `MovePriority` (±1) for the Queues page. `DownloadsViewModel.Reorder(...)` just forwards to the manager (code-behind calls it).
- **Row VM**: `DownloadItemViewModel.QueueName` is computed live from `_manager.Queues` by `_item.QueueId` (no manual sync); `RaiseQueueNameChanged()` re-raises it after a cross-queue move.
- **Queue column**: shown only when `DownloadsViewModel.ShowQueue` (`Queues.Count > 1`, re-raised in `Refresh()`). **An `x:Name` on a `DataGridColumn` does NOT generate a code-behind field** (CS0103) — find it instead via `Root.Columns.FirstOrDefault(c => c.SortMemberPath == "QueueName")` and set `IsVisible` from code-behind (subscribe to the VM's `PropertyChanged` for `ShowQueue`).
- **Avalonia 12 drag-drop API changed**: `DataObject`/`DragDrop.DoDragDrop`/`DragEventArgs.Data` are **obsolete**. `DataTransfer`/`DoDragDropAsync` exist but the **OS drag session renders NO moving visual on Linux/X11** — the row never appears to follow the cursor. **We no longer use OS DragDrop here** (see "Sticky drag ghost" below); only `ReorderTo`/`Reorder`/`placeAfter = e.GetPosition(row).Y > row.Bounds.Height/2` are kept.
- Adding the grip column shifted the overlaid select-all checkbox: its `Margin` went `14 9 0 0` → `42 9 0 0` (28px grip + 14px).

## Sticky drag ghost (manual pointer drag — replaces OS DragDrop)
- **Why**: Avalonia's OS `DragDrop.DoDragDropAsync` shows no moving adorner on X11, so the picked-up row didn't visibly follow the pointer. The fix is a hand-rolled pointer-capture drag with a floating ghost — this is the only way to get "row sticks under the cursor" here.
- **Overlay**: a `<Canvas x:Name="DragOverlay" IsHitTestVisible="False" ClipToBounds="False" />` is the last child of the `Panel` wrapping the DataGrid (so the ghost paints above rows).
- **Ghost** = a `Border` (opaque `SystemRegionColor` bg, accent border, `BoxShadows.Parse("0 6 18 0 #50000000")`, Opacity ~0.92, `IsHitTestVisible=False`) whose `Child` is a `Border{ Background = new VisualBrush(sourceRow){ Stretch=None, AlignmentX=Left, AlignmentY=Top } }` — i.e. a live snapshot of the dragged `DataGridRow`. Size it to `sourceRow.Bounds`.
- **Flow** (all in `DownloadsView.axaml.cs`, grip Border wires `PointerPressed/Moved/Released/CaptureLost`):
  - Press: find source `DataGridRow`, add `dragging` class, build the ghost, `Canvas.SetLeft/Top` it to the row's position **translated into `DragOverlay` space** (`row.TranslatePoint(new Point(0,0), DragOverlay)`), record `_ghostGrabY = e.GetPosition(row).Y`, `e.Pointer.Capture(grip)`.
  - Move: `Canvas.SetTop(ghost, e.GetPosition(DragOverlay).Y - _ghostGrabY)` (keep X fixed); highlight the row under the pointer via `Root.InputHitTest(e.GetPosition(Root))` → `FindAncestorOfType<DataGridRow>(includeSelf:true)` → `droptarget` class (skip the dragged row itself).
  - Release: `RowAt(pointer)` → if a different row, `pageVm.Reorder(_dragRow, target, placeAfter)`; then `ClearDrag()` (remove ghost from overlay, drop both classes, null fields), `e.Pointer.Capture(null)`.
  - CaptureLost: also `ClearDrag()` (covers Esc / focus steal).
- **Gotcha**: `VisualBrush(sourceRow)` re-renders the row's *live* appearance — if you also dim the source with `Opacity`, the ghost dims too. We keep the source's `dragging` class subtle for that reason; don't crank source opacity down.
- `DataGridRow.droptarget`/`.dragging` styles still live in `DownloadsView.axaml` `DataGrid.Styles`.

## Drag-reorder follow-up (cursor, highlight, queue label visibility)
- **`AddQueue` MUST `NotifyList()`** — without it the main grid's `DownloadsViewModel.ShowQueue` is never re-raised, so the per-row **Queue column stays hidden** even after adding a 2nd queue (RemoveQueue already notified; AddQueue didn't — that was the "I can't see the queue name" bug).
- **Drag cursor** = `Cursor="SizeNorthSouth"` (vertical ↕) on the grip Border (not `SizeAll`).
- **Queue shown in the details dialog**: `DownloadDetailsViewModel.HasQueue` (= `Item.QueueName` non-empty) gates the queue display. It's **inline on the Status line** (one row): `Status 62% · Stopped | Queue main` — a multi-column Grid with a thin `Border` separator + `Det_Queue` label + `Item.QueueName`, all three gated by `HasQueue`, with Size/Speed still right-aligned. (Was a separate line below; author wanted it on the status line.)

## UX batch 5 patterns (focus-aware notifications, copyable toasts, add-dialog resolve, expired link, sample plugin)
- **Avalonia here is 12.0.4, NOT 11.x** (csproj wins over `Directory.Build.props`'s `AvaloniaVersion=11.0.2`). Two 12-specific gotchas hit this batch: (a) **`IClipboard.SetTextAsync` moved to an extension** `Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(this IClipboard, string)` — `using Avalonia.Input.Platform;` or it won't compile. (b) The rich toast overload `WindowNotificationManager.Show(object content, NotificationType, TimeSpan?, Action onClick, Action onClose, string[] classes)` **does** exist in 12 — pass a custom `Control` as `content` to get a fully custom toast (the card hosts your visual; `type` still colors it, `onClick` makes the card clickable).
- **Clipboard READ in Avalonia 12**: there is NO `IClipboard.GetTextAsync()` — the read method is the extension `Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(this IClipboard)` (returns `Task<string>`, null if nothing/denied). `IClipboard` itself only exposes `SetDataAsync/TryGetDataAsync/TryGetInProcessDataAsync/ClearAsync/FlushAsync`; text/bitmap/file helpers are all `ClipboardExtensions` (`SetTextAsync`, `TryGetTextAsync`, `TryGetBitmapAsync`, …). Same extension class as the write path.
- **Focus-aware notification routing** (`NotificationService`): one channel per message by focus. `AppFocused` (default true = gentler in-app when unknown) is set from `window.Activated/Deactivated` wired in `MainViewModel.SetupAppShell()` (`SetFocused(window.IsActive)` + the two events). `Notify` → focused: in-app; unfocused/tray: native OS (fallback to in-app only if no native channel, e.g. Windows). **All passive callers already funnel through `Notify`/`NotifyCompleted/Failed/AllCompleted`**, so routing is centralized — don't add per-call channel logic. Hiding to tray fires `Deactivated` (IsActive=false) so tray messages correctly go to the OS.
- **Actionable prompts when unfocused** (`ShowAction`, e.g. update available): send a plain OS notification now AND enqueue `(title,message,onClick)`; `SetFocused(true)` flushes the queue by re-showing the clickable in-app toast — so the action is never lost. Focused ⇒ in-app actionable immediately.
- **Copyable/custom in-app toast** = `BuildToast(...)` returns a `Border` (severity stripe) → `StackPanel` with `SelectableTextBlock` title+message (selectable satisfies "let me copy the text") + a **Copy** button (`_topLevel.Clipboard.SetTextAsync($"{title}: {message}")`) + an optional action button. The action button (not card-`onClick`) handles `ShowAction` so it doesn't conflict with the in-content Copy button.
- **Add-dialog single-link name/size pre-resolve** (`AddDownloadItemViewModel`): on `Urls` change, `TriggerResolve()` — debounce `Task.Delay(600, cts.Token)` then `UrlResolver.ResolveFileInfoAsync(url, Settings.ToConfiguration())` → `RemoteFileInfo` (**`.FileName` string + `.FileSize` long**, FileSize≤0 = unknown). Per-keystroke `CancellationTokenSource`; only apply if the input is still that exact single link and `_userTypedName` is false (the `Filename` setter tracks user typing so resolve never clobbers it; resolve writes `_fileName` via `RaiseAndSetIfChanged(ref _fileName, …, nameof(Filename))` to bypass the setter). `IsFilenameEnabled = !IsMultiple` disables (not hides) the name box for multi-link adds; size hint row is `IsVisible=IsSingleLink` + `Resolving`/`HasSizeText`. Unknown size → **engine downloads single-part natively** (no app code needed — RangeDownload needs a known size + range support).
- **Expired/anti-bot link detection** (`DownloadManager`): pure `LooksExpiredOrInvalid(string head, long bytes)` — flags a "completed" download that's **small (≤512KB) AND looks like markup** (head trimmed/lowercased starts-with `<!doctype html`/`<html`/`<?xml` or contains `<head`/`<body`/`<title`). Wrapper `IsExpiredOrInvalidLink` sniffs the first 1KB of the saved file (path from `(e.UserState as DownloadPackage)?.FileName`); checked in the success `else` branch of `DownloadFileCompleted`, before marking Completed → sets Failed + `Localizer.Instance["Error_LinkExpired"]`. Sits alongside `LooksCorruptedAfterResume`/`LooksAlreadyDownloaded` (same pure-helper + completion-branch pattern). Genuine small non-markup files are not flagged.
- **Sample plugin "not a Downloader plugin"** root cause was **the sample wasn't in `Downloader.Desktop.sln`** → `dotnet build` never produced a fresh DLL, so users installed a stale/absent one. Fix = `dotnet sln add ../samples/Downloader.Desktop.SamplePlugin/...csproj`. The loader + source were already current (the green `Loads_a_real_external_plugin_DLL_from_disk` test, which builds+stages the sample via the test csproj `StageSamplePlugin` target, proves it). If "install does nothing/errors" recurs, suspect a stale DLL on disk, not the SDK.

## UX batch 6 patterns (queue menu refresh, combo padding, name tooltip)
- **`MenuFlyout.ItemsSource` must be an `ObservableCollection` mutated in place — a computed property + `PropertyChanged` does NOT refresh it.** `DownloadsViewModel.StartQueueTargets`/`StopQueueTargets` feed the toolbar "Start/Stop queue ▾" flyouts. First attempt (computed `=>` list + re-raise `PropertyChanged` on a new `QueuesChanged` event) **did not work** — a `MenuFlyout` lives in a separate popup tree, materializes its items once, and ignores a `PropertyChanged` re-read while closed, so a newly-added queue only appeared after an app restart. **Working fix:** make the two properties get-only `ObservableCollection<QueueActionTarget>` and rebuild them *in place* (`Clear()` + `Add(...)`) in a `RebuildQueueTargets()` called from the ctor and from the `QueuesChanged` handler — the flyout honors `CollectionChanged` across open/close. Keep `event Action QueuesChanged` on the manager (interface + impl), raised at the end of `AddQueue`/`RemoveQueue`. Run the rebuild inline when `Dispatcher.UIThread.CheckAccess()` (synchronous from the Queues dialog button and deterministic in headless tests), else `Post`. **General rule: any menu/flyout `ItemsSource` that changes at runtime must bind to an `ObservableCollection`, not a recomputed `IEnumerable`.**
- **Global ComboBox padding**: the app's `Style Selector="ComboBox"` only set `Cursor=Hand`, so selected text sat flush on the edge. Add `Padding="10 6"` to it AND a matching `Style Selector="ComboBoxItem"` (so dropdown rows are inset too). One global style in `App.axaml` covers every combo (Settings language picker, Add dialog, Scheduler).
- **Full-name-on-hover for trimmed grid cells**: the Name cell keeps `TextTrimming="CharacterEllipsis"`; bind `ToolTip.Tip` to a VM `NameTooltip` (= `DisplayName`, plus `"\n{ErrorMessage}"` when `HasError`) rather than to `ErrorMessage` alone. **Gotcha:** a tooltip binding only updates when the source raises `PropertyChanged`, so `NameTooltip` must be re-raised at *every* site that raises `DisplayName` (4 places in `DownloadItemViewModel`, incl. `OnLanguageChanged`) **and** in the `ErrorMessage` setter — else the tooltip stays stuck on the initial "Fetching…" value after the name resolves.

## CI gotcha — sample-plugin staging must be RID-agnostic (PluginTests flake)
- **Symptom**: `PluginTests.Loads_a_real_external_plugin_DLL_from_disk` fails on CI in **Release** (win/mac), intermittently, with `"sample plugin was not staged — check the test csproj target"`. Local `dotnet test` (Debug) never shows it.
- **Cause**: the test csproj `StageSamplePlugin` target copied the sample DLL from a **hardcoded non-RID path** `samples/.../bin/$(Configuration)/net10.0/Downloader.Desktop.SamplePlugin.dll`, with `Copy Condition="Exists(...)"`. The test project is `SelfContained` with a `RuntimeIdentifier`; CI's `dotnet build <sln>` builds the sample BOTH as a solution project (non-RID path) AND as the test's self-contained ProjectReference (RID subfolder `net10.0/<rid>/`) — a parallel-build race. When the sample landed only under the RID subfolder, the non-RID source didn't exist, the `Condition="Exists"` **silently skipped** the copy → green build, `plugins-sample/` never created → runtime test fail. `dotnet test --no-build` can't re-stage, so it stays broken.
- **Fix** (`Downloader.Desktop.Tests.csproj`): glob the DLL recursively — `Include="$(SamplePluginBin)\**\Downloader.Desktop.SamplePlugin.dll" Exclude="$(SamplePluginBin)\**\ref\**"` (the `ref/` exclude matters — those are metadata-only assemblies that throw on load) — so it stages wherever the sample lands, and add an `<Error Condition="'@(SamplePluginDll)' == ''">` so a genuine miss is a **loud build failure**, not a silent skip + confusing test failure. Verified: simulating RID-only output still stages; 141/141 green in both Debug and Release. **General rule: never hardcode `bin/$(Configuration)/net10.0/` for a self-contained/RID project's output — glob across the optional RID subfolder.**

## Plugin boundary NOW has a consumer (github.com/owner/repo download fix)
- **The bug**: the plugin SDK + `PluginManager` loader + sample GitHub-Releases resolver all worked, but `DownloadManager` **never called any resolver** — `Start` only did `UrlResolver.ResolveAsync` (HTTP redirects). So a pasted `github.com/owner/repo` link downloaded the HTML page, not the release asset. The resolver itself was correct; the app just had no consumer.
- **Fix**: `DownloadManager` takes an optional `PluginManager` (greediest-ctor DI picks it; the registered singleton is the same instance `MainViewModel` loads plugins into, so resolvers are present by download time) and calls `public ResolveViaPluginsAsync(url, fileName, ct)` at the top of `Start`'s off-thread work, before `UrlResolver.ResolveAsync`. If an **enabled** resolver `CanResolve(url)`, the link is rewritten to `plan.Parts[0].Url` and `SuggestedFileName` is used (only when the user didn't type a name). No-match / no-manager / resolver-exception all pass the link through unchanged. **Only the first part is downloaded** — multi-part/transfer/post-process plans (HLS, torrent) need the not-yet-built job coordinator and are logged.
- **Testing a loaded-DLL plugin**: the test project references the sample only at runtime (`ReferenceOutputAssembly=false`), so you can't compile-call its internals. Cover `CanResolve` through `pm.FindResolver(url)` (pure, no network), and put the full `ResolveAsync` (hits api.github.com) behind a `DLDESKTOP_NET=1` env gate so CI/offline skip it — run it locally to verify the real repo resolves. App-side logic (`ResolveViaPluginsAsync`) is unit-tested with an in-process fake plugin via `PluginManager.RegisterPlugin`.
- *Known cosmetic follow-up*: the Add dialog name preview (`UrlResolver.ResolveFileInfoAsync`) still probes the github HTML page so it may show no/odd name before the download starts — the actual download is correct. Wire plugin resolution into the preview later if desired.

## Multi-part plan runner (Phase 2 — HLS/video assemble, `DownloadManager.Plans.cs`)
- **Now the app runs the WHOLE plan, not just the first part** (superseded the "first part only" note above). `Start` calls `ResolvePlanAsync` (full `DownloadPlan`); if the plan `NeedsRunner` (>1 part OR a post-process) it's persisted to `DownloadItem.PlanJson` (`Models/PersistedPlan.cs`, a JSON-safe copy of the SDK plan — the SDK types are init-only + read-only-collection so they DON'T round-trip through STJ, hence the DTO) and run via `RunPlanAsync` → `ExecutePlanAsync`. A single-part `PostProcess.None` plan keeps the legacy engine path (zero regression).
- **`ExecutePlanAsync` is UI-FREE and `internal`** (InternalsVisibleTo the test project) so it's unit-testable without the dispatcher/queue: it takes callbacks (`onPartService`/`onStage`/`onProgress`/`isCancelled`) instead of touching the vm. `RunPlanAsync` is the thin VM wrapper that marshals those to the row via `OnUi`. When `_config` is null it falls back to `new DownloadConfiguration()`, so tests can `new DownloadManager()` with NO Initialize + a loopback server (see `PlanRunnerTests`).
- **Parts** download sequentially, each via its own `DownloadService` (item settings + per-part `Headers` applied to `RequestConfiguration.Headers`) into `<folder>/.<final-name>.parts/NNNN_<safe-name>`. **Completion detection has TWO checks**: `IsPartComplete` (restart-skip: size-match, else a `.done` marker) vs `PartDownloadedOk` (post-download: exists + size-match-or-non-empty). Don't merge them — the marker is written AFTER the post-download check, so using the marker-requiring check right after download wrongly fails unknown-size parts.
- **Pause/resume/cancel reuse the per-row `vm.Download`**: each part's engine is published to it, so the manager's guarded Pause/Resume/Cancel act on the current part. **Engine `Pause()` suspends the awaited `DownloadFileTaskAsync` (doesn't complete it)**, so the runner loop just blocks through a pause — no special handling. Cancel sets Status=Stopped → the part task returns → the runner sees Stopped, deletes the parts folder, returns null. A failed part is detected via the completion event's `e.Error` (the engine does NOT throw from `DownloadFileTaskAsync` on failure — same gotcha as UpdateFlow).
- **Assemble**: `PluginManager.FindPostProcessor(plan.PostProcess)` → `ProcessAsync` to `<final>.assembling` → atomic move → delete parts. Missing processor → Failed (`Plan_NoProcessor`). Multi-part with `PostProcess.None` → raw binary concat. **Retry clears `PlanJson`** so Start re-resolves (expiring signed segment URLs); matching completed parts on disk are reused. Progress: byte-weighted when all parts have `ExpectedSize` (reserve last 10% for assembly), else parts-done/total. Status text via `DownloadItemViewModel.PlanStage` ("Part i/N" / "Assembling…"). **Still needs the author's in-app e2e** with the real HLS plugin (ffmpeg/yt-dlp + GUI) — can't verify headlessly.
- **HLS perf + ffmpeg-naming fixes (author's live e2e found these)**: (1) `IsSingleChunkPart` — Segment/≤8 MB parts get `ChunkCount=1, ParallelDownload=false` (never 8 engine chunks per tiny segment); segment-only plans run **4 segments in parallel** (`SemaphoreSlim`, index-ordered assembly; mixed/big-part plans stay sequential). (2) **ffmpeg refuses an output without a standard extension**: temp assembling path must keep the extension LAST — `AssemblingPath` gives `video.assembling.mp4` (the old `finalPath+".assembling"` caused `Unable to choose an output format`, exit 234). (3) `NormalizeAssembledName` — a post-processed plan's final name never keeps `.m3u8`/`.m3u`/empty (→ `.mp4` or the plugin's suggested ext); the row `FileName` is synced to the normalized name.

## Tray notifications (macOS) + plugin removal patterns
- **macOS shows in-app toasts from the tray** because hiding a window to the tray on macOS does NOT fire the window's `Deactivated` event, so the focus-only routing kept `AppFocused=true`. Fix: route on **visibility too**, not just focus. `NotificationService.PreferOsChannel(bool appFocused, bool? windowVisible) => !appFocused || windowVisible == false` (pure, unit-tested); `Notify`/`ShowAction` use `InAppVisible = !PreferOsChannel(AppFocused, (_topLevel as Window)?.IsVisible)` — in-app only when a window is on screen AND focused, else OS (with the existing in-app fallback for platforms w/o a native channel, e.g. Windows). Also call `NotificationService.SetFocused(false)` right after every `window.Hide()` (close-to-tray + `--minimized` start) so the event state is correct too. General rule: notification channel = "is a window visible & focused", not focus alone.
- **Removing/uninstalling a plugin**: `PluginLoadContext` is already `isCollectible:true`, but `LoadedPlugin` used to DISCARD the ALC (ctor param named `_`) and never stored the DLL path — so removal was impossible. Now `LoadedPlugin` keeps `Alc` + `SourcePath` (passed through `RegisterPlugin(plugin, alc, sourcePath)`, set from the dll path in `LoadFromDirectory`). `PluginManager.RemovePlugin(id)`: remove from `_plugins` under the lock (stops contributions at once) → `Alc.Unload()` → best-effort delete the DLL + sidecar `.deps.json` (retry once after `GC.Collect()`+`WaitForPendingFinalizers` for Windows file locks; if still locked, leave for next launch + log). On Linux/macOS deleting a mapped file just works. UI: `PluginRowViewModel` gets a `RemoveCommand` wired to a `RemoveRow` callback the page passes in (refresh list + `NotificationService.Inform` toast); trash button (`DeleteRegular` icon, `SystemErrorTextColor`) in `PluginsView`. **Correction (was stale):** Plugins i18n is NOT English-only — all 16 language packs carry the `Plugins_` keys. Per the sync-all-languages rule, add new `Plugins_` keys to **all 16** packs with real translations, not just `en.json`.

## Local API + CLI (issue #2) — patterns
- **`Services/LocalApiService`** (renamed from BrowserIntegrationService, same 15151 listener + toggle): legacy `/add?url=`+`/ping` unchanged (incl. CORS `*`); new `/api/add|list|pause|resume|cancel|retry|remove` return JSON and deliberately send NO CORS headers (no auth token by author decision — a web page can POST but not read). Handlers marshal onto the UI thread with `await Dispatcher.UIThread.InvokeAsync(...)` and return real outcomes (201/400/404). `LocalApiService.Manager`/`.Config` are wired in `MainViewModel.SetupAppShell`. Pure helpers (`ApiAddRequest.FromJson/FromQuery`, `BuildItem`, `QueryParam`, `ExtractIdFromJson`) carry the tests.
- **Port is a RANGE now, not a constant**: `LocalApiService.PortRange = 15151–15155` (`PreferredPort=15151`); `Start()` binds the first free one (persisted last-known-good port first, via `DownloadSettings.LocalApiPort`), exposes `EffectivePort` (0 = not running). There is NO `LocalApiService.Port` anymore — tests/CLI use `EffectivePort`/`PortRange`. Why a small fixed range and never "any free port": **MV3 `host_permissions` are static/install-time**, so the extension can only ever reach pre-declared origins — both manifests list all 5 ports × {127.0.0.1, localhost}. `common.js` discovers the live port by probing `/ping` across the range (cached `appPort` in extension storage first — `discoverAppPort(probe, cachedPort)` is exported + unit-tested). CLI (`CliRunner.ResolveCandidatePorts`) reads the persisted port from the config file, then probes the rest. Settings shows a read-only "Local API address" row + green/gray dot (`SettingViewModel.LocalApiAddress/LocalApiStatusText/LocalApiStatusBrush`); a one-time fallback notification fires in `MainViewModel.SetupAppShell` when `EffectivePort != PreferredPort`. Note: once fallen back, the app *stays* on the persisted fallback port on later restarts even if 15151 frees up (by design — stability over reclaiming the default).
- **CLI = same binary, verb-first** (`CliParser.TryParse` pure; `CliRunner` executes): parsed in `Program.Main` BEFORE Avalonia and BEFORE `SingleInstanceService.TryClaim` (a `list` must not steal the lock). `add` forwards `add:{json}` over the single-instance lock channel (`SingleInstanceService.LockPort`, now 15150) via `SingleInstanceService.TryForwardAdd`, or spawns the app detached with `--cli-add <json>`; `TryForward` also translates `--cli-add` args so a spawn race still delivers the payload. `list`/control talk HTTP to the API (15151, or its fallback in 15151–15155 — `CliRunner.ResolveCandidatePorts` probes the range; need toggle on). Windows: `AttachConsole(-1)` P/Invoke in CliRunner (WinExe has no console).
- **Toggle default flipped ON + one-time migration**: `Config.SchemaVersion` (0 = pre-field) — `EnsureValid` flips `EnableBrowserIntegration=true` when `SchemaVersion<1` then stamps `CurrentSchemaVersion`; a post-migration user "off" is respected. Pattern to reuse for future default flips.
- **Extension silent add**: `common.js sendToApp(url, filename)` consults `api.storage.local` `addMode` (default `silent` → `GET /api/add`, fall back to `/add?url=` ONLY on 404 — a 200 from an older app already opened the dialog, retrying would double-add). Popup footer checkbox "Add silently (no dialog)"; `storage` permission added to BOTH manifests.
- **xunit attribute gotcha (CS0182)**: `[InlineData(new[] {...})]` / `new string[] {...}` for a `string[]` theory param does NOT compile — attribute args can't do the array-into-params-object[] conversion. Use a plain `[Fact]` looping over `new[] { new[] {...}, ... }`.
- **Headless HTTP-vs-dispatcher deadlock**: an `[AvaloniaFact]` that awaits an HttpClient call whose server handler does `Dispatcher.UIThread.InvokeAsync` deadlocks (test thread IS the UI thread). Pattern: run HTTP via `Task.Run`, spin `while (!task.IsCompleted) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }` (see `LocalApiEndToEndTests.Pump`).
- **Screenshots are Ubuntu-rendered**: re-running `CaptureScreenshots` on macOS rewrites ALL PNGs (font rendering differs platform-wide) — do NOT commit those; regenerate only on the Linux box.
- **Scrolled captures: never grab `OfType<ScrollViewer>().FirstOrDefault()`.** Every `ComboBox` (and several other templated controls) contains an internal `PART_ScrollViewer`; in `SettingView` one of those sorts first, so setting `.Offset` on it was silently clamped to 0 and the `settings-accent-*` shots were **byte-identical duplicates** of the unscrolled `settings-*` shots for a long time without anyone noticing. Select the page scroller by "actually scrollable" (`Extent.Height > Viewport.Height`, largest overflow first), then `ScrollTo(sv, y)` (sets Offset + pumps layout). To frame a specific control, locate it and compute `sv.Offset.Y + control.TranslatePoint(default, sv).Y - margin` — **`BringIntoView()` does NOT exist on `Button` in Avalonia 12**. Always `md5sum` a new scrolled PNG against the unscrolled one to prove the scroll actually happened.

## macOS "Restart to update does nothing" (v1.5.0) — root cause + forensics pattern
- **Bug**: clicking Settings → "Restart to update" on macOS did nothing; app stayed on the old version. Settings is a MODAL (`DialogHelper.ShowPage` → `ShowDialog(MainWindow)`), and `Quit()` closed the OWNER window while the modal's nested native session was still running — macOS swallows the shutdown, `DesktopOnShutdownRequested` never fires, so `UpdateFlow.ApplyPendingOnExit` never writes the swap script. **Fix**: `MainViewModel.Quit` closes `window.OwnedWindows` first, then the main window. Any future quit-path change must keep this (quit is reachable from inside modals).
- **Forensics that pinned it** (reusable): the swap script `$TMPDIR/downloader-update-<pid>.sh` is never self-deleted — its ABSENCE proves `ApplyDownloadedArchive` never ran (vs. ran-and-failed). The downloaded archive `$TMPDIR/Downloader-<rid>.tar.gz` is only deleted by the swap script — absent + still-old-version + no script ⇒ exit hook never reached. Homebrew is NOT a blocker: the cask installs a normal user-owned `/Applications/Downloader.app` (verified the engine download to `Path.GetTempPath()` works standalone).
- **UpdateFlow latent gap** (not yet fixed): it assumes `DownloadFileTaskAsync` THROWS on failure, but the engine reports failure via `DownloadFileCompleted(e.Error)` and completes the task normally — a failed update download can leave a partial archive that gets marked Ready. If flaky-update reports appear, subscribe to the completion event / verify archive integrity before `Raise(Ready)`.


## Notch overlay ("dynamic island", `NotchService`/`NotchView`/`NotchViewModel`)
- **Opt-in** (`DownloadSettings.EnableNotch`, default off; Settings toggle under the tray toggle; live start/stop). A separate borderless window (`SystemDecorations=None`, `Topmost`, `ShowInTaskbar=false`, `ShowActivated=false`, `Focusable=false`, transparent bg) with a dark pill body rounded on the BOTTOM corners only (notch look). Collapsed 170×34 shows clock + ↓total-speed chip; hover expands to 380×190 with top-3 running rows (thin progress bars) + "and N more…"; `PointerExited` collapses after a ~300 ms `DispatcherTimer` grace (skim-past no-flicker). Click → `TrayService.ShowWindow()`. Runs independent of the main window (stays up in close-to-tray).
- **Positioning**: `NotchView.Reposition()` — horizontal center of `Screens.Primary.WorkingArea` (width × `screen.Scaling` → pixels), `Y = screen.Bounds.Y` (top of the SCREEN, not the work area — on macOS that tucks the pill right under the menu-bar/notch line; a normal window cannot draw INSIDE the notch without private APIs). Re-run on every expand/collapse since the width changes.
- **One stat at a time (author's spec, BOTH modes)**: the clock shows only when idle — collapsed via `ShowCollapsedClock` (`!IsMac && !HasActivity`), expanded via `IsVisible="{Binding !HasActivity}"` on the header TimeText (speed gets `HasActivity`). When anything downloads, speed replaces the time everywhere; never show both.
- **VM**: 1 s clock `DispatcherTimer`; `StatsChanged` → rebuild `RunningRows` ONLY on membership change (the row VMs self-update progress/speed); `IDisposable` unhooks timer+event. Mockups: gated test `CaptureNotchMockups` (`DLDESKTOP_NOTCH_MOCKUP=1`) renders collapsed/expanded PNGs via `CaptureRenderedFrame` into the openspec change folder.

## Plugin consolidation — built-in vs. optional/catalog tiers (consolidate-official-plugins)
- **All first-party plugin source now lives in THIS repo.** The former separate `bezzad/Downloader.Plugins` repo (HLS) was consolidated in as a clean copy of its `develop` tip (v1.1.2, already carried the x.com + YouTube fixes), renamed `Downloader.Plugins.Hls` → `Downloader.Desktop.Plugins.Hls` (plugin id `com.bezzad.hls` unchanged). The old repo is deleted by the author only after confirming this works.
- **Two tiers, both under `src/Downloader.Desktop.Plugins/`:**
  - **Built-in** (`GitHub`, `Ollama`): staged into the app output by the app csproj's `StageBundledPlugins`/`...OnPublish` targets; disable-only; update with the app.
  - **Optional/catalog** (`Hls`, future Torrent): in the solution for build/test ONLY. The app has **no `ProjectReference`** to them and never stages them. They ship as release assets and install on demand.
- **Isolation gotcha (the important one):** the staging targets MUST be an **explicit per-plugin allow-list**, NOT a wildcard over `..\Downloader.Desktop.Plugins\*`. A `*` glob would sweep an optional plugin (same parent folder) into the app bundle — the exact thing the tier split prevents. Guarded by `PluginIsolationTests` (app csproj never references the optional assembly — comment-stripped so explanatory comments can still name it) + a `release.yml` `dotnet publish` grep. Safe-failure direction: forget to list a new built-in → it's simply unbundled (harmless); the reverse (wildcard grabbing an optional) is the bug.
- **MSBuild gotchas hit here:** (1) an item transform `@(Item->'...**...')` does NOT re-glob `**`/`*` — it yields a literal path. Use `<PropertyGroup>` dir vars + semicolon-separated glob `Include="$(A)\**\x.dll;$(B)\**\x.dll"` (globs in `Include` DO evaluate). (2) The app is `SelfContained` so `$(OutDir)` includes the RID — staged plugins land in `bin/<cfg>/net10.0/<rid>/plugins`, NOT `bin/<cfg>/net10.0/plugins`. (3) `AfterTargets="Build"` targets don't run on an incremental up-to-date build; use `-t:Rebuild`/`--no-incremental` to actually exercise staging.
- **Release/catalog:** `scripts/build-plugins.sh` (run by the `plugins` job in `release.yml`, on `vX.Y.Z`) builds each optional plugin, zips dll+deps.json, sha256s it, and generates `plugins-catalog.json` (static fields from `packaging/plugins/optional-plugins.json`; version from the plugin csproj `<Version>`). Both attach to the SAME app release. `dist/` is gitignored.
- **Version single-source:** `HlsPlugin.Version` derives from the assembly (`typeof(HlsPlugin).Assembly.GetName().Version`, Major.Minor.Build) so the runtime-reported version and the catalog version (from csproj `<Version>`) can't drift → the update check never loops. Bump the plugin by editing its csproj `<Version>` only.
- **App side:** `Services/PluginCatalogService` (sibling to `UpdateService`, same GitHub `releases/latest` call) fetches + parses the catalog and resolves asset URLs; `PluginManager.InstallFromZipAsync` **verifies sha256 BEFORE extract/load** (hard gate, friendly error on mismatch, nothing touched); `InstallOrUpdateAsync` = download→verify→load (RemovePlugin-first for update swaps). Settings → Plugins shows a "More plugins" catalog list (Add) + per-row Update badge; `MainViewModel.CheckPluginUpdatesAsync` runs at startup alongside the app self-update and shows an actionable toast (never auto-updates). Tests install into a TEMP root (internal overload) so the real `~/.config/Downloader/plugins` is never touched.
- **Test project structure (single consolidated project, foldered by category):** there is ONE test project, `Downloader.Desktop.Tests`, organized into folders whose namespaces match: `Unit/` (`…Tests.Unit`, pure logic), `Integration/` (`…Tests.Integration`, loopback/engine/API-CLI/e2e), `UI/` (`…Tests.UI`, Avalonia headless + `CaptureScreenshots`), `Plugins/` (`…Tests.Plugins`, all plugin tests) with `Plugins/Hls/` (`…Tests.Plugins.Hls`) for the HLS plugin, and `TestSupport/` (`…Tests`, kept at root ns — hosts `[assembly: AvaloniaTestApplication]`/`TestAppBuilder`). **No loose `.cs` at the project root** — put a new test in the folder that fits and use the matching sub-namespace. The HLS tests were folded IN here (the former separate `Downloader.Desktop.Plugins.Hls.Tests` project is gone) — they recompiled unchanged under xUnit v3 (only basic `[Fact]/[Theory]/Assert` APIs). This also fixed a latent gap: that separate project was never in CI's `Test_Project_Path`, so its 62 tests never ran in CI; now they do.
- **Gotchas from that consolidation:** (1) the merged project needs `<ImplicitUsings>enable</ImplicitUsings>` — the HLS tests were authored against implicit usings (`System`, `System.Threading.Tasks`, …); existing tests keep explicit usings (implicit+explicit coexist). (2) The `Plugins/Hls` sub-namespace SHADOWS the unqualified `Plugins.` shorthand: a test in `…Tests.Integration` writing `Plugins.PostProcessKind` now resolves to `…Tests.Plugins` (wrong) → fully-qualify as `Downloader.Desktop.Plugins.PostProcessKind`. (3) HLS test files lost parent-namespace access to the plugin's types, so they need an explicit `using Downloader.Desktop.Plugins.Hls;`. (4) The HLS plugin's `InternalsVisibleTo` is now `Downloader.Desktop.Tests` (tests call its `internal` `SiteExtractor`/`ExtractionResult`); the plugin is a normal **compile** `ProjectReference` from the test project (its DLL lands in test output for `PluginLoadTests`' host-mirroring ALC load). Referencing an OPTIONAL plugin from the TEST project is fine — the isolation rule (`PluginIsolationTests`) only bars the APP project. (5) HLS fixtures are copied to `<testout>/Fixtures` via a `<None Include="Plugins\Hls\Fixtures\**\*"><Link>Fixtures\%(RecursiveDir)…` item (where `TestFixtures.Dir` reads them).
- **Speed limit — global vs per-item (fix-ux-reliability-batch §4):** the engine's `DownloadConfiguration.MaximumBytesPerSecond` normalizes `0` → `long.MaxValue` (both mean unlimited) — so when reading it back, treat `v <= 0 || v == long.MaxValue` as "unlimited" (see `DownloadDetailsViewModel.SpeedLimitKb`). A per-item cap set in the details dialog persists via `DownloadItem.HasCustomSpeedLimit`/`CustomSpeedLimitBytesPerSecond` (write-through on the VM like `Status`); `DownloadManager.Start` applies it over the global `Settings` value when the flag is set, so it survives stop/resume + restart. Global changes live-apply through `DownloadManager.ApplyGlobalSpeedLimit(bytes)` (skips items with a custom cap); the Settings speed setter calls it, mirroring the `MaxConcurrentDownloads` → `DefaultQueue` sync pattern. "Use global limit" button (`UseGlobalSpeedLimit()`) clears the flag and re-applies the current global.
- **Manual window resize math (fix-ux-reliability-batch §5):** `WindowEdge` lives in **`Avalonia.Controls`**, NOT `Avalonia.Input`. The resize geometry is now a pure static `Views/WindowResize.Compute(...)` anchored to a **press-time snapshot** (pointer in screen/device px via `Window.PointToScreen`, window `Position`, `Bounds` W/H) — every frame recomputes from that fixed anchor plus the current pointer's screen-space delta. Do NOT re-read the window's live (moving) `Bounds`/`Position` per frame: that's what let West/North drags compound error and walk the window off-screen. `WindowResize.ClampOnScreen(...)` is a last-resort guard keeping the window overlapping some screen's working area. The math is unit-tested (`Unit/WindowResizeTests`) via the property "N tiny frames == one big delta"; a real multi-frame OS drag can't be headless-simulated, so edge/corner dragging still needs manual author verification (task 5.6).
- **YouTube / video-site cookie hand-off (fix-hls-youtube-resolver):** diagnosis (gated `YtDlpDiagnosisTests`, `DLDESKTOP_NET=1`) found YouTube's anonymous extraction hits a bot-check ("Sign in to confirm you're not a bot") and `--cookies-from-browser` **hangs on the macOS keychain gate** (Chrome 127+ app-bound encryption is the Windows equivalent). Fix = the browser extension hands the app a live session's cookies, which yt-dlp reads via `--cookies <file>` (never the on-disk store). Flow: extension `chrome.cookies.getAll({url})` for the exact sent URL → POST `/api/add` JSON `cookies` field → `LocalApiService.BuildItem` writes `CookieFile.WriteTempFile` (Netscape format, chmod 600) → `DownloadItem.CookieFilePath` (**`[JsonIgnore]`, transient — cookies are secrets, never persisted/logged**) → `DownloadManager.ResolvePlanAsync(url, ct, cookieFilePath)` → `ResolveOptions.CookieFilePath` → `HlsResolver.ResolveAsync(url, options, ct)` → `IYtDlp.ExtractJsonAsync(url, cookieFilePath, ct)` (tries `--cookies` FIRST, falls back to anonymous → browser loop). Temp file deleted in `DownloadManager.Start`'s `finally` (`DeleteCookieFile`). To exercise the fix in the gated test, set `DLDESKTOP_COOKIES=<netscape file>` too.
- **Non-breaking plugin-SDK extension pattern:** to add an optional arg to an `ILinkResolver`/`IYtDlp` call without breaking external plugins or test stubs, add a **default-implemented interface overload** (C# 8 DIM) that delegates to the existing method — e.g. `ResolveAsync(url, ResolveOptions, ct) => ResolveAsync(url, ct)`. Only the resolver that needs it overrides it; `PluginManager`/`DownloadManager` call the new overload.
- **Browser-extension test harness gotcha:** `common.js` binds `const api = globalThis.browser || globalThis.chrome` **at load**, so a test that needs `chrome.*` must set `global.chrome = { … }` BEFORE `require("./common.js")` (mutate its sub-objects per-test since `api` holds the same reference). `captureCookies` must never throw — it returns `[]` on any failure so a cookie-capture problem never blocks sending the URL. Cookies go out as a **JSON POST** to `/api/add` (a GET query can't carry them); the URL-only GET path is unchanged when there are no cookies.
- **e2e flakiness:** the Playwright media-detection specs (`hls-and-quality.spec.js`, `relevance.spec.js`) are timing-sensitive under headless load and can fail on a first full run, then pass on a targeted re-run — re-run the specific spec before treating a failure as a regression. **`npx playwright test --workers=1`
  makes the whole suite green** (7/7 here) where the default parallel run failed 7/7 — the specs contend for
  the shared persistent-context Chromium, so run them serially rather than chasing individual flakes. Also
  note `npm test` in that folder can print `sh: playwright: not found` right after a fresh `npm install`;
  `npx playwright test` works. `dotnet test`'s `HlsResolverTests.ResolveAsync_follows_master_to_best_variant` (loopback server) is similarly flaky under parallel load.

## Plugin binary dependencies MUST self-heal (YouTube "some videos fail" root cause, 2026-07-11)
- **Symptom**: YouTube extraction fails with `Requested format is not available` (or the misleading "requires a signed-in browser session") while other videos work. **Cause**: the plugin-install-time dependency fetch was interrupted → a TRUNCATED `deno.zip` (never extracted; without deno yt-dlp can't solve the n-challenge → no real formats) and/or an unfinished ffmpeg `.download` sidecar — and NOTHING retried them. Compounding it: `YtDlpBinary`/`FfmpegBinary`'s internally-created `HttpClient` had the default **100 s whole-body timeout**, which truncates a ~45 MB binary on a slow link every single attempt.
- **Fixes (keep all four)**: (1) `PluginDependencyInstaller.EnsureAllAsync` — if the destination archive exists, try `FinishInstallAsync` first (complete-but-unextracted case), else delete + re-download; (2) `MainViewModel.EnsurePluginDependenciesAsync` — background re-ensure of enabled plugins' missing deps at every startup (resumes interrupted engine downloads via the `.download` sidecar); (3) plugin binaries use `Timeout.InfiniteTimeSpan` (ct governs) and DELETE partial/corrupt archives on any failure; (4) `YtDlpBinary.MissingFormats(stderr)` — deno==null + "Requested format is not available" → a clear "Deno component isn't installed yet" error.
- **Diagnosis shortcut**: check `~/.config/Downloader/plugins/data/com.bezzad.hls/{deno-bin,ffmpeg-bin,yt-dlp-bin}` — a lingering `.zip`/`.download` file with no extracted binary = this bug. yt-dlp with cookies but no deno passes the bot check and STILL fails (format-not-available), so don't misread it as a cookie problem.
- Verified live on `youtube.com/watch?v=m4e0lTMUPAk`: anonymous → bot check; `--cookies-from-browser chrome` (works on this box) + deno → 33 real formats; full in-app download + ffmpeg mux OK.
- **YouTube 360p cap (fixed 2026-07-11)**: `SiteExtractor`'s "prefer progressive" policy capped every YouTube download at 360p (its only progressive combined format is format 18). Now progressive wins ONLY when no strictly taller split video-only stream (with an audio-only partner) exists; otherwise fall through to HLS (YouTube premuxed m3u8 reaches 1080p) or video+audio mux. x.com unchanged (its progressive IS full quality — the `LikelyCombined` codecless case). To live-test a plugin change: build, `cp` the dll+deps.json into `~/.config/Downloader/plugins/com.bezzad.hls/`, restart the app (the app loads the INSTALLED copy, not the repo build). NOTE: `pkill -f "Downloader.Desktop"` matches the invoking bash's own command string — it kills your own shell (exit 144); use a distinct pattern or pkill by exact binary.
- **YouTube's "Sign in to confirm you're not a bot" is PER-VIDEO, not per-machine/per-binary (diagnosed 2026-08-13 on `youtu.be/8uiKr3U71RE`).** Don't chase it as a stale-yt-dlp or blocked-IP bug: on the same box and second, `dQw4w9WgXcQ` extracted anonymously while `8uiKr3U71RE` (a long ambient-music upload) failed the bot check on **every** `youtube:player_client` (`default/tv/tv_simply/web_safari/mweb/android/android_vr/ios/web_embedded/tv_embedded/web_music/web_creator`), with `formats=missing_pot`, with stable 2026.07.04 AND nightly 2026.08.04, and with freshly-minted anonymous visitor cookies (`curl -c` on youtube.com — those do NOT help). YouTube's own signature for it is `WARNING: No title found in player responses`. The only two answers are (a) real browser-session cookies or (b) a PO-token provider (`bgutil-ytdlp-pot-provider` — needs Node/Deno + npm deps incl. native `canvas`; NOT integrated, would be a sizeable new bundled dependency). Since the app already has the cookie hand-off, (a) is the shipped path. Diagnose a report by running the INSTALLED binary yourself: `~/.config/Downloader/plugins/data/com.bezzad.hls/yt-dlp-bin/yt-dlp --js-runtimes deno:<…/deno-bin/deno> -J --no-warnings <url>` — and always test a second, known-public video to tell per-video from per-machine.
- **Cookie capture must include the session's SIBLING origin, not just the link's host** (`common.js cookieUrlsFor`, fixed 2026-08-13): `chrome.cookies.getAll({url})` on `https://youtu.be/<id>` returns **youtu.be** cookies — i.e. essentially nothing — so the extension handed the app an empty jar and yt-dlp's `--cookies` path could never pass YouTube's bot check for short links. `COOKIE_SIBLING_ORIGINS` maps youtu.be/m./music.youtube.com → `https://www.youtube.com/`, x.com ↔ twitter.com, fb.watch → facebook.com; results are merged and deduped by `domain|path|name`. `<all_urls>` + the `cookies` permission already cover the extra reads.
- **yt-dlp self-refreshes on failure** (`YtDlpBinary.TryRefreshYtDlpAsync`, 2026-08-13): it used to be downloaded ONCE at plugin-install time and stay frozen forever while extractors break every few weeks. Now, only *after* an anonymous extraction fails and only if the cached binary is older than `StaleAfter` (3 days), it runs `--update-to stable` (verified working on the standalone Linux build: exit 0, prints `Updated yt-dlp to …` or `yt-dlp is up to date`), touches the mtime either way, and retries once. Guards: once per process, and never for a yt-dlp found on PATH (that one is the user's/distro's).
- **A variant-lookup FAILURE now propagates out of `PluginManager.GetVariantsAsync`** (it used to log + `return null`), so `AddDownloadItemViewModel.VariantError`/`HasVariantError` can show the plugin's message in the Add window. Previously an unresolvable YouTube link showed a spinner, then an empty section, and only explained itself on the failed row after Download. The fall-through rule is unchanged — a failing specific resolver still must NOT surrender the list to a fallback plugin.
- **FIRST thing to check on any "video link won't download" report: is the INSTALLED yt-dlp actually runnable?** (2026-08-13: the reported YouTube failure turned out to be this, not cookies.) `stat -c '%A %s' ~/snap/downloader/current/.config/Downloader/plugins/data/com.bezzad.hls/yt-dlp-bin/yt-dlp` showed `-rw-rw-r--` **23,607,553** bytes while `file` reported *"missing section headers at 39924472"* — a download interrupted at 23 MB of 40 MB, left at its FINAL name, never chmod'd, dated a month earlier. Every extraction then died in `Process.Start` ("yt-dlp could not be started"), for every site, no matter how signed-in the browser was. **Also check WHICH install the user runs** — this box has both a snap (`~/snap/downloader/current/.config/Downloader/`) and a plain one (`~/.config/Downloader/`), each with its OWN plugins + binaries; the plain one's yt-dlp was perfectly healthy, so testing there proves nothing about the snap. Compare `config.json` mtimes to see which is live. Remedy for an already-broken install: delete the fragment — the app refetches it on next launch.
- **Downloaded tool binaries: never write to the final path, never trust `File.Exists`** (`BinaryFile.cs`, the fix for the above). Root cause was two-fold: `DownloadAsync` streamed straight into the destination (a kill mid-download leaves a fragment under the real name — the `catch` can't help when the process dies), and *every* availability check (`EnsureYtDlpAsync`, `TryEnsureDenoAsync`, `EnsureFfmpegAsync`, and all three `PluginBinaryDependency.IsAvailable` lambdas) asked only `File.Exists`, so the fragment read as "installed" forever and the host's startup re-ensure skipped it. Now `BinaryFile.DownloadToAsync` writes `<path>.partial` and `File.Move`s on completion, and `BinaryFile.IsUsable` = exists + ≥1 MB + executable bit (an unusable leftover is deleted and refetched). `MakeExecutable` is `File.SetUnixFileMode`, not a spawned `chmod` — the old one was fire-and-forget and a spawn can silently fail under snap confinement.
- **STANDING: bump the plugin csproj `<Version>` whenever plugin code changes** (same session/commit; fixes = patch, behavior = minor). Forgetting it means the catalog update check sees no new version and users never get the fix (happened with the 2026-07-11 HLS fixes — code changed, version sat at 1.1.2 until the author caught it; bumped to 1.2.0).

## Link variants (link-variants change) — SDK + Add-window picker
- **SDK**: `ILinkResolver.GetVariantsAsync(url, ResolveOptions?, ct)` (DIM, null = no choices) returns `LinkVariant { Id, Label, Description?, ExpectedSize?, IsDefault, SubstituteUrl? }`; the chosen id flows back via `ResolveOptions.VariantId` and persists on `DownloadItem.VariantId` (Retry re-resolves the same variant). **Two variant flavors**: a `SubstituteUrl` variant IS its own link (Ollama tag → the item's URL becomes `gemma3:12b`, VariantId stays null — this keeps post-download actions correct since they parse the item URL); a facet variant (video quality) keeps the pasted URL + sets VariantId.
- **Add window**: `AddDownloadItemViewModel` takes a `getVariants` seam (MainViewModel passes `PluginManager.GetVariantsAsync`); single-URL input triggers a background lookup; **`CanDownload` is false while the lookup runs** (author's decision — 90 s safety CTS so a hang never wedges Add); multi-select checkboxes (default pre-checked); `BuildItems()` (internal test seam) returns one `DownloadItem` per checked variant. Multi-URL paste skips the picker.
- **HLS 2.0.0 (2026-08-21) — site extraction DROPPED.** Plugin only handles real `.m3u8`/`.m3u`. YouTube/x.com/… page URLs are no longer claimed. Why it always broke: yt-dlp vs those sites is an arms race (bot-checks, PO tokens, truncated yt-dlp/deno). Quality picker never worked for actual HLS because `GetVariantsAsync` returned null for `.m3u8`. Now a master playlist lists `#EXT-X-STREAM-INF` (highest bandwidth = default, size ≈ bandwidth × duration); `VariantId` selects that rendition; a media playlist has no picker and downloads as before. ffmpeg is the only runtime dependency. Version 1.4.0 → 2.0.0 so installed copies get the update prompt (and stop extracting page URLs).
- **HLS**: `HlsResolver.GetVariantsAsync` fetches the playlist; master → `ListMasterVariants` (bandwidth desc, default = `Best()`); media → null. Playlist GETs cache 5 min so list+resolve share one fetch. `Pick(master, variantId)` falls back to `Best()` when the id is missing/unknown.
- **Ollama tags endpoint gotcha**: the registry host 404s the OCI `/v2/<name>/tags/list`; the REAL tag list is `https://ollama.com/<ns>/<model>/tags` with `Accept: application/json` → `{"tags":[…]}` (`HttpOllamaRegistry.GetTagsAsync`, separate `tagsBaseUrl`). Gated live test: `Live_tags_endpoint_returns_real_gemma3_tags` (DLDESKTOP_NET=1). Tag-less pastes (`gemma3`) list variants; tagged ones resolve directly (`OllamaModelRef.HasExplicitTag`).
- **CI**: every workflow job now sets `timeout-minutes` (none had one — a hang burned GitHub's 6 h default). The parallel-segments plan-runner test asserts `MaxConcurrent >= 2` with a 500 ms server delay — `>=3` @250 ms was flaky on loaded macOS runners.

## Website offline-copy plugin + the transfer path (website-offline-zip-plugin change)
- **The SDK transfer path (`ITransferProvider`/`ITransfer`) is now CONSUMED by the app** (`DownloadManager.Transfers.cs`): `Start` checks `FindTransferProvider(urls[0])` BEFORE plugin resolve — a claiming provider's `ITransfer` owns the whole download. Progress → `vm.StageProgress` (normal pump), Pause/Resume → `vm.ActiveTransfer` (StartOrResume resumes a paused transfer in place), Cancel/Remove → `vm.TransferCancellation.Cancel()` (an OCE from `StartAsync` = user cancel → row stays Stopped, NOT Failed). Queue cap works unchanged (Status=Running is set synchronously). Future torrent plugin can reuse this as-is. Tests: `Plugins/TransferPathTests.cs` (fake provider + `PumpUntil` RunJobs loop).
- **Fallback resolvers**: `ILinkResolver.IsFallback` (DIM false). `PluginManager.FindResolver`/`FindResolverPluginId` are two-pass (regular resolvers first) so a generic "any web page" resolver can never shadow GitHub/HLS/Ollama; `GetVariantsAsync` shows ONLY the detected resolver's variants — first NON-EMPTY answer in fallback order; a later resolver is consulted only when the earlier offers none or throws. (It originally MERGED all claiming resolvers' variants — author rejected that: the Website fallback's "Offline copy" polluted YouTube quality lists. Don't reintroduce merging.) **The Add picker treats "no variant checked" as a plain add**, so a fallback plugin can offer ONE unchecked variant and the default flow is untouched.
- **Website plugin** (`Downloader.Desktop.Plugins.Website`, id `com.bezzad.website-zip`, optional/catalog tier like HLS): offers "Offline copy (.zip)" for `text/html` URLs via a `SubstituteUrl` variant that rewrites the item URL to the **`websitezip:` scheme** — its `WebsiteTransferProvider` claims that scheme, crawls (BFS, same-host pages depth 3/200 pages, requisites any host, 2000 assets, CSS re-parsed for url()/@import), rewrites refs relative (uncaptured links become absolute), zips to `<host>.zip`. Pure pieces (`LinkExtractor` position-based regex refs, `LocalPathMapper` URL→local layout) are unit-tested; loopback e2e in `Plugins/Website/WebsiteCrawlTests.cs` (+ a `DLDESKTOP_NET=1`-gated live crawl of example.com). No external binaries. Limitation (documented): no JS rendering; restart mid-crawl restarts the crawl. **Unknown-total transfers must report `TotalBytes = 0`**: `DownloadItemViewModel.FlushProgress` latches the FIRST positive staged size into `Size` forever — a running byte counter reported as "total" froze the row's size at ~first-flush bytes (v1.0.0 bug). The app-side guard (`DownloadManager.Transfers.cs`) also clears `vm.Size` at transfer start and drops totals `<= BytesReceived`.
- **`minAppVersion` is now ENFORCED**: `PluginCatalogService.MeetsMinAppVersion` hides catalog entries needing a newer app ("More plugins" list + startup update check) — the Website plugin carries `minAppVersion: 2.1.0` because older apps lack the transfer path (a websitezip: item there would hit the engine and fail).

## Add-window resolver badge + page-URL expired-fix (add-window-plugin-badge change)
- **Pasted page URLs used to always fail**: `IsExpiredOrInvalidLink` flagged any small HTML result. Guard = `DownloadManager.UrlLooksLikePage(sourceUrl)` (pure; no/HTML-ish path extension) skips the sniff — HTML is the expected content of a page URL. Don't remove the sniff itself (signed .zip/.mp4 links still need it).
- **Add-window badge**: `PluginManager.FindResolverPluginName(url)` (sync, CanResolve-only, fallback-ordered) → `AddDownloadItemViewModel` seam `getResolverName` (wired from MainViewModel) → `ResolverName`/`HasResolver`/`ResolverBadgeText` → pill overlaid bottom-left INSIDE the links-box `Panel` in `AddDownloadItemView.axaml` (mirror of the clipboard hint at bottom-right). i18n key `Add_HandledBy` ("Handled by {0}") in all 16 packs.
- **Quick visual check of a dialog change without the docs screenshot set**: throwaway `[AvaloniaFact]` gated by an env var that `view.Show(); Dispatcher.UIThread.RunJobs(); view.CaptureRenderedFrame().Save(...)` — render, view the PNG, delete the test before committing.

## Plugin update swap: NEVER load plugin DLLs by path (fixed 2026-07-11, post-v2.0.0)
- **Symptom (user-reported on the v2.0.0 snap)**: Settings→Plugins showed HLS v1.1.2 with an Update badge; Update "did nothing"; Remove+Add "reinstalled 1.1.2" — while the 1.3.0 DLL was correctly downloaded, sha256-verified and extracted to `plugins/com.bezzad.hls/` (disk forensics proved it byte-identical to the release asset).
- **Root cause**: the .NET runtime caches loaded assembly images **by file path**. After an in-place update swaps `plugins/<id>/<name>.dll`, `AssemblyLoadContext.LoadFromAssemblyPath` on that same path returns the **OLD** image (even from a fresh collectible ALC, even after the old ALC was unloaded). Reproduced in-process with the real v1.9.0 (1.1.2) and v2.0.0 (1.3.0) zips.
- **Fix**: `PluginLoadContext.LoadPluginAssembly` reads the file and uses `LoadFromStream` (entry DLL AND ADR-resolved deps) — stream loads bypass the path cache. Regression: `Plugins/PluginReloadTests` (copies Ollama dll to a path, removes, copies Hls dll over the SAME path, asserts the new id loads). Side effect: plugin `Assembly.Location` is empty — fine here (version comes from `GetName().Version`, data dir from `IPluginContext.DataDirectory`), but don't add plugin code relying on `Assembly.Location`.
- Also: `PluginCatalogService.InstallOrUpdateAsync` now removes the old copy only **after** the new zip downloads (before, a failed download left the plugin silently uninstalled behind a stale row), and `UpdateInstalledAsync` re-syncs the lists on failure too.

## packaging-donate-batch (2026-07-17): x.com syndication, apt/deb, GitHub Sponsors, MSIX
- **x.com "some links won't download" — cookie-free syndication fallback (HLS 1.3.2)**: the whole chain is actually sound on current develop (verified live: yt-dlp 2026.07.04 extracts the reported `/status/…/video/1` link, `SiteExtractor` picks the `http-2176` `LikelyCombined` progressive MP4, that stream is a plain public 6.4 MB `video/mp4`). The real issue is **intermittency**: x.com's guest-token GraphQL periodically returns no media ("No video could be found in this tweet"). The prior fix (`1bb08e6`) retried with `--cookies-from-browser`, but that's the fragile path (hangs on macOS keychain / needs a signed-in browser). Robust fix in `YtDlpBinary.ExtractJsonAsync`: for `IsTwitter(url)`, after the anonymous attempt fails and BEFORE the browser-cookie loop, retry with `BuildArgs(..., extractorArgs: SyndicationArgs)` = `--extractor-args "twitter:api=syndication"` — yt-dlp's cookie-free public endpoint that serves public tweet media even when the guest API is empty (verified: 9 formats). `BuildArgs` gained an optional `extractorArgs` param; `IsTwitter`/`SyndicationArgs` are pure/tested (`YtDlpArgsTests`). **Bump the plugin csproj `<Version>` every plugin change** — did 1.3.1→1.3.2.
- **Debian `.deb` + signed APT repo on GitHub Pages** (`apt install downloader`): `scripts/build-deb.sh <publish-dir>` builds `dist/Downloader_<ver>_amd64.deb` (layout mirrors the AUR pkg: `/opt/downloader`, `/usr/bin/downloader` symlink, `.desktop`, hicolor icon; `dpkg-deb --root-owner-group` avoids sudo/fakeroot). `scripts/build-apt-repo.sh <out> <deb>…` builds a signed repo (`dists/stable/{Release,InRelease,Release.gpg}` + `pool/`) using ONLY `dpkg-scanpackages` + `gpg` — the Release MD5Sum/SHA256 blocks are computed in bash (NO `apt-ftparchive` dep; it's not installed on this box). Signing key generated (ed25519, no passphrase): **public** at `packaging/apt/pubkey.gpg` + `packaging/apt/KEYID` (E1B3FF9E46158C10, committed); **private** NOT in repo → author adds it as the `APT_GPG_PRIVATE_KEY` repo secret (armored key was left in the session scratchpad). `release.yml` `deb` job (needs `build`): downloads the released linux-x64 tarball, builds+attaches the `.deb`, then (only if the secret is set) builds the repo into `_site/apt` and deploys via `actions/deploy-pages@v4`. **Author-gated activation: Settings→Pages Source="GitHub Actions" + set the secret.** Both scripts smoke-tested end-to-end here (deb builds; repo signs; `gpg --verify InRelease` = Good signature against the committed pubkey). Docs: `packaging/apt/README.md` + README "Debian/Ubuntu (APT)" block. GH Actions gotcha: a step-level `if:` can't see that step's own `env:` — gate secret-conditional steps on a **job-level** `env: APT_KEY: ${{ secrets.… }}` then `if: env.APT_KEY != ''`.
- **GitHub Sponsors + Donate modal**: `.github/FUNDING.yml` (`github: [bezzad]`, `liberapay: bezzad`, custom→Donate.md) → repo Sponsor button (lights up once the author enrolls at github.com/sponsors/bezzad). In-app: `DonateViewModel.GitHubSponsorsUrl` + `OpenSponsorsCommand`; a new "GitHub Sponsors" card at the TOP of `DonateView.axaml` (structural copy of the Liberapay card, pink `#DB61A2` heart); i18n key `Donate_Sponsors_Hint` added to all 16 packs; `Donate.md` Sponsors section. Test `Donate_links_include_github_sponsors`. Rendered + eyeballed via a throwaway `DLDESKTOP_DONATE=1` capture (deleted).
- **MSIX (build-only, self-signed — Store submission is author-gated)**: `packaging/msix/AppxManifest.xml` (Identity `bezzad.Downloader` / Publisher `CN=bezzad`, `{VERSION}` placeholder, `runFullTrust`+`internetClient`) + `packaging/msix/Assets/*` (7 logos generated from `Assets/downloader.png` via PIL — no ImageMagick on this box). `scripts/build-msix.ps1` (Windows/pwsh only): publishes/uses a win-x64 dir → stages payload+manifest+assets → `makeappx pack` → self-signs with a `CN=bezzad` self-signed cert (`New-SelfSignedCertificate`, exports `.cer` for testers to trust). `-SelfSign:$false` for a Store upload (Partner Center re-signs). `release.yml` `msix` job (windows-latest) downloads the released win-x64 zip, builds, uploads a `Downloader-msix` **workflow artifact** (not a release asset). Can't build MSIX on Linux — validated only that the manifest is well-formed XML + release.yml YAML parses. Docs + Partner Center submission checklist: `packaging/msix/README.md`. **Author-gated: register Partner Center, reserve the app identity, swap Name/Publisher, upload unsigned.**

## APT-repo Pages deploy: the `github-pages` environment must allow the `v*` tag (v2.2.0 release fix)
- **Symptom**: the `deb` job in `release.yml` failed with **empty step logs** (job rejected at init, before any step) → the `.deb` was NOT attached to the release either, even though its build+attach steps precede the Pages steps.
- **Cause**: the job declares `environment: github-pages` (required by `actions/deploy-pages`). The repo's `github-pages` environment had a deployment-branch policy allowing only **`main`**, but a release runs on the **tag** ref `refs/tags/vX.Y.Z` — the environment gate rejects it, killing the whole job (attach included).
- **Fix (one-time repo config, no workflow change)**: add a `v*` **tag** policy to the environment — `gh api -X POST repos/bezzad/Downloader.Desktop/environments/github-pages/deployment-branch-policies -f name='v*' -f type='tag'`. This lets tag-triggered deploys pass the gate and fixes every future release. Then `gh run rerun --job <deb-job-id>`. Verified: `.deb` attached + repo live at https://bezzad.github.io/Downloader.Desktop/apt (InRelease signature validates against the served pubkey; `apt install downloader` works). Author must keep Pages enabled (Settings→Pages→Source="GitHub Actions", already done) and the `APT_GPG_PRIVATE_KEY` secret set (done).

## NEVER spawn a shell (issue #4 — Bitdefender ATC4 blocked the app for it)
- **Symptom**: Bitdefender Advanced Threat Defense reported `ATC4.Detection` / `SuspiciousBehavior.30C90CB86FF01125` on a clean Win11 machine, quarantined the app + the just-installed HLS plugin files, timeline `explorer.exe → Downloader.exe (unsigned) → powershell.exe → conhost.exe`. Also quarantined unrelated files (`C:\ProgramData\Microsoft\NetFramework\BreadcrumbStore\`, AMD DXCache) — those are NOT ours, they're the behavioral-rollback net, which is how you know it's a generic verdict and not a signature hit.
- **Cause (all app-side, NOT the plugin)**: 4 places shelled out. `WindowsNotifier` spawned `powershell.exe -EncodedCommand <base64>` **per toast** — and `PluginsViewModel` posts a notification on plugin-install success, which is exactly why the user saw it "after installing plugins". Plus `StartMenuShortcut` (powershell + WScript.Shell to write the .lnk), `StartupService` (spawned `reg.exe` for the HKCU Run key), `UpdateService` swap script (`powershell Expand-Archive` over its own exe). Nothing malicious; the *shape* — unsigned parent to hidden encoded script child, + persistence write + self-replacing exe — is what gets scored.
- **Red herring**: it was NOT the old yt-dlp cookie path. yt-dlp never used PowerShell (0 refs in the deleted `YtDlpBinary.cs`); it read cookies itself via `--cookies-from-browser`. Dropping yt-dlp in HLS 2.0.0 removed the *infostealer-shaped* half (download+run unsigned yt-dlp.exe/deno.exe, read Chrome/Edge/Brave/Firefox cookie stores) but left the PowerShell chain untouched.
- **Fix (all in-process, no child processes)**: toasts to `Shell_NotifyIconW` + `NIF_INFO` on a cached hidden `HWND_MESSAGE` window (Win10/11 render it as a real toast + Action Center entry, so "works while in the tray" is preserved; keep the WNDPROC delegate in a static field, clamp text to szInfoTitle=64/szInfo=256, icon via `ExtractIconExW(Environment.ProcessPath)`); .lnk to `IShellLink`+`IPersistFile` COM (`BuiltInComInteropSupport` is already true and the app isn't trimmed, so ComImport just works — declare interface members in FULL vtable order); Run key to `Microsoft.Win32.Registry` (available on the platform-neutral `net10.0` TFM, annotate `[SupportedOSPlatform("windows")]`); update extract to the in-box `"%SystemRoot%\System32\tar.exe" -x -f` (bsdtar reads zip, Win10 1803+) by ABSOLUTE path (also kills PATH hijacking). Rejected: a `net10.0-windows10.0.x` TFM for WinRT toasts (forks the build matrix for every platform); an in-app-only Avalonia toast (regresses tray-hidden delivery).
- **Guardrail: `Unit/NoShellSpawnTests`** text-scans app+plugin source and FAILS the build on `powershell`, `pwsh`, `Expand-Archive`, `WScript.Shell`, `-EncodedCommand`, `cmd /c`, `--cookies-from-browser`, spawned `reg.exe`. Key design point: it **strips comments but still scans string literals** (the ban is on doing it, not explaining it — and the old `Expand-Archive` shipped inside a string literal). The stripper is string-aware (verbatim strings/escapes, so a `//` in a URL doesn't eat the line) and blanks to spaces so line numbers stay right. Allow-list is empty on purpose. The scanner+stripper are themselves tested. **Its first run caught two of my own leftover comments** — trust it.
- **Still open**: the Windows binaries are UNSIGNED (Bitdefender's own timeline says so) — Authenticode/Azure Trusted Signing is the real root fix and needs a cert from the author. The three Windows paths above are **unverifiable on Linux/CI** (no Windows runner): written fail-soft, pure parts unit-tested, but they need a manual Windows smoke test (notification / delete Downloader.lnk + relaunch / toggle run-at-startup / take an update).

## Per-download request context (issue #7, `per-download-request-context`)
- **Where it lives**: `Models/RequestContext` (Cookies + Headers + Referer) hangs off `DownloadItem.Request`
  (`[JsonIgnore]`). `DownloadItem.Referer` is a **persisted proxy** onto `Request.Referer` — that is the whole
  persist/transient split: cookies and headers are secrets (memory only, never in `config.json`, never logged);
  a referer is not, so it survives a restart. Test `Saving_the_config_keeps_the_referer_and_drops_cookies_and_headers`
  guards it — keep any new context field on the right side of that line.
- **`POST /api/add`** also takes `headers` (a `{"Name":"value"}` object) and `referer`. Malformed entries are
  skipped, never fatal. `ToJson` (the CLI forward path) carries the referer but never cookies/headers.
- **Applying it**: `DownloadManager.ApplyRequestContext(cfg, ctx)` in `Start`, after the per-item speed cap.
  Per-item beats global. **The four headers the engine models as PROPERTIES must not go into the raw
  `WebHeaderCollection`** — `SetHeader` routes `User-Agent`/`Referer`/`Accept`/`Content-Type` to
  `RequestConfiguration.UserAgent/Referer/Accept/ContentType`; the collection either rejects them or
  `SocketClient` ignores them. `SetHeader` uses the **indexer, not `Add`** so a per-item header replaces a
  global one instead of appending. `Plans.ApplyHeaders` now funnels through `SetHeader` too, so a resolver's
  per-part header cleanly overrides the item's.
- **`RequestConfiguration.CookieContainer` is NOT null by default** (the engine pre-creates one, Count=0) —
  don't assert null to mean "no cookies", check `Count`. `new System.Net.Cookie("bad name", …)` does NOT throw
  either; the framework is more permissive than it looks, so only genuinely empty name/domain entries are
  skipped by our guard.
- **Retry stays authenticated**: cookies are kept as a LIST on the item, and `EnsureCookieFile` re-creates the
  transient Netscape file in `Start` when it's missing. `BuildItem` still writes it on add (untouched path);
  `Start`'s `finally` still deletes it.
- **Resolver side**: `ResolveOptions.Headers` (SDK, init-only) carries the bag; `DownloadManager.ResolveHeaders`
  flattens item headers + referer into it (**the `referer` field wins over a `Referer` header, on both sides**).
  `HlsResolver` passes `options?.Headers` to every playlist GET and stamps it on each produced `DownloadPart`.

## Pause and the plan runner: `DownloadManager.Pause` alone is NOT enough (issue #7 follow-up)
- **The trap, and it is invisible from `Pause`**: `vm.Download` is only the MOST RECENTLY started part
  engine (`onPartService?.Invoke(svc)` per part). A segment plan runs `SegmentParallelism = 4` engines at
  once, so `vm.Download?.Pause()` stopped one segment and left three transferring. Worse, the runner loop's
  only stop signal was `isCancelled: () => vm.Status == Stopped` — `Paused` was invisible to it, so as each
  in-flight segment finished it started the next, working through the rest of the playlist. The row read
  "Paused" with a frozen bar the whole time, because `FlushProgress` drops staged progress for a non-Running
  row. That frozen-bar-while-still-downloading is exactly what the reporter saw.
- **The fix is two halves and you need BOTH**: `PlanController` (bottom of `DownloadManager.Plans.cs`, hung
  off the row as `vm.PlanControl`) holds every in-flight `DownloadService` and a paused flag. `Pause` pauses
  the whole set; an `isPaused` predicate beside `isCancelled` makes the runner wait rather than claim a slot
  for the next part. Pausing the live set without the gate just lets the loop start fresh parts; the gate
  without the set leaves the current ones running.
- **`PlanController.Add` pauses a late joiner** — an engine built just before the pause and started just
  after would otherwise leak one running segment.
- **Cancelling a PAUSED plan needs `CancelAll()`, which un-pauses first.** A suspended engine never completes
  its awaited task, so cancelling without resuming leaves the runner waiting on it forever.
- **`isPaused` reads the ROW's status** (`vm.Status == Paused`), not the controller's flag, so a pause that
  lands while the runner is between parts is still honored. `isCancelled` semantics are untouched.
- Both new `ExecutePlanAsync` params are optional and default to today's behavior, so existing callers and
  tests are unaffected.

## `/api/add`: the GET query carries a request context too, in WIRE shapes (issue #7 follow-up)
- `ApiAddRequest.FromQuery` parsed only `url`/`filename`/`path`/`queue`/`start`/`referer` and **silently
  dropped `cookies`/`headers` while still answering `201`** — a capture tool driving us from a GET template
  had never actually handed over a session. The query form now takes `LocalApiService.ParseCookieHeader`
  (a `name=value; name=value` Cookie-header string, first `=` splits so base64 values survive, domain taken
  from the TARGET URL's host) and `ParseHeaderBlock` (newline-separated `Name: value`). Both pure; a parse
  problem never fails the add.
- **The `201` body now reports `cookies`/`headers` counts + a `referer` bool.** Counts only — never a value.
  This is what turns "it silently didn't work" into a two-second diagnosis.
- **Contract, not a preference: never log the request URL or query string.** `LocalApiService`'s error log is
  route-name only, and there's a comment at the log site saying why. Widening it would put a live session in
  a log file, which is the whole mitigation for accepting secrets in a query at all.
- **Cookies reach a plugin resolver as a synthesized `Cookie` header** (`DownloadManager.ResolveHeaders`):
  a plugin's own `HttpClient` never sees the engine's `RequestConfiguration.CookieContainer`. An explicit
  `Cookie` header from the caller wins over the synthesized one.
- **`ConcatRecipe.KeyHeaders`** carries that context to the AES-128 key fetch, which happens at ASSEMBLY
  time out of a bare client — the one request that went out anonymous, and the reason an encrypted stream
  could fail at ~99%. `HlsResolver` stamps it only when the playlist is actually encrypted; null (every
  older recipe, and DASH) ⇒ unchanged behavior. The `keyFetcher` delegate gained a headers parameter.

## Two build/test traps that cost a session
- **`pkill -f "dotnet test"` kills your own shell** (exit 144) — the invoking bash's command string contains
  the pattern. Same family as the `pkill -f "Downloader.Desktop"` note above. Match the child by a pattern
  that can't appear in your own command line, or just don't pre-kill.
- **NuGet `Central Directory corrupt` + `Invalid argument: …/<random>.ein`** after a `dotnet` "Internal CLR
  error (0x80131506)": the crash left a 0-byte temp file in a package folder. Fix = `rm -rf` that package's
  version dir under `~/.nuget/packages/` and rebuild. It is not a code or restore-source problem.

## Expired-link refresh (issue #6, `refresh-expired-link`)
- **A signed link that dies mid-download is now recovered automatically.** `DownloadManager.HandleFailure` is
  the single place a failed attempt becomes a Failed row (both the `e.Cancelled`-with-error and the
  `e.Error` branches funnel through it); it first calls `TryAutoRefreshLink`, which re-queues the item when
  `LooksLikeExpiredLinkError(error)` (HTTP **401/403/404/410**, found by unwrapping `AggregateException`/inner
  exceptions) AND the item already has bytes AND `vm.LinkRefreshAttempts < MaxAutoLinkRefreshAttempts` (2).
  Why re-queueing is the whole fix: `Start` copies `item.Urls` into a LOCAL array before rewriting `urls[0]`
  with the resolved redirect, so the ORIGINAL pasted URL is still stored and every attempt re-resolves it →
  a fresh signature. The partial file is kept (engine `EnableAutoResumeDownload`, on by default).
- **Only a resume is refreshed** (`Downloaded > 0`): a link that never delivered a byte is a bad link and must
  fail honestly. The counter resets on completion and on a **user** `Retry`/`Resume` (the internal path uses
  `RequeueForRefresh`, which deliberately does NOT reset it) — otherwise a dead link retries forever.
- **The refresh gap shows as "Getting a fresh link…", not Failed and not an error**: `TryAutoRefreshLink` sets
  `vm.IsRefreshingLink` + `Status = Created` (never Failed — that flashed a failure in the grid) and leaves
  `ErrorMessage` null; `StatusText`'s Created case reads the flag; `Start` clears it. Note `FinishTerminal`
  pumps the queue right after, so in practice the row restarts within the same UI tick — don't write tests
  that expect the flag to still be set after `RaiseFailedForTest`.
- **Manual path**: the Details window's URL box was already editable for a non-running row, but it now has a
  hint + a **Refresh link** button (`DownloadDetailsViewModel.RefreshLinkAsync`, `internal` so tests call it
  directly instead of driving the ReactiveCommand). It probes with `UrlResolver.ResolveFileInfoAsync` and
  `EvaluateNewLink(knownSize, newSize)` → Match/Unknown → swap + `Resume`; Mismatch → `ConfirmAsync` first.
  **Why the size check matters:** the engine's `TryResumeFromExistingFile` derives its metadata offset from
  `stream.Length - Package.TotalFileSize`, so a new link reporting a DIFFERENT size makes it delete the
  partial file and start over — silently destroying what the user was trying to save.
- **The URL box writes through to the item as you type**, so an abandoned/failed refresh must restore
  `_committedUrl` (captured when the dialog opens, updated on each successful swap) or the item is left
  pointing at an unvalidated URL. `ProbeAsync`/`ConfirmAsync` are internal seams for tests (no network, no
  window).
- **Engine facts worth not re-deriving**: a 4xx surfaces because `SocketClient.SendRequestAsync` calls
  `EnsureSuccessStatusCode()` (so `HttpRequestException.StatusCode` is populated); `ChunkDownloader` retries
  `MaxTryAgainOnFailure` times against the SAME url before it gives up; mirrors are load spreading, NOT
  failover (each chunk is pinned to one request instance, and the file-info probe uses the first URL only).

## MPEG-DASH (`.mpd`) support — issue #5, `dash-mpd-support`
- **DASH lives INSIDE the HLS plugin**, not a separate one: `Downloader.Desktop.Plugins.Hls/Dash/`
  (`DashResolver`, `MpdParser`/`IMpdParser`, `MpdModels`, `DashException`), plugin id still
  `com.bezzad.hls`, display name now *Streaming media (HLS & DASH)*, version 2.2.0. Rationale: a second
  plugin would duplicate the whole `FfmpegBinary`/`BinaryFile` dependency machinery and make users
  download a second ~80 MB ffmpeg into a second data dir. The two resolvers claim disjoint extensions
  (`.m3u8`/`.m3u` vs `.mpd`), so `PluginManager`'s two-pass lookup is unaffected.
- **Parse on LOCAL names, never a namespace URI** (`e.Name.LocalName`): real MPDs use several schema
  namespace URIs and some omit the namespace entirely — binding to one URI rejects good files.
- **Refuse, don't half-support**: `type="dynamic"` (live) and ANY `ContentProtection` element → a
  `DashException` whose message reaches the failed row. A DRM manifest must never look downloadable.
- **`SegmentBase` / bare `BaseURL` means the representation IS one whole file** — emit ONE part and let the
  engine multi-chunk it (`PartKind.Video`/`Audio`). Do NOT translate `indexRange`/`Initialization@range`
  into `Range` headers: those exist for player seeking, and a Range header fights the engine's own ranged
  chunking. Segmented representations emit `PartKind.Segment` parts (1 chunk each, 4 in parallel).
- **`$Number%04d$` (and `$Time%0Nd$`) zero-padding is common** — a naive `Replace("$Number$", …)` produces
  wrong URLs. `MpdParser.Substitute` handles `$$`, `$RepresentationID$`, `$Bandwidth$`, `$Number$`,
  `$Time$`, each with the optional `%0Nd` width form, and LEAVES an unknown identifier in place so a broken
  URL is visible rather than silently mangled. `r="-1"` on a timeline `<S>` = repeat to the end of the
  period → derive the count from `mediaPresentationDuration`.
- **`ConcatRecipe` grew `Streams` + `IntermediateExtension`** instead of a new SDK `PostProcessKind`
  (which every external plugin would have to learn). `Streams == null` → one group built from
  `HasInitSegment`/`Segments.Count`, i.e. every pre-DASH recipe deserializes unchanged. `Segments` stays a
  flat 1:1 list across all groups, so AES-128 support is untouched. One group → concat + `RemuxAsync`; two
  → concat each + `MuxAsync(video, audio)`; more → refused. **A group of exactly one unencrypted whole file
  skips concatenation** and is handed to ffmpeg in place — copying a multi-GB file first is pure waste.
  DASH sets `IntermediateExtension = ".mp4"` (its segments are fMP4; labelling them `.ts` misleads ffmpeg's
  probe).
- **Extension**: `mpd` joined `MEDIA_EXTENSIONS` + `application/dash+xml` joined `MEDIA_CONTENT_TYPES`;
  new `isManifest(url)`/`MANIFEST_EXTENSIONS` drives `groupKey` (each manifest is its own group).
  `background.js` returns `kind: "dash"` with `size: null` and **must not size-probe a `.mpd`** — the
  manifest's own ~1 KB would be filtered out by `isPlausibleMediaSize` and the card would vanish.
- Fixtures for every addressing mode live in `Downloader.Desktop.Tests/Plugins/Hls/Fixtures/dash-*.mpd`.
  End-to-end (real stream → ffmpeg → playable file) is NOT verifiable headlessly — left as the author's
  manual check.
- **`NormalizeAssembledName` must strip EVERY manifest extension, `.mpd` included** (`IsManifestExtension`
  in `DownloadManager.Plans.cs`): the Add dialog's name preview probes the manifest URL, so an untouched
  name arrives as `stream.mpd` and the assembled MP4 would be written under a `.mpd` name no player opens.
  Any future manifest format has to be added there as well as to the resolver.
- **A REAL end-to-end DASH test exists and has actually been run**: `Integration/DashEndToEndTests` has
  ffmpeg author a genuine DASH stream (`-f dash`, separate video/audio adaptation sets), serves the folder
  over loopback, then runs `DashResolver` → `PersistedPlan.From` → `DownloadManager.ExecutePlanAsync` with
  the REAL `HlsPostProcessor` + `FfmpegBinary`, and ffprobes the output. Verified run: 10 parts → a 355 KB
  MP4, `streams=[video,audio]`, `duration=8.01s`, stages `10 × Plan_Part` then `Plan_Assembling`. ffmpeg's
  own generated manifest is the best fixture there is — it uses `$RepresentationID$`, `$Number%05d$` and
  two differently-shaped `SegmentTimeline`s (`r="3"` vs four explicit `<S>`).
  **To run it**: it is gated on ffmpeg+ffprobe being on PATH (repo convention), so by default it silently
  returns. Put a static build on PATH first:
  `curl -sSL https://johnvansickle.com/ffmpeg/releases/ffmpeg-release-amd64-static.tar.xz | tar xJ` then
  `export PATH=$PWD/ffmpeg-*-amd64-static:$PATH`.
  **Gotcha when asserting request counts**: the engine issues a **HEAD then a GET** per part, so 10 parts =
  20 requests — count DISTINCT paths, not raw requests.
- **`ffmpeg`/`ffprobe` 7.0.2 (johnvansickle static) SEGFAULTS (exit 139) reading MPEG-TS on this box**
  (kernel 7.0.0-30-generic). Reproducible with a bare `ffprobe src.ts` — no app code involved. Consequence:
  putting ffmpeg on PATH makes the pre-existing, normally-skipped
  `HlsPostProcessorTests.Real_ffmpeg_remux_produces_mp4_when_ffmpeg_available` FAIL. That is the environment,
  NOT a regression in `FfmpegBinary.RemuxAsync` (whose `-bsf:a aac_adtstoasc` was the first suspect and is
  innocent — removing it still segfaults, and so does `-f null -`). The fMP4 path the DASH tests use works
  fine with the same binary. Don't chase it as a code bug; try a different ffmpeg build if it ever matters.

## Zero build warnings is a HARD rule (2026-08-24, author's standing instruction)
- `dotnet build Downloader.Desktop.sln -t:Rebuild --nologo` must print **`0 Warning(s)`** — app, plugins and
  the test project. Full rationale + the per-code fix recipes are in CLAUDE.md → "Zero build warnings".
  **Use `-t:Rebuild` to check**: a plain incremental build re-reports nothing for up-to-date projects, so it
  can look clean when it isn't (this is how 74 warnings accumulated unnoticed).
- The test csproj is `<Nullable>annotations</Nullable>` (not `disable`) — that is what lets a test say
  `string?` without a CS8632 per annotation. Don't flip it back to `disable`, and don't flip it to `enable`
  either (that turns on nullable *warnings* across the whole suite).
- `TreatWarningsAsErrors` was deliberately NOT enabled: the Windows/macOS CI legs can surface
  platform-specific analyzer warnings that can't be reproduced here, and turning them into hard errors would
  break releases from a machine that cannot verify the fix. The rule is enforced by discipline + this note.

## Coverage: what the number means here (raise-test-coverage, 2026-08-26)
- **Measure with `--settings src/coverlet.runsettings`** (CI does). Without it ~2100 of ~13300 measured
  lines are Roslyn source-generator output — chiefly a **1958-line `RegexGenerator.g.cs` in the Website
  plugin** — plus compiled XAML. That alone cost ~10 points and made codecov's 55% look far worse than the
  code is. The runsettings excludes generated/compiler-emitted code and the test assembly.
- **XML comments in a .runsettings cannot contain `--`** (not even inside a word like `--collect`), or
  vstest fails with *"Settings file provided does not conform to required format"* before running anything.
- **Coverlet DROPS whole PLUGIN assemblies' hits, and it is the ALC tests that cause it.** Symptom: the
  Hls/Website/Ollama assemblies report 0% on one run and 84–95% on the next, no code change; one full run
  came out at 59.8% instead of ~77%. Cause: `PluginLoadTests`/`PluginReloadTests` load the SAME plugin
  DLLs into collectible `AssemblyLoadContext`s that then unload, and coverlet's per-module hit flushing
  loses the data when that happens — only plugin assemblies are ever affected. Filtering just those two
  classes out of a coverage run gives a stable 77.6–77.9% vs 59.8–78% with them. CI keeps running them
  (correctness beats a tidy number), so **expect the reported figure to wobble a point or two**.
  Coverlet only ever LOSES hits, never invents them, so when you need an accurate figure, run the suite
  2–3 times into the same `--results-directory` and max-merge per line — that is the closest to truth.
  A surprising 0% on well-tested code is a re-run, not a bisect. (Cost a session once — don't repeat it.)
- **Scope (author's decision, 2026-08-27): `Views/**` and the platform-integration files are EXCLUDED**
  from the measurement (Windows/macOS notifiers, StartMenuShortcut, StartupService, TrayService,
  TaskbarProgressService) — they need a specific OS or a live desktop session, and StartupService would
  mutate the developer's real "launch at login" just by running the suite. The views are still TESTED by
  `UI/ViewLoadTests`; only the metric's scope changed. Kept deliberately in scope: `SingleInstanceService`
  (loopback IPC, genuinely tested), `Program.cs`/`App.axaml.cs`, and every network-bound service.
  With that scope the number is **~78.5%**, and **82.7%** across the code the suite can guard.
  **Don't quietly widen the exclusions to make a number.** Getting past ~80% needs either another explicit
  scope call (excluding `UpdateFlow`/`UpdateService`/`PluginCatalogService`/`DialogHelper`/`CliRunner`,
  ~636 lines at 40%, which would report ~82%) or real seams for their HTTP/config paths.
- **A "passing" test can be testing nothing.** `ShutdownVerificationTests` is gated behind
  `DLDESKTOP_VERIFY=1` and `return`s immediately otherwise — it passed for months while `ShutdownService`
  sat at 0%. Before writing new tests for a file, check whether an existing suite is env-gated.
- **Verify a target is REALLY uncovered before writing tests for it.** 37 new `WebsiteResolver` tests moved
  coverage by 0 lines: the pure claim helpers were already covered elsewhere and the actual gap was the
  live `GetVariantsAsync` probe. Read the per-file uncovered count first.
- **Per-method coverage analysis lies for async-heavy files**: every async method's state machine is named
  `MoveNext`, so merging by method name collapses them. Trust the per-FILE line numbers.

### Test-writing gotchas found while doing this
- **Never use a `.invalid` HOSTNAME in a test URL** — DNS resolution stalls and the whole suite hangs until
  the outer `timeout` kills it (900 s wasted). Use the repo's unreachable IP `10.255.255.1`.
- `Config.New()` leaves **`DisabledPlugins` null** (the VM null-coalesces it). Seed it in tests.
- **Only the `IsDefault` link variant is pre-checked** in the Add dialog, not all of them.
- A scheduled window's END calls **`PauseQueue`, not `StopQueue`** (the partial keeps its progress).
- `DownloadItem.Url`'s setter **ignores blank values**, so clearing the details URL box cannot erase a
  download's link.
- `FormatDuration(0)` is `"0s"` (a real estimate); only negative/NaN/infinite give `"—"`.
- A queue card's `TotalSpeedText` is **null** until stats refresh.
- `FileExistPolicy` has **`IgnoreDownload`/`Delete`** — there is no `Overwrite`/`Resume`/`Skip`.
- Constructor argument order differs per page VM: `QueuesViewModel(Config, IDownloadManager)` and
  `SchedulerViewModel(Config, IDownloadManager)` but `DownloadsViewModel(IDownloadManager)`.
- `SchedulerViewModel`'s command is **`NewScheduleCommand`**; its row times are `TimeSpan?`, not strings.
- `QueueRowViewModel` exposes **`Queue`** (the `DownloadQueue`), not an `Id`.
- Tests that show real windows are fine headlessly, but a **stale `testhost` from another worktree** makes
  an instrumented run look hung. Kill it **by PID** — `pkill -f` matches your own shell and kills it (exit 144).

## Coverage round 2 — how to reach the parts that "cannot be tested" (2026-08-29, 92.6%)

Read this before concluding a file is untestable. Three of the four biggest "impossible" areas turned
out to be reachable, and two suites were passing **without executing the code they name**.

- **`TestSupport/DesktopLifetimeScope` gives the headless app a real main window.** Every
  `DialogHelper` entry point begins with "if `MainWindow` is null, do nothing", and headless has no
  lifetime — so the file read as covered while none of it ran (28%). The scope sets
  `Application._applicationLifetime` (the public setter refuses after init) to a
  `ClassicDesktopStyleApplicationLifetime` with a shown `Window`, and restores it on dispose. With it,
  `ShowDialog`/`Confirm`/`ShowAbout`/`ShowDetails`/the pickers all really run → 88%.
  **Finding a dialog in a test: use `scope.MainWindow.OwnedWindows`**, NOT `AppLifetime.Windows` — a
  hand-made lifetime never populates its own window list, but `ShowDialog(owner)` does set ownership.
  The pickers work headlessly and simply return null (cancelled).
- **`TestSupport/DeferringScheduler` restores the app's real startup ordering.** `MainViewModel`
  schedules `InitMainViewModelAsync` onto `RxApp.MainThreadScheduler` from its ctor, and the app assigns
  `vm.View` AFTER constructing it. In the app the init defers (nothing pumps the dispatcher yet); under
  `[AvaloniaFact]` the test thread IS the UI thread, so the default scheduler runs init INLINE, before
  `View` is set — `SetupAppShell` then hits `if (View is not Window) return` and silently wires nothing.
  Install `RxApp.MainThreadScheduler = new DeferringScheduler()` (restore it in Dispose) and the whole
  shell path runs: tray, close-to-tray, run-at-startup, local API, single-instance handler, update check.
- **A passing test can be testing nothing — check the per-file uncovered count before writing more.**
  Two shapes seen here: (a) an env-gated suite that returns immediately (`ShutdownVerificationTests`);
  (b) a fixture that swallows its own setup failure — `CliRunnerTests`' `RangeStub` bound all five API
  ports to ONE `HttpListener`, which fails to `Start()` wholesale if any one port is taken, so
  `BoundCount==0` and every test hit its "nothing to assert against" early return. **Something on this
  box holds 15151** (invisible to `ss`), so it failed here and passed on CI. Fixed with one listener
  per port + `PersistPort(stub.FirstBoundPort)` (the CLI tries the persisted port first, via
  `FileService.ConfigFileOverride`).
- **Seams added this round** (all `internal`, never set by the app):
  `StartupService.ApplyOverride` (applying run-at-startup for real would flip the developer's own
  launch-at-login — this is what had blocked the entire shell path), `UpdateFlow.CheckOverride` (only
  the GitHub lookup is network; every decision after it is ordinary logic),
  `PluginCatalogService.ReleasesUrlOverride`, `DialogHelper.{OpenFilePicker,SaveFilePicker,
  OpenFolderPicker}Override` (a picker can only ever be *cancelled* in a test, so everything a caller
  does with a chosen path was unreachable), and `SingleInstanceService.Dispatch` private→internal.
- **Plugin binaries are testable without downloading anything**: `FfmpegBinary(dataDir, HttpClient)`
  takes the client, so an `HttpMessageHandler` stub can serve a `.tar.xz` you build with the system tar
  containing a stand-in `ffmpeg` (a shell script padded past `BinaryFile.MinUsableBytes` = 1 MB, with
  the executable bit). **Scrubbing PATH to force the download path also hides `tar` — and `tar -cJf`
  execs `xz`** — so symlink both into the stub PATH dir.
- **What is genuinely still out of reach** (verified, not assumed): `App.axaml.cs`'s shutdown hook (ends
  in `desktop.Shutdown()`, which would shut the test host down), `CliRunner`'s `add` verb (spawns
  `Process.Start(Environment.ProcessPath)`, i.e. a real GUI — and forwarding instead would post a
  download into the developer's *running* app), and `NotificationService`'s macOS/Windows branches
  (cannot execute on Linux). `Program.cs` is excluded from the metric outright.
- **`RxApp.MainThreadScheduler`, `Application._applicationLifetime`, `PluginManager.PluginsRootOverride`,
  `FileService.ConfigFileOverride` are process-wide.** Always restore them in `Dispose` — collection
  parallelisation is off, so leaking one silently changes a LATER test rather than the current one.

## CI-only test failures from the coverage push (fixed 2026-08-29)
Four failures that a green Linux-Debug run cannot show you. Check all four shapes before blaming the code:
- **A test that asserts on `ShellLauncher` is a LINUX-only assertion.** `NotificationService` only launches a
  command (`notify-send`) on Linux; macOS/Windows post in-process (`MacNotifier`/`WindowsNotifier`), so
  `Assert.Single(sent)` was empty there. Gate the launch assertion on `OperatingSystem.IsLinux()`.
- **The ffmpeg install fixture must match the PLATFORM'S archive shape**: `.tar.xz` on Linux (system `tar`),
  `.zip` on macOS/Windows (`ZipFile`). Feeding a tar.xz to the macOS path fails with *"End of Central
  Directory record could not be found"*. `FfmpegProvisioningTests.BuildArchive` now branches, and the
  fixture's binary is named `ffmpeg.exe` on Windows or nothing is found inside the archive.
- **`LocalApiService` is a process-wide singleton and `Start()` no-ops when it is already running** — a test
  that leaves it bound makes the port-fallback test read 15151 as its "fallback". Always `Stop()` it in a
  `finally` (never conditionally), and `Stop()` defensively at the top of a test that needs a known state.
- **`DownloadPackage.Chunks` can hold a NULL element mid-setup** — the engine fills the array element by
  element. `DownloadDetailsViewModel.ReconcileParts` runs from a POSTED dispatcher job, so the NRE surfaced
  as a "Test Case Cleanup Failure" in an unrelated test. Skip null slots.

## The engine drops a part on the floor if it is paused on its finish line (plan runner)
`DownloadService.StartDownload`'s success branch is `_chunkError is null && Status is DownloadStatus.Running`.
`Pause()` sets `Package.SetState(Paused)`, so a pause that lands between "chunks finished" and that check
falls into the `else` — **no completion signal, no error, and the file never finalized**. The runner then saw
a task that completed with `partError == null` and no usable file: *"Part 4/16 did not finish downloading."*
(Only reproducible under CI load; 5/5 green locally.) Fix is app-side in `DownloadManager.Plans.cs`:
`PlanController.PauseCount` counts pauses, `DownloadPartOnceAsync` returns false instead of throwing, and
`DownloadPartAsync` re-fetches the part (up to `PausedPartRetries`) **only when the count changed during the
attempt** — a part that comes up empty with no pause still fails honestly.

**The in-host hang still happens on CI (2026-08-29).** The ubuntu-latest/Release job burned its whole
30-minute timeout after 1202 of 1232 tests, with the log stopping mid-suite and no culprit named; a plain
re-run of the same commit was green, and the exact CI command (coverage collector, Release) is green locally.
CI's test step now carries `--blame-hang --blame-hang-timeout 180s --blame-crash` so the occurrence
kills the host and NAMES the test instead of leaving a silent 30-minute gap. To find what did not run, diff
`--list-tests` against the job log's `Passed …` lines (`LC_ALL=C sort` both — plain `comm` mis-sorts these).

It fired on the very next run: aborted after **810** tests with *"The test running when the crash occurred:
AppTests.Staged_progress_flushes_only_while_running"* — a pure, instant test that cannot itself hang, so the
name is where the host went unresponsive, not the cause. Different runs stop at different counts (810, 1202),
which points at the shared headless dispatcher wedging rather than at one test. Still NOT reproducible here:
the exact CI command (Release + coverlet collector) is green locally, including 3 consecutive runs pinned to
2 cores with `taskset -c 0,1` to imitate a small runner. A re-run of the job passes. **Grab the artifacts
while they exist** — a re-run REPLACES the artifact, so the `Sequence_*.xml` (its `Completed="False"` rows
are the in-flight tests) and the hang dumps from the failing attempt are gone once you re-run it. Download
first, re-run second.

**It also fires LOCALLY, not just on CI (2026-08-29).** A full local run stopped after **1063** of
~1232 tests and named
`Integration.PlanRowFlowTests.A_multi_part_plan_runs_to_completion_and_leaves_the_row_finished`; that
whole class then passed in **487 ms** on its own, and a re-run of the full suite was green. So the
named test is again just where the shared dispatcher went unresponsive. Reach for this explanation
only AFTER running the named class in isolation — that one command separates "known hang" from "you
broke it", and takes seconds. The `Sequence_*.xml` is worth reading: parse it for
`Completed="False"` (`re.findall(r'<Test Name="([^"]+)"[^>]*Completed="(\w+)"', xml)`) and you get
both the culprit and the exact count of tests that did run.

## THE "test host hung" CI ABORT — root-caused and fixed (2026-09-05). Read this before blaming flake.
The intermittent abort (`Test Run Aborted` + `Total tests: Unknown` + `0 Error(s)` + an inactivity
`hangdump.dmp`, blaming a random `UI.AppTests` test at wildly varying counts) was **a real deadlock in
the app**, not test infrastructure. It had been written off as flake here since July.

- **Cause:** the engine's `Dispose()` is `Clear().Wait()`, and `Clear()` awaits the semaphore that the
  running `StartDownload` holds until it returns. `DownloadManager.ReleaseEngine` called that synchronous
  `Dispose()` — and nearly every caller of it (`FinishTerminal`, `TryAutoRefreshLink`, `TryNextUrl`,
  `TryReduceConnections`, plus the stall watchdog) is reached FROM the engine's own completion event via
  `OnUi`. So it waited for the operation that was waiting for it, **on the UI thread**. Stack from the dump:
  `Task.Wait() ← AbstractDownloadService.Dispose() ← ReleaseEngine ← FinishTerminal ← OnUi ←
  <Attach>b__3 ← OnDownloadFileCompleted ← SendDownloadCompletionSignal ← StartDownload`.
- **In the app this froze the window** when a download finished — it was never only a test problem. It
  got more frequent after `0c6ca1d` added the connection-backoff retry paths (more `ReleaseEngine` calls).
- **Fix:** `DownloadManager.DisposeOffStack` — `Task.Run` + `DisposeAsync`, so the callback returns, which
  is what lets the operation release the semaphore. **Never call `engine.Dispose()` from a manager path
  again**; anything reachable from an engine event must dispose off that stack.
- **Why xunit's `Timeout` never fired:** it cannot time out a test whose dispatcher is the deadlocked
  thing. That is why the blame landed on an innocent later test and why it looked random.
- **Regression test:** `Integration/EngineReleaseBlockingTests.Releasing_from_inside_the_engines_own_
  completion_does_not_deadlock` — releases from inside the engine's real `DownloadFileCompleted`, hopping
  to the UI thread the way the app does. Verified: **hangs** on the old code (killed by an outer timeout,
  exit 124/143, blowing past its own 180s Timeout) and passes in <0.5s with the fix. The weaker sibling
  test ("does releasing a busy engine return quickly") passes EITHER way — `Clear()` cancels first when
  the release is not nested in the callback — so do not treat it as the guard.
- **How to catch this class of thing:** run the EXACT CI command locally (`--settings
  src/coverlet.runsettings --blame-hang --blame-hang-timeout 180s --blame-crash`, under
  `taskset -c 0,1`). It writes a `crashdump.dmp` even on a passing run; `dotnet-dump analyze <dmp> -c
  pstacks` is what exposed this in one look. A healthy dump is ~36 lines (just coverlet's `UnloadModule`
  at exit); the deadlocked one was 284.

**`dotnet build` on this box intermittently prints `Internal CLR error. (0x80131506)`, exits 0, and does
NOT recompile.** Two "verification" runs silently tested a stale binary because of it. After any build you
intend to draw a conclusion from, prove the change is in the output — e.g.
`strings …/bin/Debug/net10.0/linux-x64/Downloader.Desktop.dll | grep -c <NewSymbolName>` — and pick a
symbol that only exists in the new code (a method you renamed, not one whose definition still exists).

**The one-command way to prove a CI failure is the hang and not your change (2026-09-05).** It struck both
Windows legs on `8fbf59c` while ubuntu×2 and macOS×2 were green. Do NOT start by reading your diff — start
with `gh api "repos/bezzad/Downloader.Desktop/actions/workflows/dotnet-desktop.yml/runs?branch=develop&per_page=12"
--jq '.workflow_runs[] | "\(.conclusion)\t\(.head_sha[0:7])\t\(.id)"'` and look for two commits whose code
is IDENTICAL with different outcomes. Here `77233df` (sync) and `8fbf59c` (archive) are both docs-only, zero
code delta: one passed all six legs, the other failed two. That is conclusive in one step, and the author's
own `0c6ca1d` had already failed before any of the session's work. Confirm the signature in the job log
(`Test Run Aborted` + `Total tests: Unknown` + `0 Error(s)` + an inactivity-triggered `hangdump.dmp`, with
the two legs blaming DIFFERENT tests at wildly different counts — 150 vs 476), then `gh run rerun <id>
--failed`; it went green. **Grab artifacts BEFORE re-running** (a re-run replaces them) — though note the
Windows ones are ~400 MB because they carry the dumps, so usually the log alone is enough.

**Never conditionally restore `LocalApiService`.** Three tests used the "remember whether it was running,
only stop it if it wasn't" pattern, which PRESERVES another test's leak instead of clearing it — one leak
then reached `AppShellStartupTests.Starting_up_builds_the_pages_and_re_applies_the_saved_choices`
(`Assert.False(LocalApiService.IsRunning)`) and failed a test that had done nothing wrong. Note
`ResetDefaultsCommand` re-applies the shipped defaults, and `EnableBrowserIntegration` defaults to ON, so
the Settings reset test BINDS the listener. Always `Stop()` unconditionally in `finally`, and stop it once
more to establish a baseline in any test that asserts on `IsRunning`.

**`Start_retries_in_background_until_a_port_frees_up` needs EVERY port taken, and macOS CI won't always
give you that.** It blocks all five with `HttpListener`s, but a prefix can be refused there while the port
stays free — the service then binds it and `Assert.False(IsRunning)` fails, reporting a bug that isn't
there. The test now probes each port it failed to block with a raw `TcpListener` (`PortIsFree`) and leaves
without asserting when the "everything is taken" precondition cannot be met, plus `Stop()`s first so a
leaked listener isn't mistaken for a successful bind.

**A test must never really spawn the desktop.** `PluginsViewModelCatalogTests.Reloading_rereads_the_plugins_folder`
executed `OpenFolderCommand` with no seam, so on CI it actually ran `xdg-open` — its stderr
(*"file '/tmp/dldesktop-plugins-vm-…' does not exist"*) ended up quoted as the test host's crash reason,
which is a great way to spend an hour blaming the wrong thing. Set `ShellLauncher.OpenOverride` (or
`RunOverride`) and assert the target instead; both are `internal` seams visible to the test project.

## Two plugins cannot share source, and other lessons from adding SiteMedia (2026-08-30)
- **Never link the same `.cs` into two plugin projects.** The obvious way to give the new site-media
  plugin the HLS plugin's segment pipeline + ffmpeg provisioning was `<Compile Include="..\Hls\*.cs"
  Link="Shared\…" />`. It builds — and then the TEST project, which compile-references both plugins,
  cannot name any of those types (CS0433, ambiguous between two assemblies). A runtime reference between
  two separately-downloaded plugins is worse still: either becomes unloadable without the other. So a
  plugin that needs the same capability gets its OWN types under its OWN names (`ToolFile` vs
  `BinaryFile`, `FfmpegMuxer` vs `FfmpegBinary`) and the duplication is the price of independent
  installability. Same reason SiteMedia refuses an adaptive-only page instead of re-implementing the m3u8
  pipeline.
- **`ResolvePlanAsync` used to swallow a claiming resolver's failure** and fall through to "download the
  link as-is", i.e. fetch the page's HTML and report whatever that turned into — which is how "this is a
  live stream" reached the user as an invalid link. It now rethrows as `PluginResolveException` when
  `FindResolver(url) != null`; an UNCLAIMED link still falls through unchanged. `Start`'s catch was also
  calling `Describe(ex)` rather than `DescribeFailure(ex, item)`, so the item-aware wording (extension
  hand-off, session-required) never applied there.
- **The post-download action is found by the id of the plugin that OWNED the download**, and that id was
  only ever recorded for a plugin that RESOLVED the link. A transfer-route download has no resolver, so
  `FindResolverPluginId` returned null and the finished row never offered "Add to …". `Start` now tries
  `FindTransferProviderPluginId` first on that path. When something like this is reported, drive all three
  completion routes (engine / `Plans.cs` / `Transfers.cs`) in one test — four of the five paths were fine.
- **yt-dlp's `SHA2-256SUMS` lists every asset, Deno ships one `.sha256sum` per asset.** One coreutils
  parser reads both, but "a single entry matches whatever you asked for" must be OPT-IN
  (`ParseSums(text, asset, allowSingleEntry)`) — as a default it silently accepts the wrong asset's digest
  from a multi-asset listing.
- **A test project's own `Tests.Plugins` namespace shadows the unqualified `Plugins.` prefix**, so
  `Plugins.Ollama.X` resolves to the wrong place from a test in `Tests.Integration`. Use a using-alias
  (`using OllamaPlugins = Downloader.Desktop.Plugins.Ollama;`); `global::` inside the type name works but
  reads terribly.
- **Ollama's Ollama-tier version lives in `OllamaPlugin.Version` (a string), not the csproj** — it is a
  BUILT-IN plugin and ships with the app, so there is no catalog `<Version>` to bump. Catalog-tier plugins
  are the ones whose csproj `<Version>` is the single source.
- `python3 - <<'PY'` heredocs and C# raw strings (`"""`) fight each other; write those edits with the Edit
  tool. And a `cd X && python3 …` whose `cd` fails runs nothing — check the shell's cwd, which this
  session's tooling resets independently of `cd`.

## Mirrors are LOAD SPREADING, not failover — and three engine facts found proving it (2026-08-30)
This is the fact a shipped regression turned on (v2.8.0, issue #9): the extension was changed to hand the
app the clicked link as a download's address with the redirect chain's end as a "mirror", believing the
mirror would be tried if the first failed. **It never was.** `DownloadPackage.Urls` spreads a download's
chunks; each chunk is pinned to one url and the file probe reads `Urls[0]` only. Every site that serves
its file from a different address than the page broke. The failover now lives in the app
(`DownloadManager.TryNextUrl` + `OrderUrlsForAttempt` + `vm.UrlAttempt`), so the hand-off ordering is no
longer load-bearing — but **do not re-add an "it'll fall back" assumption anywhere else**, and if you
change which address leads, the test that must fail first is `Integration/UrlFailoverTests`.

Three engine behaviours that cost an hour each while writing those tests:
- **The engine MUTATES the `DownloadConfiguration` you hand it.** After a failure it may read
  `ChunkCount=1, ParallelDownload=false` even though the attempt was configured for 8 — it rewrites the
  object when it cannot learn the file's size. Anything that needs to know what an attempt *intended* must
  capture it at Start (`vm.PlannedConnections`), never read it back afterwards.
- **A resumed download keeps the chunk layout its package was created with.** Retrying with `ChunkCount=1`
  over an existing `<name>.download` re-opens the SAME eight ranges — the setting is ignored. Backing off
  to one connection therefore has to discard the partial file (`DiscardPartialFile`), which is why the
  concurrency retry does.
- **The size probe is `GET bytes=0-0`, not HEAD** (a loopback fixture that counts "concurrent GETs" will
  count probes and refuse requests no real server would). And `Completed` is raised on the UI thread
  *before* the file is necessarily in place: a test that reads the bytes must wait for the FILE, not the
  status, or it flakes with FileNotFoundException.

Related, from the reporter's own measurements: a server can serve a file over 1–3 connections and answer
**403 from the 4th on**. `LooksLikeConcurrencyRefusal` (403 + more than one planned connection) triggers
one single-connection retry, and a 403 that survives it is reported as `Error_ServerRefusedConnections`,
never as an expired link — the global maximum is a ceiling, not a quota every server has agreed to.

### Writing an end-to-end test around a real engine download: four traps (2026-08-31)
Two happy-path tests had to be abandoned after passing locally and failing on CI. Reproduce that
environment with **`taskset -c 0,1 dotnet test -c Release`** — it fails the same way, and is the only
cheap way to tell "the app is broken" from "the runner is small". The traps, in the order they bit:
- **`MaxTryAgainOnFailure = 0` makes the engine issue NO request at all** and never complete. Use 1 when a
  test wants minimal retrying; zero is not "no retries", it is "nothing happens".
- **The engine spreads a download's chunks across every url it is given**, so with two loopback addresses
  it can fetch from the SECOND one inside the first attempt — the app's failover never runs and the test
  proves nothing. Worse, when the lead 403s it can report **Completed having written no file at all**.
  (That empty-completion is a real product hazard, recorded in the change's tasks.)
- **Whether parallel chunks OVERLAP depends on core count**: a "refuse while another request is in flight"
  fixture refuses nothing on a two-core runner. Model the server on request SHAPE (e.g. refuse ranged
  requests, serve whole-file ones) instead.
- **A file has to be big enough for the engine to split it** — 256 KB is downloaded as one chunk, 2 MB
  splits into eight.
Also: `Assert.True(cond, $"...")` builds that message BEFORE the wait, so a failure message that reads
"status=Running, requests=[]" may just be describing the starting state. Pass a `Func<string>`.

### Give the engine ONE address per attempt (2026-08-31)
`DownloadManager.OrderUrlsForAttempt` now hands the engine a single url and lets `TryNextUrl` walk the
list. Reason: the engine spreads chunks across every url it is given, and a download's addresses are NOT
equivalent mirrors — they are "the link the user clicked" and "where the browser ended up". Handing it
both let a dead address keep receiving chunks, so a download could **finish with an empty file and a green
row**, and the retry inherited the same poison. Two guards came out of it, keep both:
- `LooksEmptyAfterCompletion` — a "successful" completion whose file is missing or zero-length becomes an
  `EmptyDownloadException`, which travels the normal failure path (so the next address is tried) but has
  its own wording (`Error_NothingDownloaded`) instead of inheriting "this link expired".
- Resolve the finished file's path with the first **non-blank** candidate, not the first non-null: the
  engine's package routinely carries an EMPTY `FileName` for a download that produced nothing, and `??`
  accepts `""` — which silently disabled the guard for the exact case it exists for.
**Still open:** with every request refused, the engine sometimes emits no completion event at all and the
row sits Running for ever. The app cannot see it; it needs a watchdog. That is why the backoff's
happy-path test is missing (it hangs ~1 run in 3), not because the backoff is unproven.

### An abandoned attempt's engine can still write the row's outcome
Every retry path (`TryNextUrl`, `TryReduceConnections`, `TryAutoRefreshLink`) releases the current engine
and starts a new one — but the OLD engine can still deliver its completion afterwards. Acting on it marked
rows Completed over files that attempt never wrote (seen only on macOS/Debug CI: `status=Completed`,
`folder=[]`). `Attach` now stamps each engine's handlers with `vm.AttemptGeneration` and drops events from
any engine that is no longer the row's. Any new retry path must go through the same `Attach`, and any new
engine event handler must keep the `Stale()` check.

### The engine could finish without reporting — fixed upstream, with an app-side watchdog
`DownloadService.StartDownload`'s final `else` (an "unexpected terminal state") only logged and returned:
**no `DownloadFileCompleted`, ever**. The awaited task finishes and the row stays Running for ever with no
error, no file and nothing to retry. Reachable through the public API by pausing exactly as the chunks
finish — which is also the real cause of the old "engine drops a part on the floor if it is paused on its
finish line" note above. Fixed in `bezzad/Downloader` commit `632ccdc` (a pause after every byte arrived is
now **Completed**; anything else is **Failed** with an `IncompleteDownloadException`), covered by
`IntegrationTests/IssuesTest/CompletionSignalTest.cs`.
- **Engine repo on this box**: it multi-targets up to `net11.0` and the installed SDK is 10.0.x, so
  `dotnet test` fails outright. Use `-p:TargetFrameworks=net10.0 -f net10.0` — a plain `-f net10.0` is not
  enough, the TFM list itself has to be overridden. Full suite is 533 tests, ~12 min.
- **App-side backstop** (`DownloadManager.IsStalled` / `FailStalledDownloads`, on the UI pump): fails an
  attempt with no progress and no completion for `StallTimeout` (3 min). It must ONLY watch a Running row
  with a live engine and no `PlanStage` — assembling segments or running ffmpeg moves no bytes for minutes,
  and a paused row is silent by design. `StallTimeout` is an internal settable seam for tests; it is
  process-wide, so always restore it in a `finally`.
- Two app tests that need the engine to report reliably are `Skip`-ped with the upstream commit named,
  rather than deleted or left flaky. Re-enable them when the app moves to an engine release with the fix.

### A failed attempt can start two engines for one row (fixed 2026-08-31)
`HandleFailure` re-queues the row (`Dispatcher.Post`) AND frees its queue slot. Freeing the slot pumps
the queue, which starts the next address immediately; the posted re-queue then arrives and marks that
already-running attempt `Created` again, so the pump starts a SECOND engine. Two engines write the same
`<name>.download`, one deletes the other's file, and the row stays Running for ever with no error — it
looks exactly like a server that never answers. Guard: `RequeueForRefresh` returns early for a row that is
already Running/Completed, and `Start` marshals to the UI thread so its "already running" check cannot be
raced from an engine callback thread. Symptom to recognise in a log: two `Starting: <same url>` lines and
`AttemptGeneration` higher than the number of addresses.

### An app-level test that only fails alongside its siblings is usually a dispatcher race
`Integration/UrlFailoverTests` passed alone in 0.5 s and failed ~1/3 in a class run. Fastest way to the
cause: enable `AppLog` inside the test and put the tail of the log (plus `vm.Download.Status`,
`Package.ReceivedBytesSize/TotalFileSize`, `AttemptGeneration`) into the assertion message via a lazy
`Func<string>` — the engine's own lines showed the duplicate start immediately.

### The e2e specs share one fixed port range — run them with `workers: 1`
MV3 host permissions are static, so every spec's stub app must listen on the same range the extension
probes. Parallel spec files take each other's ports: an add lands on another file's stub, or
`app-not-found` finds a stub and fails instead of skipping. Both pass when the file runs alone.

### Deno publishes its WINDOWS digests as PowerShell `Get-FileHash`, not coreutils (issue #11)
`https://github.com/denoland/deno/releases/latest/download/<asset>.sha256sum` is
`<hex>  <name>` for linux/macOS but `Algorithm / Hash / Path` lines (uppercase digest, a `C:\…` build
path) for BOTH Windows assets — so a coreutils-only parser read no digest and `ToolChecksum` correctly
refused to extract, breaking the SiteMedia plugin's Deno step on every Windows machine. `ParseSums` now
reads either shape and still matches by name (the file name comes out of the `Path` value). Check a
publisher's file with `curl` before assuming a format; yt-dlp's `SHA2-256SUMS` is coreutils everywhere.

### Never marshal `Start` onto the UI thread (tried 2026-08-31, reverted)
It looked like a tidy way to make the "already running" guard race-free, and it broke three CI legs:
a start deferred by a dispatcher hop never runs in a test that is not pumping at that moment, so rows
stayed Running and `MemoryReleaseTests`/`PlanRowFlowTests` failed or hung the test host. It also was NOT
what fixed the double-start — the `RequeueForRefresh` guard was. Keep `Start` synchronous-entry.

### A retry must wait for the previous attempt's task (`vm.Attempt`)
The engine raises `DownloadFileCompleted` BEFORE the file is in place, and the row disposes the engine
from inside that event — so a Retry/Resume that immediately builds a new engine races the old one's final
flush over the same `<name>.download` path. Signature: the new engine reports
`Package.ReceivedBytesSize == TotalFileSize` while the save folder is EMPTY, and the row never leaves
Running. `Start` now stores its attempt task on the VM and the next attempt awaits it (bounded, 10 s).
This was the 1-in-3 flake in `MemoryReleaseTests.A_released_stopped_row_can_be_retried_to_completion`
(present since 2026-07-17, reproducible locally only under `taskset -c 0,1 -c Release`).

### A CI hang in an unrelated test = something killed the dispatcher thread (2026-08-31)
The `--blame-hang` dump named `TransferPathTests.Transfer_failure_…`, but `Sequence_*.xml` showed it was the
ONLY test in flight and `pstacks` showed **no thread anywhere executing app code and no dispatcher thread at
all** — the xunit worker was simply parked in `AvaloniaTestCase.Run`. When the dispatcher is dead, the test
can neither run nor time out (its `Timeout` needs the dispatcher), so the abort blames whichever test was
next. This is the same signature as the 2026-07-17 parallel-collections hang, with parallelism already off.
Cause this time: **an exception escaping a `DispatcherTimer` tick** takes the dispatcher's thread with it —
in the app that is the UI thread, i.e. a frozen window. `OnUiPumpTick` and `OnSchedulerTick` now catch and
log; `RunUiPumpTickOnce()` is the test seam, and `Unit/TimerTickSafetyTests` injects a throwing
`StatsChanged`/`ListChanged` listener to pin it. Rule: **no DispatcherTimer handler may throw.**
Diagnosis recipe: `gh api repos/<o>/<r>/actions/runs/<id>/artifacts`, download the leg's artifact,
`grep 'Completed="False"' Sequence_*.xml`, then `dotnet-dump analyze <dmp> -c pstacks`.

### The real fix for the dispatcher deaths: `Dispatcher.UIThread.UnhandledException`
Avalonia 12 raises it (WPF-style, with `e.Handled`) for anything that throws on the UI thread — a posted
job, a timer tick, an `async void` continuation. `App.Initialize()` now subscribes, logs and sets
`Handled = true`, which is the floor under every one of the ~20 `Dispatcher.UIThread.Post` sites and the
`OnUi` helper. Without it, ONE throwing job ends the thread: the app's window freezes silently, and the
headless suite loses the dispatcher, after which no test runs or times out. Tests host the real `App`, so
the suite is covered by the same hook. `Unit/TimerTickSafetyTests` pins it — verified by commenting the
hook out and watching the test go red, which is worth repeating for any "safety net" test.

### `SingleInstanceTests.Forwarding_to_nothing…` fails when YOUR Downloader is open
The lock ports (15150/15156–15158) are process-wide, so a real app running on the dev machine answers the
handshake and `TryForwardAdd` correctly returns true. Check with `ss -ltnp | grep 1515`. The test now
`Assert.SkipWhen`s on it — don't "fix" the service for this.

### The completion event fires BEFORE the file is in place — never judge the folder immediately
The empty-completion guard (`NothingWasDownloaded`, issue #9) read the save folder inside
`DownloadFileCompleted` and so failed successful downloads with "nothing was downloaded" whenever the
engine's final move lagged — invisible on an idle box, reproducible on macOS CI. It now waits for the
file (`DownloadManager.EmptyFileGrace`, 5 s, internal so tests can shorten it) and fails only if it never
appears; the late-arrival path calls the SAME `MarkCompleted` + `FinishTerminal` as an immediate success,
because a row left Running because the check was late is the bug this guard was written to prevent.
`Integration/LateFileCompletionTests` covers both sides.

## Extension 1.7.0: one list, previews, a download folder (single-list-thumbnails-path)
- **The popup's "Main media" vs "Other detected" split is GONE, and so is `content.js`.** The promotion
  rule (`computeMainGroups` + a per-tab `activeHint` the content script posted on play/pause/timeupdate)
  needed the hint to be FRESH (≤3 s) at the exact moment the popup asked. On a feed page whose player has
  finished autoplaying — x.com, the site this is used on most — it routinely was not, so every group
  including the real video was demoted behind a collapsed `<details>`: the user had to expand "Other" to
  find the video they were looking straight at. Replaced by `common.js`'s pure `sortDetectedGroups`.
  **Ordering by file TYPE was the first attempt and the author rejected it as ambiguous** (it cannot say
  why a 360p mp4 sits above a 1080p webm — type says nothing about which copy of a video is the good
  one). The rule is now: **HLS master first, then quality (`qualityHeight`), then known size, then
  title.** A quality is only used when the link or a picker label NAMES one — `1080p`, `1920x1080`,
  `4K`; relative words (`hd`/`high`/`low`) are deliberately parsed as *unknown*, because inventing a
  number for them orders the list on a fiction, and measured size is the more truthful fallback.
  `qualityHeightFromUrl` scans the whole PATH (not just a trailing token — CDNs use `/1080p/v.mp4`) and
  ignores the query string. No clock, no page state, no hint. **Don't reintroduce a relevance key** — a
  wrong guess would now reorder the list rather than merely mislabel a section.
- **DASH (`.mpd`) is NOT surfaced by the extension** (author's call, 1.7.0): removed from
  `MEDIA_EXTENSIONS`, `MEDIA_CONTENT_TYPES` (`application/dash+xml`) and `MANIFEST_EXTENSIONS`, and the
  `kind: "dash"` probe branch is gone from `background.js`. A manifest can be neither size-probed nor
  read for a quality, so it could only ever be a row the ordering rule can say nothing about. **The
  app's own DASH support is untouched** (`dash-streams`, the streaming plugin) — a `.mpd` still works
  when pasted into the popup's link box (that path applies no media filter) or added in the app.
  Removed with it: `MAIN_WINDOW_MS`, the `main` flag on items, `background.js`'s `activeHint` +
  `activeMediaHint` branch, `content.js` and the `content_scripts` manifest entry (so nothing of the
  extension runs on a page unless the popup is open).
- **`Number(null)` is `0` and `Number.isFinite(0)` is true** — that made an *unprobed* group (size `null`)
  outrank a measured one in `groupKnownSize`. Check `typeof x === "number"` BEFORE the numeric test
  anywhere a "not measured yet" value shares a field with a real number.
- **Previews are collected by the POPUP via `api.scripting.executeScript`**, not by a content script: the
  injected function returns, per `<video>`/`<audio>`, `currentSrc`/`src` + `poster` + a canvas
  `toDataURL("image/jpeg", 0.6)` frame capped at 160 px, plus the page's `og:image`/`twitter:image`. Same
  injection path as "Scan page links", no background state (MV3 may evict the worker), and the data URL
  dies with the popup — it is never sent to the app (a test asserts the add payload carries no
  `data:image`). **A cross-origin video taints the canvas** so `drawImage`/`toDataURL` throws
  `SecurityError` on exactly the sites that matter (x.com); the poster → `og:image` → placeholder fallback
  chain is the real feature. Mapping is `buildThumbnailIndex`/`pickThumbnail` (exact `src`/`groupKey` match,
  else the largest element's image, else the page image) — a blob: MSE src can never match a network URL.
  E2E-verifiable only on a SAME-ORIGIN fixture video (`e2e/fixtures/video-playing.html`), where the grab
  really succeeds and the row's `img[src]` is a `data:image/jpeg` URL.
- **Popup rows**: single `<ul id="list">`; a fixed 64×36 `.thumb` slot always exists (`.placeholder` with
  the type letters when there's no image, `img.onerror` → back to placeholder) so late previews never
  reflow the list. E2E selectors are `#list li` now — `#mainList`/`#otherList`/`#otherSection` are gone.
- **Download folder**: `GET /api/settings` → `{ defaultSavePath, version }` (new route in
  `LocalApiService`, read-only — keep it to those two fields, since the same API *accepts* cookies and
  headers and an echo is how a secret would escape). The options page prefills its text box from
  `fetchAppDefaultSavePath()` ONLY when nothing is saved (`getSavePath()` wins, so visiting the page can
  never undo an edit), and every silent send (`sendToAppSilently`, both forms) plus `handOffToApp` adds
  `path`. `path` is deliberately NOT part of `hasContext`, so a plain send keeps the GET form it always
  used. A folder the app rejects is a 400 → `"fail"` → the interception path leaves the browser's own
  download alone, which is the behaviour that chain was built for.
- **`scripts/build-extension.sh`'s `COMMON=(…)` list must track the manifests** — `verify_zip` fails the
  build if a manifest references a file the zip lacks, and AMO rejected earlier releases for exactly the
  opposite (a `content_scripts` entry naming a file that wasn't packaged). Removing a file means removing
  it from that array too.

## The ORDER of the recovery paths in `HandleFailure` is load-bearing (issue #9, after v2.8.2)

A failed attempt is offered to three recovery paths in turn, and which one goes first decides whether the
download survives:

1. **`TryReduceConnections` — same address, one connection.** Must come FIRST. It only fires on a 403 (or a
   finished-with-nothing) raised while more than one connection was in flight, so it cannot steal an
   ordinary failure. Putting the address walk ahead of it (v2.8.0–2.8.2) spent every address at full
   concurrency and left the polite retry to whichever address happened to be LAST — for a browser hand-off
   that is the clicked page link, not the mirror holding the file. The reporter's mirror failed at 4+
   connections and succeeded at 1 with the same link, while the app *had* that retry and aimed it wrongly.
2. **`TryNextUrl` — the next address**, which resets `ForceSingleConnection` so a capable mirror is not
   demoted to one connection by the previous address's punishment. Bound: ≤ 2 attempts per address.
3. **`TryAutoRefreshLink` — a fresh signature for the current address.**

### The count is STEPPED and cached per host (issue #14, supersedes "one single-connection retry")
`TryReduceConnections` no longer latches to one connection: it HALVES `vm.PlannedConnections`
(8 → 4 → 2 → 1, `DownloadManager.NextConnectionCount`, capped by `MaxReducedConnectionAttempts`) and
writes the result to `vm.AttemptConnections` (`int?`, null = use the ceiling — the old
`ForceSingleConnection` bool is gone). A server that refused eight may serve four, and collapsing to one
made that download four times slower than it had to be.
- **Every step still discards the partial file**, for the reason it always did: a resumed download keeps
  the chunk layout its package was created with, so asking for four connections while an eight-chunk
  partial is on disk re-opens the same eight ranges and is refused again. That is also why the resume
  guard (`vm.PreAttemptSize is not null` ⇒ no step down) must stay — a 403 on a resume is the
  expired-link shape, and that path SAVES the partial.
- **Where a download settles is remembered per HOST** (`Config.ServerConnectionLimits` +
  `Services/ServerLimits`, pure over the dictionary). `Start` begins at
  `ChooseStartingCount(host, ceiling, now)`, always clamped by the configured count — the Settings number
  is a ceiling, and a remembered 8 must never beat a user who has since chosen 2. `MarkCompleted` records
  a count below the ceiling and CLEARS the entry when the ceiling itself succeeded; entries expire after
  `ServerLimits.RetestAfter` (7 days, settable in tests) so one bad minute on a CDN is not permanent.
- **`ApplyConnectionCount` must also lower `ParallelCount`** when it is non-zero: it overrides
  `ChunkCount` in the engine (0 means "same as ChunkCount"), so a leftover ceiling value would re-open
  exactly the concurrency just backed off.
- A stepped-down row says so (`IsReducingConnections` → `State_FewerConnections`, in all 16 packs) while
  it is queued AND while it runs, and `Error_ServerRefusedConnections` no longer tells users to lower a
  setting the app now manages. Decision tests: `Integration/AdaptiveConnectionTests`,
  `Integration/UrlFailoverTests`, `Unit/ServerLimitsTests`.
- **A loopback "accepts at most N connections" server is modelled on request SHAPE**, not on requests
  actually overlapping: `PickyServer.MinRangeBytes` refuses any ranged body smaller than a threshold,
  which is exactly "no slice smaller than 1/N of the file". Counting real overlap depends on core count
  and passes while proving nothing on a two-core runner.

Two traps worth keeping:

- The connection backoff **deletes the partial file** (a resumed download keeps the chunk layout its
  package was created with, so one connection changes nothing while eight ranges sit on disk). So it must
  never run on a download that was RESUMING real bytes — guarded by `vm.PreAttemptSize is not null`. A 403
  on a resume is the expired-link shape, and that path keeps the partial.
- The decision tests for this live in `Integration/UrlFailoverTests.cs` and drive `RaiseFailedForTest`
  with `vm.PlannedConnections` set by hand — no engine, no timing. End-to-end variants of the same thing
  are the ones that historically only passed on a fast machine (see the NOTE in that file before writing
  another one).

## Two sessions in ONE worktree: never `git add -A` (learned the hard way, 2026-09-01)
- The author sometimes runs a second session on the SAME checkout (e.g. one on the extension, one on
  issue #9's retry order). `git add -A` then sweeps the OTHER session's uncommitted work into your
  commit and pushes it under your message — it happened: `DownloadManager.cs` +
  `Integration/UrlFailoverTests.cs` (143 lines of someone else's in-flight work) landed inside an
  extension commit. **Stage explicit paths** (`git add src/browser-extension docs/... `) and read
  `git status --short` BEFORE committing, treating any file outside your own change as someone else's.
- **Do not "fix" it by rewriting history or reverting their files.** `develop` is shared (never
  force-push), and a revert commit would delete edits another session is still holding in its working
  tree. The honest repair is: verify the tree still builds and their tests pass, leave the content
  alone, and TELL the author — the other session can carry on and commit the rest normally (its
  `git status` will simply show those files as already committed).
- **A concurrent `dotnet build` in the same tree produces phantom failures**: a run that overlapped the
  other session's build reported `1 Error(s)` with no error line and exit code 0; re-running alone gave
  `0 errors, 0 warnings`. Same family as the stale-`testhost` note above. Re-run alone before believing
  a build/test failure, and prefer a filtered `--filter` run over the full suite while another session
  is active.

## Firefox/AMO auto-publish: was broken for 2 months, NOT a token issue (fixed 2026-09-01)
- **Diagnosis recipe**: `gh run list --workflow=extension.yml` (or, once deleted, `gh run view <id> --log`
  on old run ids from `git log` blame) + `curl -fsSL "https://addons.mozilla.org/api/v5/addons/addon/<slug>/versions/?page_size=50"`
  to see what's ACTUALLY live on AMO. This showed the last real publish was `1.1.0` (2026-07-03); every
  release from `1.2.0` through `1.6.1` never reached Firefox users, only Chrome/Edge (manual dashboard).
- **Root cause**: `.github/workflows/extension.yml`'s staging step hand-copied a file list
  (`cp background.js common.js popup.html popup.css popup.js`) that silently drifted from the manifest —
  `content.js` (content_scripts) was never in it, and later `options.html/.css/.js` weren't either. AMO's
  linter rejected every submission with `MANIFEST_CONTENT_SCRIPT_FILE_NOT_FOUND`. **The `AMO_JWT_ISSUER`/
  `AMO_JWT_SECRET` secrets were valid the whole time** — `gh secret list` shows them unchanged since
  2026-07-03, and the failed run logs show the job got past auth and upload, only AMO's post-upload
  validation failed. Never assume "token expired" for a publish failure without reading the actual log —
  the error message named the real cause directly.
- **Fix, don't just re-add**: the rebuilt workflow stages by calling `scripts/build-extension.sh` itself
  (the SAME zip a manual dashboard upload uses — its `verify_zip` already fails the build if a manifest
  reference is missing) instead of a second hand-maintained file list, so the two can never drift apart
  again. It also runs `npx web-ext@8 lint --source-dir <unpacked>` — the SAME linter AMO's server runs —
  as its own failing CI step BEFORE `web-ext sign`, so a broken package fails loudly in CI instead of only
  inside AMO's review queue (this is how the 2-month breakage went unnoticed: nothing ran the linter until
  the real submission). Verify locally before trusting a workflow edit: `./scripts/build-extension.sh &&
  unzip dist/downloader-extension-firefox.zip -d /tmp/x && npx --yes web-ext@8 lint --source-dir /tmp/x`.
- **`manifest.firefox.json`'s `strict_min_version` was also stale** (`121.0`, but
  `browser_specific_settings.gecko.data_collection_permissions` needs Firefox 140/Android 142) — a
  WARNING not the failing error, but bumped to `142.0` anyway so a real submission carries zero lint
  warnings too, not just zero errors.

## In-app browser-extension install (install-browser-extension, 2026-09-01)
- **No browser accepts a locally installed unsigned extension into a normal profile, and OS elevation does
  not change that** — this question is settled, don't re-open it. Chromium's external-install registry key
  and `External Extensions` JSON take only a Web-Store `update_url`; Firefox needs a Mozilla-signed xpi. The
  ONLY thing admin rights buy is a browser-**policy** write (`ExtensionInstallForcelist` / `policies.json`),
  which is the hijacker signature and is scored HIGHER for being elevated — i.e. strictly worse than the
  unsigned-exe-spawns-powershell shape that already got this app quarantined (issue #4). So the app fetches,
  verifies and unpacks the files; the browser is where the install happens. `NoShellSpawnTests` now bans the
  policy hooks AND the profile-path fragments (`Login Data`, `Local State`, `Web Data`, `cookies.sqlite`,
  `places.sqlite`, `profiles.ini`) outright.
- **`BrowserDetector` reads existence + executable path ONLY.** A feature about browsers is exactly where
  profile access gets added by accident. Windows = `Microsoft.Win32.Registry` (`StartMenuInternet` →
  `shell\open\command`, then `App Paths`), never a spawned `reg.exe`; Linux = `PATH` + snap/flatpak export
  dirs; macOS = known `.app` bundles. `DetectOverride` is the test seam (a test cannot install a browser).
- **The unpack destination must NEVER move**: a browser derives a manually loaded extension's ID from its
  absolute folder path, so a new path per install means a new identity and an empty settings store, and a
  temp folder breaks the extension when the OS cleans temp. `<AppData>/Downloader/extension/<target>/`,
  staged as `<target>.new` → swap so an interrupted install leaves the previous copy intact.
- **A matching sha256 does NOT make a zip trusted** — it only proves it is the file the catalog named. Every
  entry path is still checked for `..`, rooted and drive-qualified forms before extraction.
- **`storeUrl` in `packaging/extension/targets.json` is the whole switch** between the manual "load unpacked"
  path and opening that browser at its store listing. Publishing a listing is a data edit, not code.
- **The extension identifies itself on requests it already makes** (`extv`/`extb` query params, JSON fields
  on the POST form, `X-Downloader-Extension` header), read once in `LocalApiService`'s request loop so
  `/ping` and the legacy routes carry it too. In memory only — never persisted, never logged (the GET form of
  `/api/add` carries a live session; that's why the URL isn't logged either). An unreadable manifest yields
  `{}` and the request goes out unchanged.
- **"Connected" must come from the extension calling, never from having unpacked files.** The manual load
  fails in ways the app cannot see (Developer mode disabled by policy, user closed the tab), so a tick
  meaning "we unzipped something" is worse than no tick.

## Two shared-checkout / flake traps (2026-09-01)
- **`git commit` commits the INDEX, not what you just `git add`ed.** The existing note says "never
  `git add -A`" — that is not enough. Another session's `git add` had already staged six of its files, so a
  commit of two of my own swept all six in under my message. Nothing was pushed, so the repair was
  `git reset --soft HEAD~1` + `git restore --staged <their paths>` + recommit (their working-tree content is
  untouched by that; they only have to re-`add`). **Read `git diff --cached` before every commit here**, not
  just `git status`.
- **The Playwright `interception` spec can fail in a FULL run even with `--workers=1`** and pass 10/10 when
  its own file runs alone (seen: "browser's copy is cancelled" got `in_progress` instead of `interrupted`).
  That symptom looks exactly like the app rejecting the hand-off, so check the stub first — `startStubApp`
  answers 201 regardless of body, so extra JSON fields/headers cannot cause it. Re-run the single spec
  before believing a regression. The existing note said workers=1 made the suite green; it mostly does.
- **A `Task.Delay` after a fire-and-forget `ICommand.Execute(null)` is a flake waiting for CI.** One such
  assertion passed alone and failed in the 1490-test run. Type the command `ReactiveCommand<Unit, Unit>`
  (still an `ICommand`, XAML unchanged) and `await cmd.Execute()` — needs `using System.Reactive.Linq;`.

## "Open containing folder" does nothing on Linux — the snap/D-Bus trap (2026-09-01)
- **Symptom**: clicking open/reveal-folder does nothing at all, for every download row, no error, no log
  line. Reported on Linux; reproduced under the **snap**.
- **Root cause, two defects compounding**: (1) the Linux reveal is a D-Bus call to
  `org.freedesktop.FileManager1`, which **AppArmor DENIES to a snap-confined app**, but `dbus-send`
  *without* `--print-reply` never waits for a reply and **exits 0 anyway**; (2) `ShellLauncher.Run`
  reported whether the process STARTED, not whether it succeeded. So the app concluded the reveal worked
  and never ran its "just open the folder" fallback.
- **Verify it like this** (works on any machine with the snap installed):
  `snap run --shell downloader -c 'dbus-send --session --print-reply --dest=org.freedesktop.FileManager1 --type=method_call /org/freedesktop/FileManager1 org.freedesktop.FileManager1.ShowItems "array:string:file:///home/<you>/Downloads/x" string:; echo $?'`
  → **exit 1** (AccessDenied) confined, **exit 0** unconfined; drop `--print-reply` and it is 0 both ways.
  `xdg-open` and `gio open` ARE allowed under snap, so the fallback is what makes it work.
- **Fix**: `ShellLauncher.RunChecked(timeout, …)` (waits, checks the exit code, kills a hang) +
  `--print-reply` + `ShellLauncher.OpenFolder` / `RevealInFolder` owning the fallback chain
  (default handler → `gio open`) + `AppLog.Warn` at every failure. All four folder buttons (download row,
  plugins, logs, extension dialog) now go through it.
- **`explorer.exe` returns a NON-ZERO exit code even on success** — never route the Windows reveal through
  `RunChecked`, and keep its `/select,"<path>"` argument quoted exactly as it is (the quotes are inside
  the single argument; `ArgumentList` does the outer escaping).
- **A snap's `/tmp` is a private tmpfs and `$HOME` is `~/snap/<name>/<rev>`** — a probe using `/tmp/...`
  or `$HOME/...` inside `snap run --shell` tests nothing. Use a real absolute path under the user's home.

## `System.Progress<T>` is asynchronous — never assert its value right after the await
`new Progress<double>(p => X = p)` captures `SynchronizationContext.Current` and **posts** each callback
(to the ThreadPool when there is none). So `await DoWork(new Progress<double>(...)); Assert.Equal(1.0, X);`
is a race: it passed alone and failed in the full run once an `[AvaloniaFact]` had installed the dispatcher
context. Keep `Progress<T>` in the VM — it is what stops a bound property being set from the engine's
background thread — and assert the progress contract against the SERVICE, where reports are collected
synchronously. Same family as the two other timing traps above; the tell is "passes alone, fails in the
full run, passes on re-run".

**Now there is a helper — use `TestSupport/SyncProgress<T>`, not `new Progress<T>(…)`, in any test that
ASSERTS on what was reported.** It implements `IProgress<T>` and records synchronously on the reporting
thread (`.Reports` for the snapshot). This recurred on macOS CI 2026-09-05 as
`ExtensionInstallServiceTests.Reporting_progress_reaches_one_hundred_percent` → `Assert.Contains() …
Collection: []` — not "a wrong value", **no reports at all**, which is the signature. `Unit/SyncProgressTests`
pins the mechanism with a deliberately non-pumping `SynchronizationContext`. Two other tests pass a
`Progress<T>` but never assert on it, so they are fine as they are.
Two gotchas met while writing that test: (a) do NOT `await` anything while a non-pumping context is
current — the continuation is queued into it and the test hangs to its `Timeout` rather than failing (the
same mechanism, from the other side), so make such a test non-async; (b) `Task.Run(...).GetAwaiter()
.GetResult()` in a test trips `xUnit1031`/`xUnit1051` — unnecessary anyway, since `Progress<T>.Report`
posts to the captured context no matter which thread calls it.

## Awaiting a `ReactiveCommand` from a plain `[Fact]` hangs
`ReactiveCommand` delivers on `RxApp.MainThreadScheduler`, which in a plain `[Fact]` is whatever the last
test left there and nothing pumps it — `await cmd.Execute()` then hangs to the per-test timeout. It failed
with the class run ALONE and passed alongside `AppShellStartupTests`/`DialogFlowTests` (they install a
`DeferringScheduler`), i.e. order-dependent in both directions. Pin it for the assertion
(`RxApp.MainThreadScheduler = ImmediateScheduler.Instance`, restore in `finally` — it is process-wide) or
await the underlying method directly.

## The extension installer needs a BUNDLED copy — the catalog alone is dead on arrival (2026-09-02)
- **Symptom**: "Install browser extension" installs nothing and the folder button has nothing to open;
  `~/.config/Downloader/extension/` does not exist.
- **Cause, and it is a design trap worth remembering**: the installer reads its build from
  `extension-catalog.json` on the LATEST GitHub release — an asset that only exists from the release that
  ships the feature onward. Every already-published release carries none, so on every machine today the
  catalog fetch legitimately returns empty. A feature whose whole point is "see that it worked" could not
  be verified before being shipped. Check it with
  `gh release view --repo bezzad/Downloader.Desktop --json assets --jq '.assets[].name'`.
- **Fix**: the app bundles a copy of `src/browser-extension` as `AvaloniaResource` under
  `Assets/extension/` (~110 KB) and `ExtensionInstallService.InstallBundled(target, gecko)` writes it out,
  picking `manifest.firefox.json` → `manifest.json` for gecko. **The catalog still wins when reachable**, so
  the extension keeps updating independently of the app — the bundle is the floor, not the source of truth.
  No sha256 is involved on that path and none is needed: the bytes come from the app binary, not the network.
- **Keep `ExtensionInstallService.BundledFiles` in step with `COMMON` in `scripts/build-extension.sh`** — a
  file in the zip but not the bundle makes the bundled install a browser rejects outright. Guarded by
  `Unit/BundledExtensionTests.The_bundled_file_list_matches_the_release_packaging_script`.
- Side effect: the "your app is too old for this build" state is now unreachable (the bundled copy always
  matches the running app), so that i18n key was removed from all 16 packs.
- **General lesson**: before building a feature that fetches from "the latest release", ask what it does on
  the release that introduces it. If the answer is "nothing", it cannot be tested before shipping.

## "Video downloads but has no sound" — HLS audio renditions and Opus-in-MP4 (2026-09-02)
Two independent causes, both reported as the same symptom (file plays, no audio):
- **The HLS plugin only ever downloaded the chosen `#EXT-X-STREAM-INF` variant.** In a master playlist
  whose variants carry `AUDIO="grp"`, the variant's playlist is **video-only** and the audio lives in a
  separate `#EXT-X-MEDIA:TYPE=AUDIO,…,URI="…"` rendition — the shape YouTube's HLS manifests and many CDN
  (x.com) masters use. `M3u8Parser` ignored `#EXT-X-MEDIA` entirely, so the audio playlist was never
  fetched. Now: renditions are parsed (`HlsRendition`), variants carry `AudioGroupId`/`Codecs`,
  `HlsMasterPlaylist.AudioFor(variant)` picks the group's `DEFAULT=YES` entry, and `HlsResolver` emits the
  audio segments as a **second `ConcatRecipe.StreamGroup`** — which reuses the DASH two-group path
  (concat each → `MuxAsync`) with no post-processor change. A self-contained variant still produces
  `Streams == null` (one group), byte-for-byte the old recipe.
  - `AudioFor` returns null when the variant names no `AUDIO` group *unless* its `CODECS` proves it is
    video-only (`DeclaresNoAudio`) — an absent CODECS proves nothing and must not trigger a guess.
  - A rendition with **no `URI`** means the audio is muxed into the variant; never download it.
  - fMP4 HLS (an `#EXT-X-MAP`) now sets `IntermediateExtension = ".mp4"` — labelling fMP4 as `.ts` throws
    ffmpeg's probing off (same reason DASH does it).
- **Opus audio copied into MP4** (SiteMedia/yt-dlp mux path): `-c copy` writes Opus into MP4 happily, but
  most desktop players will not decode it there. `SiteExtractor` now prefers an **MP4-native** audio
  format (`IsMp4NativeAudio`: judged on `acodec`, falling back to `ext` — extracted URLs have NO file
  extension, so any extension-based check on the DOWNLOADED file is useless) over both yt-dlp's own
  `requested_formats` pick and a higher-bitrate Opus. Opus is still used when it is the only audio.
- **Mux args are now explicit in both plugins** (`FfmpegBinary.BuildMuxArgs`, `FfmpegMuxer.BuildMuxArgs`):
  `-map 0:v:0 -map 1:a:0`. ffmpeg's default selection picks one stream per type across BOTH inputs, so a
  "video-only" file carrying a stray audio track can win and the real audio is dropped. `FfmpegBinary`
  adds `-bsf:a aac_adtstoasc` only for `.ts`/`.aac` audio (AAC in MPEG-TS is ADTS-framed and illegal in
  MP4); MP4/fMP4 audio already carries its ASC and must NOT be filtered.
- Tests: `Plugins/Hls/HlsSeparateAudioTests.cs` (parser → resolver → post-processor round trip → args) and
  `Plugins/SiteMedia/SiteMediaAudioSelectionTests.cs`. Versions bumped: HLS 2.2.1→**2.3.0**,
  SiteMedia 1.0.1→**1.1.0** (a stale version means the catalog never offers the fix).

### …and the extension's quality picker was the OTHER half of "no sound" (2026-09-02)
Diagnosed from the real record, not a guess: `~/.config/Downloader/config.json`'s `Downloads` entry for
the reported x.com item held
`https://video.twimg.com/amplify_video/<id>/pl/avc1/720x1280/<name>.m3u8` — a **rendition**, whose own
playlist maps to `/vid/avc1/…` fMP4 segments, i.e. **video only** (verified with curl; the master's
address cannot be guessed from a rendition's, `…/pl/<name>.m3u8` 404s). So the app never received the
master and had nothing to attach audio from — the HLS-plugin fix alone could not have helped that item.
Cause: `popup.js buildGroups` replaced an HLS master group's options with the parsed variants and set
`o.value = v.uri`, so "Download" sent the **rendition** URL. Now each option keeps `url` (what was
probed/deduped/thumbnailed) and gains `sendUrl` (the master) + `variantId`; `sendOption` sends those.
- **`/api/add` gained `variantId`** (JSON body, GET query, and `ToJson` for a forwarded CLI add) →
  `DownloadItem.VariantId`, which `DownloadManager.Start` already passes to the resolver. Like `path`
  it is NOT part of the extension's `hasContext`, so a plain send keeps its GET form.
- The id scheme is the plugin's own (`HlsResolver.UniqueId` = BANDWIDTH). The extension sends the
  variant's `BANDWIDTH`; an unknown/absent id makes `Pick` fall back to `Best()` — **audio always beats
  an exact quality match**, so the coupling degrades safely.
- Still unfixable by design: a rendition sniffed with **no master anywhere** (the extension deletes
  rendition rows only when it saw their master — `childUris`). Nothing in a media playlist says where
  its audio group lives, and guessing x.com's `/pl/mp4a/<bitrate>/…` sibling is exactly the
  site-specific arms race HLS 2.0.0 dropped. For those, the page URL + `com.bezzad.site-media` is the
  answer.
- Tests: `Unit/ApiVariantChoiceTests.cs` (6), 3 in `common.test.js`, and a real-browser e2e
  (`e2e/tests/hls-and-quality.spec.js`) that picks 640x480 and asserts the stub app received
  `master.m3u8` + `variantId=1200000` and NOT `high/index.m3u8`. Extension 1.8.1 → **1.9.0**.

## YouTube "403 (Forbidden)" right after a successful extraction (2026-09-02)
- **Symptom**: the extension offers the page, the app extracts it (log: "Extracted separate video+audio
  streams … will mux"), and one second later the row fails with
  `Network error: … 403 (Forbidden).` The engine log reads `File size: 0, Supports range download: False`
  — i.e. the engine's `GET bytes=0-0` size probe was ALREADY refused, so nothing about chunking,
  concurrency, cookies, referer or the UA is involved.
- **Cause**: YouTube answers an extraction through one of several internal player clients, and its CDN
  then refuses SOME of those clients' links unless the request carries a PO token this app cannot mint.
  The reported case came back as `c=WEB_EMBEDDED_PLAYER` (no `pot=` in the URL) — a client yt-dlp fell
  back to BECAUSE cookies were supplied. Which client is refused varies per video/session/date: measured
  on one box, one minute — default(`VISIONOS`) 206, `tv_simply` 206, `web`/`web_safari` 206,
  `mweb` 403, `android_vr` 403, `web_embedded` 403. So there is nothing to hard-code a preference for.
- **Diagnose in 30 seconds** — the persisted plan keeps the failing URLs:
  `python3 -c "…"` over `~/.config/Downloader/config.json` → the item's `PlanJson` → each part's URL query
  → look at `c=` and whether `pot=` is present, then `curl -s -o /dev/null -w '%{http_code}' -r 0-100 <url>`.
  Extract a comparison yourself with the INSTALLED binaries:
  `~/.config/Downloader/plugins/data/com.bezzad.site-media/yt-dlp-bin/yt-dlp --js-runtimes deno:<…/deno-bin/deno> --extractor-args "youtube:player_client=<c>" -J --no-warnings <url>`.
- **Fix (SiteMedia 1.2.0)**: `SiteMediaResolver.EnsureFetchableAsync` probes the chosen stream
  (`IMediaProbe` → one `GET bytes=0-0` with the part's own headers) and, on a 401/403/410, re-extracts
  through `YouTubeRetryClients` (`tv_simply`, then `web_safari`, cookies kept) taking the first choice the
  CDN serves; all refused ⇒ a sentence the user can act on, never a raw HTTP status. **Only YouTube is
  probed** — nowhere else has a second way to ask, so a probe there would spend a request on a question
  nothing can act on (and would put every existing resolver test on the network).
- **yt-dlp TELLS you when a 403 is coming — do not suppress its warnings.** `--no-warnings` was removed
  from `BuildArgs` for exactly this: warnings go to **stderr** (stdout stays parseable JSON, verified),
  and they carry the real reason — *"tv_simply client https formats require a GVS PO Token which was not
  provided. They will be skipped as they may yield HTTP Error 403."* `MentionsMissingToken(stderr)` logs
  it whenever an extraction succeeds. This is also why a pinned client can fail with *"Requested format
  is not available"*: yt-dlp SKIPPED that client's token-gated formats, leaving nothing to select.
- **Handing yt-dlp a signed-in session is what CAUSES the token requirement**, so the first retry drops
  it (`SiteMediaResolver.RetryWithoutCookies`). Measured on this box, one minute apart, same video:
  with the extension's real cookies the default client answers `WEB_EMBEDDED_PLAYER` → **403**; with an
  empty jar or none at all it answers `VISIONOS` → **206**. Pinned clients follow for the pages that
  genuinely need the session.
- **What is still unfixable without a PO-token provider**: a video YouTube bot-checks anonymously AND
  gates behind a token when signed in (this box's VPN IP hits that often — bot checks are per-video, see
  the 2026-08-13 note). Both retries then fail honestly. The documented answer is yt-dlp's
  `bgutil-ytdlp-pot-provider`, which is a Node/Deno service plus a yt-dlp plugin — a sizeable new
  dependency chain, NOT attempted.
- **A probe that cannot REACH the server never rejects a link** (`ProbeVerdict.Unknown`) — an offline box
  must not turn every download into "this site refused it".

## Testing a local build of an OPTIONAL plugin (no release needed)
`scripts/dev-run.sh` = build the solution in Release → copy each optional plugin's dll+deps.json into
`~/.config/Downloader/plugins/<id>/` → `dotnet run -c Release --no-build`. `--no-run` stops after the
install, `--root <dir>` targets another plugins root (the snap's lives under
`~/snap/downloader/current/.config/Downloader/plugins`), `-- <args>` are passed to the app.
- The plugins root is **per-user, not per-install** (`PluginManager.PluginsRoot`), so a `dotnet run`
  from the repo loads exactly the same installed plugins as a packaged app — that is what makes this
  work at all. Built-in plugins (GitHub, Ollama) need nothing: the app csproj stages them into its own
  output.
- **Restart is mandatory** — a plugin assembly is cached by path once loaded (see the plugin-update
  swap note above), so copying over a running app changes nothing.
- Keep the destination folder named by the plugin id: it is the plugin's identity on disk and where
  its already-downloaded tools (yt-dlp/ffmpeg/deno, hundreds of MB) live.
- A locally installed version ABOVE the catalog's is never "updated" backwards, so a dev copy survives
  the startup update check.

## Quality picker for a page sent from the extension (`/api/variants`)

The extension's page row (a YouTube/x.com page the site-media plugin claims) is a normal card built by
`popup.js buildCard`, and its `<select>` is filled from the APP, not from anything the extension can
see: `POST /api/variants {url, cookies}` → `PluginManager.GetVariantsAsync(url, ResolveOptions, ct)` →
the claiming resolver's `GetVariantsAsync` (`SiteExtractor.ListVariants`: one entry per video height +
`audio`). Points worth not re-deriving:
- **The cookies MUST travel with the question.** Listing qualities runs the same extraction as
  downloading them, so an anonymous lookup on YouTube reports "no choices" for exactly the pages this
  exists for. The endpoint writes a temp Netscape jar (`CookieFile.WriteTempFile`) and deletes it in a
  `finally`.
- **A failed lookup answers 200 with `error`**, not 500: the page can still be handed over whole and the
  app picks a stream itself, so the row falls back to one plain Download.
- **Option identity is `optionKey(opt)` = `url#variantId`**, because a page's qualities are all the SAME
  url; keying the `<select>` by url alone collapses them onto the first.
- **A picked variant forces the API path** in `common.js sendToApp` even in "dialog" add-mode — the
  legacy `/add?url=` endpoint carries a URL and nothing else, so the dialog would discard the pick.
- The app's own Add dialog still looks variants up ANONYMOUSLY (`MainViewModel` → `getVariants`), so a
  hand-pasted YouTube link shows the "needs a signed-in session" note rather than a picker. Sending the
  page from the extension is the path that has the session.


## The extension-install dialog: detection can only prove PRESENCE (2026-09-03, reported on the snap)
- **`BrowserDetector` returning an empty list does not mean the machine has no browsers.** Inside strict
  snap confinement `/usr/bin` is the BASE snap's, so a `.deb` Chrome is invisible; a snap Firefox
  (`/snap/bin`, which IS in the namespace) is not. That asymmetry is the whole bug the author reported —
  the dialog listed Firefox only, on a machine running Chrome.
- So the dialog lists **`BrowserDetector.All()`** (every supported browser, each with `IsInstalled`) and
  `RebuildTargets` always builds **both families**. Building the folder list from detected browsers meant
  a family with nothing detected got no folder, i.e. no way to install into the browser actually in use.
  `Detect()` is now just `All().Where(IsInstalled)` and stays for callers that want confirmed browsers.
- Linux lookup also searches the **vendor dirs** (`/opt/google/chrome`, `/opt/microsoft/msedge`,
  `/opt/brave.com/brave`, `/opt/vivaldi`, `/opt/opera`, `/usr/lib/firefox`, …) — a `.deb` browser's
  `/usr/bin` entry is only a symlink — and then every dir again under **`/var/lib/snapd/hostfs`**, the
  only view a confined app has of the real machine. Un-prefixed paths are preferred (those are the ones
  the app can execute); a denied probe reads as "not found".
- **There is no store button and there must not be one**: no browser accepts a locally installed unsigned
  extension, nothing is published in any store, and the extension ships inside the app — so it could only
  ever do nothing. `Ext_OpenStore`/`UseStore`/`StoreUrl` are gone (the catalog's `storeUrl` field stays in
  the JSON contract, unused). A test asserts neither row type re-grows a store member.
- **Two different "versions", and conflating them is what made this look broken**: `ExtensionTargetRow.
  InstalledVersion` = the files on disk (from `ExtensionInstallService.ReadInstalled`, no browser
  involvement, survives a restart) and `ExtensionBrowserRow.ConnectedVersion` = what that browser's
  extension reported (only the extension can say so). The dialog now shows both.
- **The connected version is matched to a row by browser ID**, so the extension's `browserLabel()`
  vocabulary MUST be a subset of `BrowserDetector.Supported`'s ids. It used to answer `chrome` for every
  Chromium fork, so a Brave/Vivaldi/Opera row read "not added yet" for ever. `labelFromUserAgent(ua,
  isGecko, isBrave)` (pure, unit-tested) now returns brave/vivaldi/opera/edge/chromium/librewolf/firefox —
  order matters, every fork's UA also contains `Chrome/`, and Brave is only detectable via
  `navigator.brave`. Pinned across the boundary by
  `BundledExtensionTests.Every_browser_the_extension_can_report_is_a_browser_the_app_lists`, which reads
  the BUNDLED `common.js`.
- **This remote container has NO .NET SDK** and the egress proxy blocks `builds.dotnet.microsoft.com`
  (403), so `dotnet build`/`dotnet test` cannot run here at all. Node/Playwright do work. When a session
  lands in that environment: say so, push to a branch, and let CI be the verification — do not report a
  C# task green from a code read.


## Reading a CI failure from a box with no .NET SDK (2026-09-03)
The remote/web container has NO .NET SDK and the egress proxy blocks
`builds.dotnet.microsoft.com` (403), so `dotnet build`/`dotnet test` cannot run at all there and CI is
the only test loop. Two things make that loop cheap:
- **Get failure NAMES from the log, not the artifact.** `test-results.trx` lives in an artifact whose
  blob-storage URL the proxy also blocks. Instead call `get_job_logs` with `return_content: true` and
  `tail_lines: 2100`: the result EXCEEDS the context limit, so the tool SAVES IT TO A FILE and prints the
  path — then grep that file (`s.replace('\\n','\n')` first; the log arrives as one long line) for
  `[FAIL]`. A [FAIL] block carries the assertion, the expected/actual and the source line. Do NOT page
  through `tail_lines` guessing: the block sits wherever the test ran, often >300 lines from the end.
- **Read the other legs before theorising.** `list_workflow_jobs` with `filter: latest` shows all six at
  once; "4 green, 2 red on one OS" points somewhere completely different from "6 red".
- **`Total tests: Unknown` + `Passed: N` + a `hangdump.dmp` is the KNOWN in-host crash, not a test
  failure** (see the notes above). It aborts the run wherever it happens to be — 2026-09-03 it named
  `TransferPathTests.Transfer_backed_item_completes_with_the_produced_file` on both Windows legs while
  ubuntu×2 and macOS×2 were fully green, and the base commit (`develop` 6cda586) carried the same
  crashdump. The log says it itself: "This test may, or may not be the source of the crash."
- Platform-separator trap worth repeating: a Unix/macOS path lookup must join with `'/'`, never
  `Path.Combine` — on the Windows leg that produces a path shape neither platform has, and the lookup's
  own tests then fail there for a reason unrelated to the lookup (`BrowserDetector.UnixJoin`).

## The local API served ONE request at a time — a slow route took the whole extension down (2026-09-03)
- **Symptom (reported on x.com)**: the popup lists the detected media fine, but clicking Download leaves
  the button on "…" for ever and NOTHING arrives in the app. Meanwhile the same click on a YouTube page
  works and reads "Sent". The tell that separates this from an add the app refused: a refusal says
  **"Failed"**, and an app that is not listening says **"Downloader was not found on ports 15151–15155"**.
  A button stuck on "…" with neither message means nothing ever answered — and so does a status dot that
  is neither green nor accompanied by the not-found line, because `refreshStatus()` is still pending too.
- **Cause**: `LocalApiService.AcceptLoopAsync` **awaited** each request before accepting the next. One
  slow route therefore blocked every caller, `/ping` included. The slow route is `/api/variants`, which
  runs the site tool (yt-dlp) and ran with `CancellationToken.None` — so a lookup started from a YouTube
  popup could still be holding the listener minutes later, when the user had moved to another tab. Fixed
  by dispatching each context WITHOUT awaiting (`_ = HandleContextAsync(ctx)`, which never throws) and
  giving the variants lookup the same 90 s valve the Add window has (`VariantLookupTimeout`, an internal
  test seam). Regression: `Integration/ConcurrentApiRequestTests` — both tests fail on the old code.
- **The extension had no deadline of its own either**, which is what turned a stalled app into a hang
  rather than an error: every app-facing fetch now goes through `common.js appFetch(url, init, timeoutMs)`
  (`APP_TIMEOUT_MS` = ping 2 s / add 20 s / ask 8 s / variants 120 s). It BOTH aborts and races a timer —
  the abort releases the socket, the race means a fetch that ignores the signal (or a stubbed one in the
  node tests) still cannot wedge the caller.
- **A background message handler must always answer.** `background.js`'s `onMessage` IIFE had no catch, so
  a throw closed the channel with no response; the popup's `const { ok } = await send(...)` then threw on
  destructuring `undefined` and left the button mid-state. Both ends are fixed: the handler always calls
  `sendResponse`, and `sendOne` treats a missing answer as a failure and re-enables the button so a retry
  is possible. Any new message type must keep both halves.

## "Available" for the extension = catalog OR the app's own bundled copy (2026-09-04)
- **The reported gap**: the install dialog printed "Files installed: v1.11.0" beside the available version
  and never drew the conclusion, so a stale copy looked identical to a current one; and the startup check
  (`MainViewModel.CheckExtensionUpdateAsync`) returned early on an empty catalog — which is EVERY machine
  today, since no published release carries `extension-catalog.json`. Net effect: nothing anywhere could
  ever say "your extension is out of date".
- **Fix**: `ExtensionCatalogService.Newer(a, b)` / `BestAvailable(catalog, bundledVersion)` — the bundled
  copy is a first-class source of "what could be installed", not just a fallback for a missing catalog.
  `ShouldWarnAboutExtension` now takes the bundled version; `ExtensionTargetRow` gained `UpdateAvailable`
  /`UpdateText` (files on disk vs best available, independent of any browser having called), the footer
  button switches to `Ext_Update` ("Update the files"), and `SettingViewModel.ExtensionHintText` says it
  from the Settings row without opening the dialog.
- **`ExtensionTargetRow.IsBundled` now also means "the bundled copy is NEWER than the catalog entry"** —
  right after an app update it routinely is, and installing the catalog's older build over it would be a
  downgrade dressed up as an install.
- **Updating IS re-installing into the SAME folder** (`InstallBundled` stages `<target>.new` and swaps),
  and that path must never move: a browser derives an unpacked extension's identity from its absolute
  path, so a new folder = a different extension with an empty settings store. What the app CANNOT do is
  make the browser re-read it — an unpacked extension stays loaded until it is reloaded or the browser
  restarts — so a successful update sets `Notice = Ext_ReloadAfterUpdate` saying exactly that.
- **TEST TRAP, and it bit for real**: a test that builds an `ExtensionInstallViewModel` without stubbing
  BOTH `bundledVersion` and `installBundled` can take the bundled path (the real app copy outranks a
  stubbed catalog) and run the REAL installer — which wrote into the developer's own
  `~/.config/Downloader/extension/`. Stub both in every VM test, and note `Vm(...)` in
  `Unit/ExtensionInstallViewModelTests` defaults `bundledVersion` to `"0.0.1"` so catalog-focused tests
  are not steered by whatever version the app happens to ship.

## A CI guard that diffs the PUSH is wrong on `main` — diff what it actually means (2026-09-04)
- `extension.yml`'s bump guard ("extension code changed but the manifest version is already on AMO")
  compared `github.event.before` → `GITHUB_SHA`. On `develop` that is one push's worth of commits and
  the check is right. On **`main` the push is the release merge**, so the base is the PREVIOUS release
  and the span covers every extension commit since then — while the manifest already carries the
  version the `develop` run published to AMO minutes earlier. It therefore failed on EVERY release
  (2026-09-02, -03, -04) for code that WAS published. A permanently red check is how a real AMO
  failure goes unnoticed.
- Fix: the base is the commit that **last SET the current manifest version** (walk
  `git log --format=%H $GITHUB_SHA -- manifest.firefox.json` newest→oldest while the version at that
  commit still equals the current one; the last such commit is the bump). Then the question is "has
  anything changed since the bump?", which is branch- and event-independent. Needs `fetch-depth: 0`.
- **Don't use `git log -S"…"` for this**: the pickaxe matches any commit where the string's COUNT
  changed — including the commit that REMOVED that version — so it can resolve to the wrong commit.
- Verify a workflow step without waiting for the next release: extract the step's `run` from the YAML
  (`yaml.safe_load` → the step's `run`), and execute it locally with `GITHUB_SHA` pointed at the exact
  commit that failed, plus synthetic commits in a throwaway `git worktree` for the negative cases. Then
  make the step runnable on `workflow_dispatch` and dispatch it once for a real on-runner proof.

---
name: downloader-desktop
description: Build, run, test and develop the Downloader.Desktop app (Avalonia/.NET download manager). Use for any task in this repo — launching the GUI, running the test suite, regenerating screenshots, or implementing features against the architecture below.
---

# Downloader.Desktop

Cross-platform (Windows/Linux/macOS) Avalonia + .NET 10 GUI for the `Downloader` multipart-download engine. MVVM with ReactiveUI. Original modern-minimal design (ocean-blue/teal, light+dark). End-user focused: simple, stable, sensible defaults.

All commands run from the **`src/`** folder (where `Downloader.Desktop.sln` lives).

## Maintaining this skill (read first, every session)
Treat this file as a living cache. **Whenever you discover something non-obvious that a future session would otherwise have to re-derive** (an engine API shape, a gotcha, a settled design choice), append a concise boilerplate note here. The goal is *steadily fewer tokens per session*: each future run should read the answer here instead of re-grepping the codebase or the sibling `../Downloader` engine. Keep additions short and factual — a few lines, not essays. Prune notes that become wrong. This is an explicit standing instruction from the author.

**Never commit automatically.** Make edits (skill, code, docs) in the working tree and leave them staged/unstaged for the author to review; only run `git commit`/`git push` when the author explicitly asks. (This overrides any general "commit the skill change automatically" guidance.)

## Engine (`Downloader` 5.8.0) quick reference — sibling repo `../Downloader` is exactly this version
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
- **`HttpClientTimeout` is the WHOLE-request timeout** (`HttpClient.Timeout`), incl. reading a chunk's body — keep it large (default 100 s). Setting it small (e.g. 10 s) makes longer chunks fail with "Operation Cancelled" after retries (~1 min). Per-block stalls are handled by `BlockTimeout`, not this.
- **Cancellation vs failure status**: the engine raises `DownloadFileCompleted` with `Cancelled=true` for BOTH a user pause/stop and an internal abort (e.g. timeout). Disambiguate by the status we set *before* calling the engine: if it's already Paused/Stopped it was the user; a cancel while still Running = real failure → mark Failed.
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
- **Status badge colors** live in `Converters/StatusToBrushConverter`: Running teal, Completed green, Failed red, Paused amber, Stopped neutral-gray, Queued steel-blue (`#4F6D9C` — deliberately distinct from Stopped's gray so a waiting vs stopped row is tellable apart). Badge shows for every state except Running (which shows live `%`); `DownloadItemViewModel.ShowStatusBadge` = `Status != Running`.

## Packaging / publish
- Self-contained, dependency-free single file: `dotnet publish -r <rid> --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true` (the last flag is required so Skia/native libs are bundled). Validated ~49 MB ELF.
- The output binary is `Downloader.Desktop[.exe]` (= project name). Renaming the *file* to `Downloader` is safe — `avares://Downloader.Desktop/...` uses the embedded assembly name, not the file name, so don't change `AssemblyName` (that WOULD break every avares URI).
- CI/release live in `.github/workflows/` (`dotnet-desktop.yml` = build+test on push/PR, `release.yml` = matrix publish on `v*` tag → **creates** the GitHub Release for the tag and attaches zip/tar.gz). Local: `scripts/publish.sh [rid ...]`.
- **Release matrix RIDs**: `win-x64`, `linux-x64`, `osx-x64`, `osx-arm64` (only macOS ships both arches; Windows/Linux are x64-only). Versioning is automatic — `VersionPrefix` (major.minor) is hand-set in the csproj (currently `1.0`), build/revision derived from build time. **To release**: ensure `main` is green + pushed, then `git tag vMAJOR.MINOR.PATCH && git push origin <tag>` (match major.minor to `VersionPrefix`). `softprops/action-gh-release` creates the Release if absent; re-running needs the tag + Release deleted first.

## Avalonia 12 gotchas worth caching
- **DataGrid cell focus/current border**: there is NO named `FocusVisual`/`CurrencyVisual` element in v12. The current/focus outline is on an unnamed template `Border`; kill it with `DataGridCell:current /template/ Border` + `DataGridCell:focus /template/ Border` → `BorderThickness=0`/`BorderBrush=Transparent` (and `Rectangle` Stroke for safety). `Focusable=False` does NOT remove it. Full-row selection highlight comes from the Fluent theme's `:selected` default.
- **DataGrid grouping kills row virtualization** → janky scroll/UI past ~10 rows. Keep the `DataGridCollectionView` flat (no `GroupDescriptions`) for performance.
- **Single-line `TextBox` strips newlines on paste** (`AcceptsReturn=false`), merging pasted multi-line input. For multi-URL paste, set `AcceptsReturn="True"` + a `KeyDown` handler that fires the action on Enter (Shift+Enter = newline).
- **Reveal-a-file-in-folder** cross-platform: Windows `explorer /select,"path"`, macOS `open -R path`, Linux `dbus-send … org.freedesktop.FileManager1.ShowItems array:string:file://path string:` (fallback: open the directory).
- **Custom window chrome**: `ExtendClientAreaChromeHints` was **removed in Avalonia 12** (compile error AVLN2000). Use only `ExtendClientAreaToDecorationsHint="True"` + `ExtendClientAreaTitleBarHeightHint="-1"`, then draw your own bar (see `Views/TitleBar`). OS resize/snap still works. Drag = `host.BeginMoveDrag(e)` on left-button `PointerPressed`; get the window via `TopLevel.GetTopLevel(this) as Window`.
- All three windows (MainWindow, AddDownloadItemView, DownloadDetailsView) use `TitleBar`; dialogs set `ShowMinMax="False"`.
- **Esc-to-close dialogs**: with `WindowDecorations="None"` there's no native close-on-Esc. Override `OnKeyDown` on the dialog window and `Close()` on `Key.Escape` (see `DownloadDetailsView`). A focused `TextBox` doesn't swallow Esc, so the window-level override is enough.

## Build / run / test
```bash
dotnet build Downloader.Desktop.sln                                   # 0 warnings / 0 errors expected
dotnet run  --project Downloader.Desktop/Downloader.Desktop.csproj    # launch the GUI (needs a desktop session)
dotnet test Downloader.Desktop.Tests/Downloader.Desktop.Tests.csproj  # 50 unit + headless UI + integration tests
```
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

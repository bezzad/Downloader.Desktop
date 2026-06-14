---
name: downloader-desktop
description: Build, run, test and develop the Downloader.Desktop app (Avalonia/.NET download manager). Use for any task in this repo — launching the GUI, running the test suite, regenerating screenshots, or implementing features against the architecture below.
---

# Downloader.Desktop

Cross-platform (Windows/Linux/macOS) Avalonia + .NET 10 GUI for the `Downloader` multipart-download engine. MVVM with ReactiveUI. Original modern-minimal design (ocean-blue/teal, light+dark). End-user focused: simple, stable, sensible defaults.

All commands run from the **`src/`** folder (where `Downloader.Desktop.sln` lives).

## Maintaining this skill (read first, every session)
Treat this file as a living cache. **Whenever you discover something non-obvious that a future session would otherwise have to re-derive** (an engine API shape, a gotcha, a settled design choice), append a concise boilerplate note here in the same edit/commit. The goal is *steadily fewer tokens per session*: each future run should read the answer here instead of re-grepping the codebase or the sibling `../Downloader` engine. Keep additions short and factual — a few lines, not essays. Prune notes that become wrong. This is an explicit standing instruction from the author.

## Engine (`Downloader` 5.8.0) quick reference — sibling repo `../Downloader` is exactly this version
- `DownloadBuilder` is **single-URL only** (`WithUrl(string)`) and its `IDownload` **cannot take a logger** (no `AddLogger` on `IDownload`). For mirrors and logging, use `DownloadService` directly instead of the builder.
- `DownloadService(DownloadConfiguration cfg, ILoggerFactory factory = null)` — implements `IDownloadService`: same events (`DownloadStarted/DownloadProgressChanged/ChunkDownloadProgressChanged/DownloadFileCompleted`), plus `Package`, `Pause()`, `Resume()`, `CancelAsync()`/`CancelTaskAsync()`, `Clear()`, and `AddLogger(ILogger)`.
- **Multi-URL / mirrors** are first-class: `DownloadFileTaskAsync(string[] urls, DirectoryInfo folder, ct)` (auto-resolves name), `(string[] urls, string fileName, ct)`, and package overloads. `DownloadPackage.Urls` is `string[]`. So the data model should carry `List<string> Urls` (first = primary, rest = mirrors), not a separate `Url` + `Mirrors`.
- Filename still auto-resolves from URL/Content-Disposition; read it from `DownloadStartedEventArgs.FileName` (full path).

## Avalonia 12 gotchas worth caching
- **Custom window chrome**: `ExtendClientAreaChromeHints` was **removed in Avalonia 12** (compile error AVLN2000). Use only `ExtendClientAreaToDecorationsHint="True"` + `ExtendClientAreaTitleBarHeightHint="-1"`, then draw your own bar (see `Views/TitleBar`). OS resize/snap still works. Drag = `host.BeginMoveDrag(e)` on left-button `PointerPressed`; get the window via `TopLevel.GetTopLevel(this) as Window`.
- All three windows (MainWindow, AddDownloadItemView, DownloadDetailsView) use `TitleBar`; dialogs set `ShowMinMax="False"`.

## Build / run / test
```bash
dotnet build Downloader.Desktop.sln                                   # 0 warnings / 0 errors expected
dotnet run  --project Downloader.Desktop/Downloader.Desktop.csproj    # launch the GUI (needs a desktop session)
dotnet test Downloader.Desktop.Tests/Downloader.Desktop.Tests.csproj  # 30 unit + headless UI tests
```
Headless smoke check (no display interaction): `timeout 10 dotnet run --project Downloader.Desktop/Downloader.Desktop.csproj` — a clean 10s run (SIGTERM/143) with no exceptions means it launched OK. Note: empty-list startup does NOT exercise row/file-kind icons.

## Regenerate README screenshots
A gated headless test renders real PNGs to `docs/screenshots/` (home-dark, home-light, settings-dark):
```bash
DLDESKTOP_CAPTURE=1 dotnet test Downloader.Desktop.Tests/Downloader.Desktop.Tests.csproj --filter FullyQualifiedName~CaptureScreenshots
```
Then verify the PNGs by viewing them. Capture uses the real `App` with `.UseSkia().UseHeadless(UseHeadlessDrawing=false)`.

## Architecture (where things live)
- `Services/DownloadManager` (DI singleton `IDownloadManager`): owns the master `ObservableCollection<DownloadItemViewModel>`, builds `IDownload` via `DownloadBuilder`, marshals engine events to the UI thread (throttled ~5fps), queue concurrency + `DispatcherTimer` scheduler, `StatsChanged`/`ListChanged` events.
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

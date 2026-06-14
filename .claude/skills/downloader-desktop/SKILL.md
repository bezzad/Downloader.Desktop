---
name: downloader-desktop
description: Build, run, test and develop the Downloader.Desktop app (Avalonia/.NET download manager). Use for any task in this repo — launching the GUI, running the test suite, regenerating screenshots, or implementing features against the architecture below.
---

# Downloader.Desktop

Cross-platform (Windows/Linux/macOS) Avalonia + .NET 10 GUI for the `Downloader` multipart-download engine. MVVM with ReactiveUI. Original modern-minimal design (ocean-blue/teal, light+dark). End-user focused: simple, stable, sensible defaults.

All commands run from the **`src/`** folder (where `Downloader.Desktop.sln` lives).

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

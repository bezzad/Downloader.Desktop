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
- The author steers and gives feedback; fold it in and keep this file current.

## Stack
- **.NET 10** (`net10.0`); macOS build target switches to `net8.0-macos` when `IsMacBuild=true`.
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

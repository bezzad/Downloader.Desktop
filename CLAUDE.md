# CLAUDE.md — Downloader.Desktop

Cross-platform desktop GUI (Windows/Linux/macOS) for the [Downloader](https://github.com/bezzad/downloader) multipart download library. Status: **early dev — no production release yet**. A full **V1 redesign** is implemented on branch `feat/v1-redesign` (awaiting the author's interactive testing + merge).

## Product vision
- **Goal**: a GUI download manager exposing the `Downloader` engine's features (multipart, pause/resume, speed control, etc.) to **end users, not developers**.
- **Audience**: non-technical people on Windows / Linux / macOS. Must be **stable, simple, self-explanatory** — no exposed/complex config, sensible defaults.
- **Author owns the engine**: `bezzad` developed the `Downloader` library (https://github.com/bezzad/downloader); this app is the UI layer on top of it.
- **Reference apps** (study for UX/feature inspiration):
  - ab-download-manager — https://github.com/amir1376/ab-download-manager / https://abdownloadmanager.com/ (primary visual + UX reference; built in Kotlin/Compose Multiplatform).
  - Internet Download Manager (IDM) — the global gold standard, but far larger/more feature-heavy than we need.
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
- **Visual style** = **inspired by ab-download-manager, NOT a copy**. Modern minimal: clean rounded cards, accent color, light/dark, friendly empty states; aimed at non-developers (not the dense IDM table look). **I have creative freedom to design a distinctive, special look** — propose mockups, don't clone. Keep it simple/understandable above all.
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
   - *Remaining (post-V1):* browser/clipboard capture, categories, per-download (not just per-queue) scheduling UI, request certificates/cookies/credentials in Settings, packaging/installers.
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

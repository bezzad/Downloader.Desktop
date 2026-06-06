# CLAUDE.md — Downloader.Desktop

Cross-platform desktop GUI (Windows/Linux/macOS) for the [Downloader](https://github.com/bezzad/downloader) multipart download library. Status: **early dev — no production release yet**; many commands are stubs.

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
- **Visual style** = **Modern minimal, "ab-download-manager" style**: clean rounded cards, accent color, light/dark, friendly empty states. Aimed at non-developers (not the dense IDM table look).
- **Tech stack** = **Stay on Avalonia + .NET** (author was open to alternatives; this is the recommended path). Rationale:
  - Reuses the existing **.NET `Downloader`** engine directly.
  - **Avalonia keeps Linux desktop** AND offers a mobile path (iOS/Android/Browser) for the next phase.
  - **.NET MAUI** was considered but **drops Linux desktop**, conflicting with the goal; non-.NET (Kotlin/Compose) would abandon the engine. Revisit only if the author asks.
- **Mobile**: framework chosen now (Avalonia) must support it; first mobile platform decided later. Avoid desktop-only architectural lock-in.

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
1. **Wire the engine into the list**: make `DownloadItemViewModel` track a live `IDownload` — bind progress %, speed, downloaded/total from the `Downloader` events (`DownloadProgressChanged`, `DownloadFileCompleted`). Fix the integer-division `Status` bug.
2. **Per-item actions**: implement pause / resume / cancel / remove and reflect real status (queued, downloading, paused, completed, failed).
3. **Bulk actions**: implement the stubbed `StartAll` / `StopAll` / `ClearAllStoppedItems`.
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

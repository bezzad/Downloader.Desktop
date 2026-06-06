# CLAUDE.md — Downloader.Desktop

Cross-platform desktop GUI (Windows/Linux/macOS) for the [Downloader](https://github.com/bezzad/downloader) multipart download library. Status: **work in progress / "coming soon"**, so many commands are stubs.

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

## Conventions
- Git user: `bezzad`. Main branch: `main`.
- C#: `LangVersion=latest`, file-scoped namespaces, `Avalonia`/`ReactiveUI` idioms (`RaiseAndSetIfChanged`, `ReactiveCommand.CreateFromTask`).
- Keep this file updated when structure changes to minimize re-exploration.

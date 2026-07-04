## Why

The Language combobox in Settings renders each option's flag from a tiny 30×20px PNG (`Assets/flags/{code}.png`) displayed at 22×15. On HiDPI displays, where the control renders at 2x–3x device pixels, these small source bitmaps look blurry and blocky. Non-technical users notice this immediately when opening the language picker — it undercuts the "clean, modern" visual bar the app holds itself to everywhere else.

## What Changes

- Replace all 16 flag PNGs in `src/Downloader.Desktop/Assets/flags/` (`en, fa, es, fr, ar, eo, tr, zh, ja, hi, ru, ko, pt, de, it, az`) with higher-resolution source images (e.g. 60×40 or vector-derived raster at 3x the display size) so they render crisply at both 1x and HiDPI scale factors.
- Keep the existing lazy-load pattern in `Localizer.LanguageOption.Flag` (`avares://Downloader.Desktop/Assets/flags/{Code}.png`) and the existing 22×15 display size in `SettingView.axaml` — only the asset quality/resolution changes, not the loading mechanism or layout.
- No new language codes are added; this is an asset-quality fix for the existing 16.

## Capabilities

### New Capabilities
(none)

### Modified Capabilities
- `ui-theme`: language flag icons must be high enough resolution to render crisply (not blurry/blocky) at their display size, including on HiDPI displays.

## Impact

- `src/Downloader.Desktop/Assets/flags/*.png` — 16 files replaced.
- `src/Downloader.Desktop/Downloader.Desktop.csproj` — no changes expected (assets already globbed as `AvaloniaResource`).
- `Downloader.Desktop.Tests/AppTests.cs` — existing `Flag != null` assertion should still pass unchanged; may add a lightweight pixel-dimension assertion.
- Docs/screenshots: none of the existing README screenshots crop into the language combobox, so no screenshot refresh is expected, but verify during implementation.

## Why

The tray icon's menu is an OS-drawn `NativeMenu`. On Ubuntu GNOME the right-click menu does not open
at all (a long-open bug), and where it does open it looks plain and out of place: no icons, no theme,
English-only labels ("Open Downloader", "Quit Downloader" are hard-coded, not translated). The author
wants a menu with a proper look — a small rounded card with icon rows, in the app's theme — opened by
clicking the tray icon. (Task 10 of the author's UI batch of 2026-09-28; independent of the other four
changes.)

## What Changes

- **Linux and Windows: clicking the tray icon (left or right) opens the app's own menu window**, drawn
  by the app: rounded card, one row per item with an icon, hover highlight, theme colors (light/dark +
  accent), translated labels. It opens beside the tray (above the taskbar / below the top bar) and
  closes when it loses focus, on Esc, or after an item is chosen.
- **macOS keeps the native menu-bar menu** (the platform norm, and it works) — with the same items and
  translated labels.
- **Items**: Show downloads · Settings · Notifications (on/off switch) · Exit.
  - Show downloads = bring the main window back on the downloads list.
  - Settings = bring the main window back on the Settings page.
  - Exit = quit the app (same path as today's "Quit", so a pending update still applies).
- **All tray labels are translated** (16 language packs).
- **Step 1 is an on-device diagnosis on the author's Ubuntu machine** (the skill forbids another
  code-only tray change): a diagnostic build logs which tray events actually arrive (left/right click,
  with and without a native menu attached). The design of step 2 follows that evidence. Work stops at
  step 1 until the result is back.
- **BREAKING (behavioural):** on Linux/Windows a tray click no longer restores the window directly; it
  opens the menu, whose first item restores it.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `system-tray`: a click opens the app-drawn menu (Linux/Windows) or the native menu (macOS); the menu
  items and their behaviour; translated labels; "one click restores the window" becomes "one click
  opens the menu, whose first item restores the window".

## Impact

- `Services/TrayService.cs` — per-platform menu choice; click → show the popup; translated labels.
- New `Views/TrayMenuView.axaml(.cs)` + `ViewModels/TrayMenuViewModel.cs` (items, commands).
- New pure placement helper (where the popup goes on a given screen).
- `ViewModels/MainViewModel.cs` — "show on page X" entry point used by the menu (reuses `Navigate`).
- All 16 packs in `Assets/i18n/` (tray labels).
- Tests in `UI/`/`Unit/`; `docs/screenshots/` — a capture of the menu, light + dark.
- A skill-file note with the on-device findings (whatever they turn out to be).

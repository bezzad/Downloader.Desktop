## 1. Mockup first (repo rule)

- [x] 1.1 Rendered real mockups (headless Skia, gated `CaptureNotchMockups` test →
  `mockup-collapsed.png` / `mockup-expanded.png` in this change folder). Author asked to apply all tasks
  unattended, so implementation proceeded with this default look — review the PNGs / the live overlay and
  request visual tweaks in re-test.

## 2. Overlay window & service

- [x] 2.1 `Services/NotchService`: Start/Stop (idempotent, fail-soft like TrayService); positioning lives
  in `NotchView.Reposition` (horizontal center of the primary screen, Y = top screen edge).
- [x] 2.2 `Views/NotchView.axaml(.cs)`: borderless topmost transparent window (`SystemDecorations=None`,
  `ShowInTaskbar=false`, `ShowActivated=false`, `Focusable=false`), dark pill with rounded BOTTOM corners
  only (notch look); collapsed (170×34: clock + ↓speed chip) ⇄ expanded (380×190: header + up to 3 running
  rows with thin progress bars + "and N more…") swapped by `IsExpanded` bindings; hover expands, ~300 ms
  delayed collapse (skim-past no-flicker); click = bring the main window to front (`TrayService.ShowWindow`).
- [x] 2.3 `ViewModels/NotchViewModel`: 1 s clock `DispatcherTimer`, `StatsChanged` subscription, top-3
  running rows (rebuilt only on membership change — rows self-update), `Notch_More` overflow, aggregate
  `TotalSpeedText`; `IDisposable` unhooks everything.

## 3. Settings & lifecycle

- [x] 3.1 `DownloadSettings.EnableNotch` (default off) + Settings toggle (below the tray toggle) with
  hint; `SettingViewModel.EnableNotch` starts/stops live; `MainViewModel.SetupAppShell` starts it when
  enabled (independent window → stays up in close-to-tray).
- [x] 3.2 `Set_Notch`/`Set_Notch_Hint`/`Notch_More`/`Notch_NoDownloads` translated in ALL 16 packs
  (drift-checked).

## 4. Tests & verification

- [x] 4.1 `Notch_vm_lists_top_rows_with_overflow_and_total_speed`, `Notch_vm_is_quiet_when_idle`.
- [x] 4.2 `Notch_window_builds_and_toggles_expanded_state`, `Notch_service_starts_and_stops_fail_soft`
  (+ the gated mockup capture). Suite 221/221 green.
- [ ] 4.3 **Author on-device verification:** macOS (pill under the notch/menu-bar line), Windows/Linux
  (top-center, hover expand, click-to-open); Linux WM variance is best-effort like the tray. Stays
  in-progress until confirmed.

## 5. Docs

- [x] 5.1 README feature bullet added ("Dynamic Island (notch)"); mockup PNGs live in this change folder
  (a docs screenshot/GIF can follow the author's visual approval); SKILL.md gets the overlay-window note.

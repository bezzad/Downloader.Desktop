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
- [x] 4.4 **(reprocess — author feedback)** On macOS the pill sat BELOW the menu bar instead of at the
  physical notch (webcam housing) like boring.notch/NotchNook. Fixed: the NSWindow level is raised above
  the menu bar via objc interop (`setLevel: 26` = NSStatusWindowLevel+1) with
  `setCollectionBehavior: canJoinAllSpaces|stationary|fullScreenAuxiliary`, positioning centers on the
  FULL screen bounds (the hardware notch is at the physical center) with Y = screen top, and the macOS
  collapsed width is 200 (≈ notch width) so the pill visually merges with it. Win/Linux unchanged.
- [x] 4.5 **(reprocess — author feedback: "empty rectangle when collapsed")** With the pill now AT the
  physical notch, centered content hid BEHIND the webcam housing. Collapsed layout redesigned as WINGS
  flanking a hardware-sized center gap (185px on macOS, collapses elsewhere): left = Downloader logo +
  live ↓speed, right = total percent (average of active downloads) + clock; collapsed width 340 (mac).
  Expanded now lists **running AND paused** items (paused show "62% · Paused" via StatusText), gets the
  logo + totals in the header, and roomier line spacing. Mockups regenerated in this folder.
- [x] 4.3 **Author-verified on-device (2026-07-05):** the pill sits at the macOS notch with visible
  wings; after two rounds of author-driven layout polish (wings around the hardware cutout, then
  percent-left / speed-right with the collapsed clock kept Win/Linux-only), the author confirmed the
  island looks and behaves right ("it is good") and archived it explicitly.

## 5. Docs

- [x] 5.1 README feature bullet added ("Dynamic Island (notch)"); mockup PNGs live in this change folder
  (a docs screenshot/GIF can follow the author's visual approval); SKILL.md gets the overlay-window note.

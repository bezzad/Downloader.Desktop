## Context

- `Services/TrayService` builds an Avalonia `TrayIcon` with a `NativeMenu` (Open / Disable notifications
  / Quit, English literals) and subscribes `Clicked → ShowWindow`. Icon is downscaled to 64×64.
- On Linux the tray is a StatusNotifierItem over D-Bus; the menu is a D-Bus menu drawn by the shell.
  On Ubuntu GNOME (AppIndicator extension) the menu does not open; per the code comment `Clicked`
  "usually never fires" there.
- **Skill rule (downloader-desktop, "Linux tray … DO NOT make speculative changes")**: this code
  flip-flopped three times; one guess made the icon disappear. Any change needs on-device evidence.
  Settled: keep the icon small; the `Clicked` subscription must stay unconditional.
- Windows: Shell_NotifyIcon; `Clicked` fires on left click; right click shows the native menu.
- macOS: status item with a native menu — works.

## Goals / Non-Goals

**Goals:** a themed, translated tray menu on Linux/Windows opened by left or right click; the same
items natively on macOS; decided from evidence, not guesses.

**Non-Goals:** download progress or a download list inside the tray menu; changing the icon; changing
close-to-tray behaviour.

## Decisions

1. **Diagnose first (task group 1, on the author's machine).** Add a temporary diagnostic switch
   (env var, e.g. `DLDESKTOP_TRAY_DIAG=1`) that logs every tray event Avalonia raises, and a variant
   that attaches NO native menu. The author runs: (a) as today, (b) no-menu variant; left-click and
   right-click in each; and sends the log plus `dbus-monitor "interface='org.kde.StatusNotifierItem'"`
   output. Outcomes and what they mean:
   - `Clicked` arrives for left and/or right click in the no-menu variant → the popup plan below works
     as designed.
   - Nothing arrives in either variant → the shell only offers a D-Bus menu; fall back to a native
     menu that at least opens (the diagnosis also tells us why it doesn't today) and record the
     finding; the themed popup then ships for Windows (and any Linux shell that does report clicks),
     and GNOME keeps a native menu — with the author's OK, since that is less than was asked.
   The diagnostic switch is removed (or kept only as logging) before the change is finished.
2. **Custom popup = a small borderless `Window`** (`TrayMenuView`): `WindowDecorations=None`, `Topmost`,
   `ShowInTaskbar=false`, transparent + rounded root border (the app's rounded-window pattern), rows
   styled like the app's context menus (App.axaml menu styles), icon + label per row, the notifications
   row carrying a `ToggleSwitch`. Closes on `Deactivated`, Esc, or after a command. One instance,
   reused.
3. **Which click**: both left and right open the popup wherever the platform reports them. On Windows
   the native menu is not attached, so a right click also reaches us as a click (confirmed in the
   Windows smoke test; if not, right click keeps a native menu with the same items as a fallback).
4. **Placement is a pure function**: `TrayMenuPlacement.Place(screenBounds, workingArea, menuSize)` →
   top-left point. The taskbar/top-bar side is where `WorkingArea` is inset from `Bounds` (bottom on
   default Windows, top on GNOME, left/right for side taskbars); the menu goes into the corner on that
   side, at the right end (left end in RTL). No inset found → bottom-right. Avalonia does not give the
   icon's own position, so "near the tray area" is the honest target. Unit-tested for all four sides.
5. **macOS keeps `NativeMenu`** with the same four items (the switch becomes a checkable-looking
   label swap, as today — `NativeMenuItemToggleType` does not exist in Avalonia 12, skill note).
6. **Items call the existing paths**: Show downloads → `WindowActivation.BringToFront` +
   `Navigate(Downloads)`; Settings → same + `Navigate(Settings)`; Notifications → the existing toggle
   (`NotificationService.Enabled` + `NotificationsToggled` persist callback); Exit → the existing
   `_onQuit` (keeps the update-on-exit guarantee). Navigation is marshalled to the UI thread (tray
   events can arrive on a D-Bus thread — skill note).
7. **Labels via `Localizer`** (`Tray_ShowDownloads`, `Tray_Settings`, `Tray_Notifications`, `Tray_Exit`),
   refreshed on language change; native macOS item headers are rebuilt on change.

## Risks / Trade-offs

- [GNOME never tells the app about the click] → decision 1 finds this out before any UI work; the
  fallback is written down, not improvised.
- [Popup steals focus / hides behind other windows on Wayland] → `Topmost` + activate; checked on
  device. Wayland may refuse explicit positioning; then the compositor places it — accepted.
- [Losing "one click restores the window"] → "Show downloads" is the first row, directly under the
  pointer's area; the change is called out as breaking.
- [Can't test a real tray headlessly] → the VM (items, commands), placement and label refresh are
  unit/headless-tested; the view is rendered for the screenshot; the tray wiring is verified on device.

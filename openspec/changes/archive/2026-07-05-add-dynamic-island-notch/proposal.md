## Why

The author wants a "Dynamic Island"-style download hub that lives at the top-center of the screen —
inspired by the macOS notch apps (reference video:
https://x.com/Yappologistic/status/2073277276985475278/video/1 and the Swift implementation
https://github.com/TheBoredTeam/boring.notch). Today the only at-a-glance surfaces are the main
window and the tray icon; neither gives a glanceable, always-available live download readout while
the user works in other apps.

- **macOS:** blend with the physical notch — a slim pill hugging the notch/menu-bar area that
  expands on hover.
- **Windows / Linux:** no hardware notch, so *draw* one: a small top-center strip showing the system
  time; hovering expands it into a compact panel with live download data.

## What Changes

- A new **notch overlay window** (`NotchView` + `NotchViewModel`): borderless, always-on-top,
  transparent, click-through-except-content, docked top-center of the primary screen.
  - **Collapsed state:** a slim dark pill. macOS: sized/positioned to sit flush with the notch
    (menu-bar height). Windows/Linux: shows the **system clock** (per the author's spec) plus a tiny
    aggregate download indicator (e.g. ↓ speed) when anything is active.
  - **Expanded state (on hover):** animates open into a compact island showing active downloads —
    name, progress bar, speed — plus totals; mouse-leave collapses it. Clicking a row (or the island)
    surfaces the main window.
- A **Settings toggle** (`DownloadSettings.EnableNotch`, default **off** — it claims screen real
  estate; opt-in) with a hint; the overlay starts/stops live when toggled, and is created at startup
  when enabled (works alongside close-to-tray: the notch stays while the main window is hidden).
- Reuses the existing manager events (`StatsChanged` + row VMs) — no new download plumbing; the
  overlay is a pure view over `IDownloadManager`.

## Capabilities

### New Capabilities
- `notch-overlay`: an optional always-on-top top-center overlay showing the clock (Win/Linux) /
  hugging the notch (macOS), expanding on hover to live download activity.

## Impact

- New: `Views/NotchView.axaml(.cs)`, `ViewModels/NotchViewModel.cs`, `Services/NotchService.cs`
  (create/position/show-hide, screen-geometry math, hover expand/collapse).
- `Models/DownloadSettings.cs` (+ Settings UI row + i18n in ALL 16 packs).
- `MainViewModel.SetupAppShell` — start when enabled; live toggle from Settings.
- Platform notes: Avalonia cannot draw *inside* the macOS menu-bar/notch region from a normal
  window — the pill sits just below/around it (same approach every non-private-API notch app uses);
  Linux behavior varies by WM/compositor (needs on-device verification, like the tray).

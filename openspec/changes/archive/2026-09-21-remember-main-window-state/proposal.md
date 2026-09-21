## Why

The main window forgets how the user left it (issue #15): maximize it, quit — through the window
button, the tray menu or an OS restart — and the next launch comes back at the built-in 1000×620,
centred. Modal dialogs already remember their size (`Config.WindowSizes`), so the one window the
user actually lives in is the only one that does not. For an app that sits open all day beside a
browser, re-arranging it on every launch is a daily papercut.

## What Changes

- Persist the main window's **layout**: maximized-or-normal, plus the normal-state size **and
  position**, into the existing `config.json`.
- Record it **as it changes** (resize / move / maximize-restore, debounced) rather than only at
  exit — so a shutdown the app never sees (OS restart, power loss, kill) still restores the last
  layout the user chose.
- Restore it at startup: maximized reopens maximized; a normal window reopens at its previous size
  and position.
- Restoration is **safe by construction**: the layout is clamped to a currently-connected screen's
  working area and to the window's `MinWidth`/`MinHeight`, so an unplugged second monitor or a
  changed resolution can never leave the window off-screen or unusably small.
- **Minimized is never restored** — a minimized state is saved as normal, so launching the app
  always produces a visible window (except the existing `--minimized` tray start, which is
  untouched).
- Out of scope: dialog **position** (dialogs keep remembering size only), per-page layout, the
  notch overlay.

## Capabilities

### New Capabilities
- `window-state-memory`: the main window remembers and restores its maximized state, size and
  position across restarts, clamped to the screens actually available at launch.

### Modified Capabilities
<!-- None: window-chrome's requirements (manual resize, modal chrome) are unchanged. -->

## Impact

- `src/Downloader.Desktop/Models/Config.cs` — a new persisted `MainWindow` layout record
  (`WindowLayout`: `IsMaximized`, `Width`, `Height`, `X`, `Y`), defaulted in `New()`/`EnsureValid()`.
- New `src/Downloader.Desktop/Services/WindowLayout*.cs` — a **pure** capture/clamp helper (screens
  injected as plain rectangles) so every branch is testable headlessly on any OS.
- `src/Downloader.Desktop/ViewModels/MainViewModel.cs` — apply the layout in `SetupAppShell()`;
  subscribe to the window's resize/move/state changes and reuse the existing debounced `SaveSoon()`.
- `src/Downloader.Desktop/Views/MainWindow.axaml` — the hard-coded `Width`/`Height` and
  `WindowStartupLocation` become the *defaults* used when nothing is remembered.
- Tests: new `Unit/WindowLayoutTests` (clamping, multi-monitor, minimized) and a headless
  `UI/WindowStateMemoryTests` (round-trip through `Config`).
- No new dependency, no schema bump (an absent record simply means "no memory").

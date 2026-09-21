## Context

`MainWindow.axaml` hard-codes `Width="1000" Height="620"` +
`WindowStartupLocation="CenterScreen"`, and nothing reads or writes the window's state. Modal
dialogs already have half of this: `Config.WindowSizes` (a `Dictionary<string, WindowSize>`) with
`DialogHelper.ApplyPersistedSize`/`SavePersistedSize`, restored before `ShowDialog` and saved on
`Closing`. That mechanism is deliberately size-only and keyed per dialog type; the main window
needs maximized-state and position too, and it must survive an exit the app never observes.

Constraints that shape the design:

- **The window can close without quitting.** Close-to-tray cancels `Closing` and hides the window
  (`MainViewModel.SetupAppShell`), and the tray "Quit" path, an update-on-exit and an OS restart all
  reach shutdown differently. Saving only on `Closing` would miss cases the issue explicitly names.
- **The app already has a debounced writer**: `MainViewModel.SaveSoon()` (plus a 20 s autosave and a
  save on shutdown). Layout changes must ride that, never write the file themselves.
- **Screens change between runs.** A laptop undocked from a second monitor, or a resolution change,
  must not produce an invisible window — the single worst failure mode for this feature.
- **Every platform must be covered from this box** (standing rule). Avalonia's `Screens`/`Window`
  cannot be driven headlessly in a meaningful way, so the decision logic has to be pure.

## Goals / Non-Goals

**Goals:**

- Reopen the main window the way the user left it: maximized, or at its previous normal size and
  position.
- Survive every exit route named in issue #15 — window close, tray Quit, OS restart/kill.
- Never restore a window that is off-screen, larger than the available screen, or below
  `MinWidth`/`MinHeight`.
- Keep the restore decision pure and unit-tested for all three platforms' geometry.

**Non-Goals:**

- Remembering dialog **position** (Add / Details / About keep size-only memory).
- A per-screen or per-monitor-arrangement memory (one remembered layout, clamped at launch).
- Changing close-to-tray, `--minimized` startup, or any other window behaviour.
- A user-visible setting to turn this off — it is the behaviour users already expect.

## Decisions

### 1. A dedicated `MainWindow` record on `Config`, not another `WindowSizes` entry

`WindowSizes` stores `{Width, Height}` and is applied through `DialogHelper`, which is modal-dialog
machinery (it also calls `BeginModal`, `ShowDialog`, etc.). Widening `WindowSize` with
`IsMaximized/X/Y` would give every dialog fields none of them use and change a shape three tests
already assert on.

Instead: `Config.MainWindow` of a new type `WindowLayout { bool IsMaximized; double Width, Height;
double X, Y; }`. An absent/`null` record means "no memory" and the XAML defaults apply — so old
config files need no migration and `SchemaVersion` stays at 1.

*Alternative considered:* a separate `window-layout.json`. Rejected — one config file is already the
app's contract, and it is atomically written with a semaphore (`FileService`).

### 2. The geometry decision is a pure function over plain rectangles

`Services/WindowLayoutPolicy` exposes two pure statics:

- `Capture(isMaximized, restoreBounds) → WindowLayout`
- `Resolve(WindowLayout saved, IReadOnlyList<PixelRect> workingAreas, Size min, Size fallback) →
  WindowLayout` — returns exactly what to apply.

Avalonia types used here (`PixelRect`, `Size`) are plain structs with no platform behaviour, so the
whole ruleset runs in a plain `[Fact]` on any OS. `MainViewModel` does only the dumb part: read
`window.Screens.All`'s `WorkingArea`, call `Resolve`, assign `Width/Height/Position/WindowState`.

*Alternative considered:* clamping inline in the VM, mirroring `DialogHelper.ApplyPersistedSize`.
Rejected — that code path is exercisable only with a real window, which is precisely how a
multi-monitor branch ships untested.

### 3. Clamp rules (what `Resolve` guarantees)

1. Size is clamped to `[min, largest working area]` per axis.
2. The window's **title-bar strip** must intersect some screen's working area by at least
   `MinVisible` (96 × 24 px). If no screen qualifies, the window is re-centred on the primary
   (first) working area at the clamped size. This is the "second monitor unplugged" case.
3. A saved `IsMaximized` wins: the window is shown `Maximized`, and the saved normal geometry is
   kept untouched so restoring from maximized lands where it used to.
4. A nonsense record (non-finite, zero or negative size) is treated as no memory.

Rule 2 uses the title strip rather than the whole rectangle because a window whose top-left is
off-screen but whose title bar is reachable is still usable and is a layout users choose
deliberately; a window whose title bar is nowhere is not draggable back.

### 4. Capture on change, debounced through `SaveSoon()`

Subscribe once in `SetupAppShell()` to the window's `PropertyChanged` for `ClientSizeProperty`,
`WindowStateProperty` and to `PositionChanged`. Each event re-captures and calls the existing
`SaveSoon()`. No new timer, no new file write path.

Two rules make the captured value correct:

- **Only capture normal-state geometry.** While maximized (or minimized) the window's size/position
  describe the maximized frame, which must not overwrite the remembered restore bounds — that is
  what makes "restore down" still land correctly after a restart.
- **Minimized is recorded as not-maximized, geometry unchanged.** The app never restores minimized;
  the existing `--minimized` tray start remains the only way to launch hidden.

*Alternative considered:* saving only on `Closing`/shutdown. Rejected outright by the issue (OS
restart) and by close-to-tray.

### 5. XAML keeps its defaults

`Width/Height/MinWidth/MinHeight/WindowStartupLocation` stay exactly as they are: they define the
first-run layout and the `fallback` passed to `Resolve`. A remembered layout is applied before the
window is shown, so there is no visible jump.

## Risks / Trade-offs

- **A remembered position lands on a screen the OS then rearranges (dock/undock while closed)** →
  `Resolve`'s visibility rule re-centres on the primary working area; the test matrix includes
  "saved on a second monitor that is now gone".
- **Linux WMs ignore or override `Window.Position`** (a known Avalonia/X11-and-Wayland reality; the
  app already works around WM differences in `ResizeGrips`/`WindowResize`) → size and maximized
  state still restore, position degrades to the WM's choice. Acceptable: the issue's headline case
  is maximized, and nothing breaks.
- **Per-change capture fires often while dragging a resize** → capture writes two doubles into an
  in-memory record and calls an already-debounced save; no I/O per event. `SaveSoon()`'s existing
  debounce is the throttle.
- **`PositionChanged` can fire during startup before the layout is applied** → the subscription is
  installed *after* the restore, and a guard flag ignores events while applying.
- **Maximized-on-startup hides a bad restore size** → the normal geometry is still clamped on load,
  so restoring down after launch cannot produce an off-screen window either.

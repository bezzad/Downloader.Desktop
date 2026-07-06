## Why

The author found five separate reliability/UX issues while using the app: stopped downloads spontaneously
restarting after a relaunch, notifications always appearing as in-app toasts instead of routing to the OS
when the app is in the background, the notch overlay's expanded panel being noticeably taller than its
content needs, a global speed-limit change in Settings not propagating live to downloads that are already
running or stopped, and (reported mid-investigation, with a screen recording) dragging the main window's
LEFT edge to resize eventually makes the window disappear. Each is independently reproducible and fixable;
bundling them into one batch avoids five tiny round-trips for closely related "does the app behave the way
I expect" reports. (A sixth item the author raised — confirming the HLS plugin ships correctly on the next
release, and fixing its YouTube resolution failures — is split out into its own dedicated change,
`fix-hls-youtube-resolver`, since it turned out to be substantial diagnostic/fix work in its own right.)

## What Changes

- **Durable download state across restart**: a download the user explicitly stopped (including via Stop
  All) SHALL stay Stopped after the app is closed and reopened, and SHALL NOT be silently re-queued or
  auto-started. Investigation traced a concrete mechanism: `DownloadManager`'s scheduler re-evaluates every
  enabled `DownloadSchedule` on the FIRST tick after launch using only the current time-of-day window, with
  no memory of whether that schedule already fired earlier that day before the restart — so relaunching the
  app while inside an enabled schedule's window re-fires its start trigger, flips the queue's Stopped rows
  back to Created via `StartQueue`, and downloads resume a few seconds later. Add a regression test that
  reproduces "Stop All → restart → still Stopped" (with and without an enabled schedule) and harden the
  scheduler so a restart cannot re-trigger a start that isn't a genuine new time-window crossing.
- **Focus-aware notification routing on Windows**: `Services/NotificationService.TryNative` currently has NO
  Windows implementation — it unconditionally returns `false` on Windows, so every notification (even while
  unfocused/backgrounded) falls back to the in-app toast, defeating the focus-aware routing the
  `notifications` capability already specifies. Add a real Windows OS-notification channel (no new NuGet
  dependency — spawn a system utility the way the existing Linux/macOS branches do) so Windows gets true OS
  notifications when the app is unfocused or hidden to the tray, matching Linux/macOS behavior.
- **Notch overlay expanded size**: the expanded panel's fixed `400×210` is noticeably taller than its
  content (header + top-3 running rows + padding) needs — the author estimates ~1.5× too tall. Recompute
  the expanded height from the actual content: header row height + top/bottom padding + 3 rows (each row's
  name/status line + progress-bar height + inter-row spacing) + a small bottom margin, so the panel fits its
  content snugly instead of leaving empty space below the third row.
- **Global speed-limit changes propagate live**: today `DownloadManager.Start(vm)` builds a brand-new
  `DownloadConfiguration` from Settings every time a download starts, so a per-item `Configuration` handle
  (used by the details dialog's live speed-limit control) is completely disconnected from Settings once
  created — changing **Settings → Speed limit** afterward has no effect on any download already running or
  stopped, and any per-item override set via the details dialog is silently lost on the next Start/Resume.
  Add a per-item "custom speed limit" flag + persisted value on `DownloadItem`, and: (a) when the global
  Settings speed limit changes, apply it live to every Running download's `Configuration` and to every
  Stopped download's remembered value, **except** items flagged with a custom per-item limit; (b) make the
  details dialog's speed-limit control set that flag + persist the value when the user sets a per-item
  limit, and (c) have `Start`/`Resume` honor a persisted per-item override instead of always taking the
  current global Settings value.
- **Main window disappears after resizing from the left edge**: confirmed via a screen recording — dragging
  the window's LEFT (west) edge repeatedly eventually makes the whole window vanish (the process keeps
  running; the tray icon and its menu still respond, so this is a window-state bug, not a real crash).
  `Views/ResizeGrips.axaml.cs` implements manual resize (native `Window.BeginResizeDrag` is a no-op on
  macOS for borderless windows): the RIGHT/BOTTOM edges only change `Width`/`Height` (no position math,
  which is why they "work fine"), but the LEFT/TOP edges must ALSO shift `Window.Position` to keep the
  opposite edge fixed, computed as a delta from `Window.Bounds.Width/Height` re-read at the top of every
  `PointerMoved` callback. `MinWidth`/`MinHeight` are already set (840×500), which rules out a plain
  size-collapse — the vanishing is much more likely the window's `Position` drifting off every screen,
  because each frame's delta is computed against `_window.Bounds`, a value Avalonia is not guaranteed to
  have synchronously updated to the previous frame's own `_window.Width =`/`_window.Height =` assignment —
  under a fast drag this can compound. Rebase the west/north math on a fixed drag-start snapshot (pointer
  position + window position/size captured once in `OnPressed`) and compute every subsequent frame from
  that fixed baseline plus the pointer's screen-space delta, instead of re-reading the window's own
  possibly-stale `Bounds`/`Position` each frame.

## Capabilities

### New Capabilities
- `speed-limit`: global vs. per-item download speed limiting — a per-item custom limit persists and is
  respected; a global Settings change propagates live to every download that has no custom limit.
- `window-chrome`: custom-chrome window behavior (the borderless windows' manual resize/drag) — resizing
  from any edge or corner SHALL keep the window on-screen and visible.

### Modified Capabilities
- `download-status`: adds a durability requirement — a Stopped download (including via Stop All) SHALL
  remain Stopped across an app restart and SHALL NOT be auto-started by the scheduler re-evaluating an
  already-fired-today schedule window.
- `notifications`: the existing "route to OS when unfocused" requirement SHALL hold on Windows too (today
  it silently falls back to in-app there, which is a conformance gap against the current spec, not new
  behavior).
- `notch-overlay`: adds a compactness requirement — the expanded panel's size SHALL be sized to its content
  (header + up to 3 rows + padding), not a fixed oversized rectangle.
## Impact

- **Code**: `Services/DownloadManager.cs` (scheduler fire-tracking, speed-limit propagation, Start/Resume
  honoring a per-item override), `Models/DownloadItem.cs` (new persisted fields), `ViewModels/SettingViewModel.cs`
  (propagate on speed-limit change), `ViewModels/DownloadDetailsViewModel.cs` (set the custom-limit flag),
  `Services/NotificationService.cs` (Windows native channel), `Views/NotchView.axaml.cs` (expanded size
  constants), `Views/ResizeGrips.axaml.cs` (drag-start-anchored resize math).
- **Docs**: possibly `.claude/skills/downloader-desktop/SKILL.md` (new gotchas — scheduler restart-fire
  tracking, per-item speed-limit persistence, Windows notification mechanism, resize-grip anchoring).
- **Tests**: new regression tests for restart durability (with/without a schedule), speed-limit propagation
  (custom vs. default items), and the notch overlay's computed size; existing screenshot/notch-mockup
  captures may need regeneration (Linux-only per standing convention).
- **No breaking changes**: all fixes are corrective (align behavior with what the app already claims to do)
  or additive (per-item speed override).

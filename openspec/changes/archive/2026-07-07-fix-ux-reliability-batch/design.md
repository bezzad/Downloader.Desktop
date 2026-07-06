## Context

Five independent issues, each investigated in the current codebase before writing this design so the fix
approach below is grounded in actual code paths, not speculation. (A sixth item — the HLS plugin's release
readiness and its YouTube resolution failures — was split out into a separate change,
`fix-hls-youtube-resolver`, once investigation showed it needed its own diagnostic pass rather than a quick
verification.)

1. **Stopped downloads auto-restart after a relaunch.** `DownloadManager.Initialize` already normalizes a
   saved `Running`/`Paused` status to `Stopped` (a live connection can't survive a process exit), and
   `Cancel` (used by Stop/Stop All) already sets `Stopped` for running, paused, AND queued (Created/None)
   items, persisted immediately via `NotifyList → ListChanged → RequestSave`. Nothing in `Initialize` or the
   startup path calls `PumpQueue`/`StartAll`/`Resume` automatically. The one mechanism that CAN flip a
   Stopped row back to `Created` and start it with no explicit user action is the scheduler:
   `EvaluateSchedules` runs on a 30s `DispatcherTimer` and, on the very first tick after launch
   (`_firedDay = DateTime.MinValue` initially), fires `TriggerStart` for any ENABLED schedule whose
   time-of-day window (`tod >= StartTime && (StopTime == null || tod < StopTime)`) currently contains "now"
   — with zero memory of whether that schedule already fired earlier the same day, because `_firedKeys` is
   an in-memory `HashSet` that resets to empty every process start. `TriggerStart` → `StartQueue` explicitly
   flips every Stopped/Failed row in the target queue to `Created` and pumps it. This exactly reproduces
   "stop, restart, and a few seconds later it's downloading again": relaunching the app while inside an
   enabled schedule's window re-fires a start that already happened before the restart, undoing an explicit
   Stop that came after.
2. **Notifications always in-app.** `Services/NotificationService.Notify` already routes on
   `InAppVisible` (focused + a window on screen) vs. `TryNative` (OS channel), matching the `notifications`
   spec. But `TryNative`'s Windows branch is simply absent — the code falls through to `return false` with
   the comment "Windows + fallbacks use the in-app toast". So on Windows, EVERY notification while
   unfocused/tray-hidden still shows as an in-app toast (invisible if no window is on screen, or seen when
   the user next opens the window) — a straightforward conformance gap against the existing spec, confirmed
   by reading the code rather than by inference.
3. **Notch overlay expanded size.** `NotchView.axaml.cs` hardcodes `ExpandedWidth/Height = 400, 210`, but the
   content (`NotchViewModel.MaxRows = 3`, already correctly capping the row list) is: a ~24px header row, a
   `StackPanel` with `Margin="18 10 18 14"` (24px vertical padding), 10px spacing before the row list, and
   each row ≈ name/status line + 5px spacing + a 4px-tall progress bar + 7px top margin (~28-32px/row) — three
   rows ≈ 90-100px. Summed, content needs roughly 150-165px, well under the current 210px fixed value —
   consistent with the author's "about 1.5× too tall" estimate.
4. **Global speed-limit doesn't propagate.** `DownloadManager.Start(vm)` calls
   `_config.Settings.ToConfiguration()` fresh every time and stores the result as `vm.Configuration` — a
   plain, un-synced `DownloadConfiguration` object. The details dialog's `SpeedLimitKb` property (already
   live: mutating `Item.Configuration.MaximumBytesPerSecond` takes effect on the running download without a
   restart, since the engine reads the field continuously) is the ONLY way to touch a per-item limit today,
   and it is never persisted to the `DownloadItem` model — so it's silently lost on the next Start/Resume,
   AND a later change to Settings' global limit has no way to reach an already-created `vm.Configuration`.
   There is currently no concept of "this item has its own limit" at all; it needs to be introduced.
5. **Main window vanishes after a left-edge resize** (confirmed via a screen recording the author provided —
   extracted with `ffmpeg` and inspected frame-by-frame). `Views/ResizeGrips.axaml.cs` implements manual
   resize because `Window.BeginResizeDrag` is a no-op on macOS for borderless windows. The EAST/SOUTH edges
   only ever set `Width`/`Height` — no `Position` math, which is why the author found dragging the right
   edge trouble-free. The WEST/NORTH edges must ALSO shift `Position` to keep the opposite edge visually
   fixed, and compute that shift from `_window.Bounds.Width/Height` (read fresh at the top of every
   `PointerMoved`) versus the newly-clamped width/height. `MainWindow` already sets `MinWidth="840"
   MinHeight="500"`, ruling out a plain "shrunk to zero" collapse. The recording's last frames show the
   window gone entirely while the macOS menu-bar tray icon's menu still opens and responds — i.e. the
   process is alive and `TrayService` still works; only the window's on-screen position/size became
   invalid. The most likely mechanism: each `PointerMoved` computes its position delta against
   `_window.Bounds`/`_window.Position`, which Avalonia is not guaranteed to have synchronously updated to
   reflect the PREVIOUS callback's own `_window.Width =`/`_window.Height =`/`_window.Position =`
   assignments before the next input event is dispatched. Under a fast drag (many pointer-move events in
   quick succession, as in the recording) this creates a per-frame reference point that can already be
   stale, and errors compound across frames — driving `Position` off every screen instead of just
   width/height (which stay bounded by `MinWidth`/`MinHeight`/`MaxWidth`/`MaxHeight` clamps).

## Goals / Non-Goals

**Goals:**
- Stopped/paused download state is durable across a restart under every code path that can currently touch
  it, not just the specific "Stop All" flow — verified with a regression test that fails on today's code.
- Notifications reach the correct channel (in-app vs. OS) identically on Windows, Linux, and macOS.
- The notch overlay's expanded panel is sized to its actual content, not a fixed oversized box.
- A global speed-limit change reaches every download that hasn't been given its own limit, live, without
  restarting the download; a per-item limit — once set — survives Stop/Resume and app restarts and is never
  silently overwritten by a later global change.
- Resizing the main window (and any other custom-chrome window using `ResizeGrips`) from any edge/corner,
  at any drag speed, never leaves the window off-screen or invisible.

**Non-Goals:**
- Redesigning the Scheduler feature's semantics beyond making a restart mid-window not re-fire a start that
  already happened. "Catch-up" behavior (starting a schedule's target once, the first time the app opens
  after a window began, if it never fired that day) is preserved intentionally — only the specific
  same-day-already-fired-then-restarted case changes.
- A UI to configure "OS notification style" per platform, or notification action buttons on Windows beyond
  what the existing in-app actionable-toast fallback already provides.
- Any change to `NotchViewModel`'s content logic (row selection/overflow) — only the window's outer size.
- A general per-item settings override system beyond speed limit (e.g. per-item connection count) — scoped
  strictly to the reported issue.
- Rewriting `ResizeGrips` to use OS-native resize (still a no-op on macOS for borderless windows per the
  existing gotcha) — the fix stays within the manual pointer-capture approach, just anchored differently.

## Decisions

### D1: Scheduler start-fire tracking persists per schedule, not just per process
Add `LastFiredStartDate`/`LastFiredStopDate` (a `DateOnly?` or `DateTime?` truncated to date) to
`DownloadSchedule`, persisted with the rest of `Config`. `EvaluateSchedules` checks this persisted date
instead of (or in addition to) the in-memory `_firedKeys`/`_firedDay`: a schedule whose `LastFiredStartDate`
already equals today does NOT re-fire `TriggerStart` even on a fresh process's first tick. The in-memory
`_firedKeys` set can be dropped once the persisted date does the same job, simplifying rather than adding a
second parallel tracking mechanism.
- **Alternative considered**: make `Cancel`/`StopAll` set a per-item or per-queue "explicitly stopped,
  don't auto-resume" flag that the scheduler must respect. Rejected — it doesn't generalize (a user might
  explicitly WANT the schedule to restart things later that same window), and it adds a second flag
  interacting with an already load-bearing `DownloadStatus` enum. Tracking "did this schedule already do
  its job today" is the more precise, narrower fix and matches what a schedule is actually supposed to
  guarantee (fire once per day per window), independent of what the user does afterward.

### D2: Regression test reproduces the exact user-reported sequence before the fix lands
Write a `DownloadManager`-level test: seed a Stopped item in a queue, seed an ENABLED schedule whose window
contains "now" with `LastFiredStartDate` unset (simulating "it fired earlier today, in a session that's now
gone" is NOT the scenario to reproduce first — reproduce the WORSE case: a schedule that has never recorded
firing today, mimicking a pre-migration saved config or a same-day-first-launch) → call the same
`EvaluateSchedules`/`OnSchedulerTick` path a fresh `Initialize` would hit → assert the item is still Stopped
when no schedule should fire, and assert it OK to fire once when a schedule genuinely should. Also add a
plain "no schedules configured at all → Stopped stays Stopped after Initialize" test as the simplest
possible regression guard, independent of the scheduler theory being the sole cause.

### D3: Windows notification channel via a spawned OS utility, matching the existing pattern
Add a real Windows branch to `TryNative`, implemented the same way as the Linux (`notify-send`)/macOS
(in-process `MacNotifier`) branches — no new NuGet dependency. Windows 10+ ships PowerShell with access to
the WinRT toast APIs; spawn `powershell.exe -NoProfile -WindowStyle Hidden -Command "..."` constructing and
showing a `Windows.UI.Notifications.ToastNotification`, matching the `Process.Start`-based shape already
used for Linux. Keep the same try/catch-and-return-`bool` contract so `PreferOsChannel`/`InAppVisible`
logic is untouched.
- **Alternative considered**: `System.Windows.Forms.NotifyIcon.ShowBalloonTip` (older balloon-tip API).
  Simpler, but would require adding a Windows Forms reference (`UseWindowsForms=true`) to a project that is
  currently a single cross-platform `net10.0` TFM (no `net10.0-windows` split) — a bigger structural change
  for a visually dated notification style. Rejected in favor of the process-spawn approach, which needs no
  project/TFM changes and produces a modern native toast.
- **Risk accepted**: this cannot be exercised on the current (macOS) dev box or by CI (no Windows runner
  configured); it can only be verified on an actual Windows machine, same caveat as the existing
  Linux/macOS branches when they were added.

### D4: Notch expanded size computed from content constants, not eyeballed
Replace the hardcoded `ExpandedHeight = 210` with an explicit sum of named constants matching the actual
XAML metrics (header height, top/bottom padding, per-row height including its progress bar, inter-row
spacing, row count `NotchViewModel.MaxRows`), so the relationship between "3 rows" and the window height is
self-documenting and won't drift out of sync if a row's content changes again later. Verify visually via the
existing gated `CaptureNotchMockups` test (renders collapsed/expanded PNGs) — Linux-only per standing
screenshot convention.

### D5: Per-item speed limit as an explicit flag + value, single propagation path
Add `HasCustomSpeedLimit` (bool) and `CustomSpeedLimitBytesPerSecond` (long) to `DownloadItem` (persisted).
`DownloadDetailsViewModel.SpeedLimitKb`'s setter sets both the live `Item.Configuration.MaximumBytesPerSecond`
(existing behavior, immediate effect) AND `Item.HasCustomSpeedLimit = true` +
`Item.CustomSpeedLimitBytesPerSecond` (persisted, so it survives Stop/Resume/restart). Add a small
"Use global limit" toggle next to the existing NumericUpDown so the user can explicitly clear the override
(`HasCustomSpeedLimit = false`) and immediately fall back to the current global value.
`DownloadManager.Start(vm)` builds the configuration from Settings as today, then — if
`vm.GetItem().HasCustomSpeedLimit` — overwrites just `configuration.MaximumBytesPerSecond` with the item's
persisted value before starting the engine. A new `DownloadManager.ApplyGlobalSpeedLimit(long bytesPerSecond)`
iterates `Items` and, for every item WITHOUT `HasCustomSpeedLimit`, sets `vm.Configuration.MaximumBytesPerSecond`
live (a no-op for a Stopped item with no active `Configuration`, since its next `Start` already pulls the
current Settings value naturally). `SettingViewModel`'s speed-limit setter calls this after writing through
to `Settings.MaximumBytesPerSecond`, mirroring the existing `MaxConcurrentDownloads` → `DefaultQueue`
sync-on-change pattern already in the codebase.
- **Alternative considered**: store only a nullable override (`long? CustomSpeedLimitBytesPerSecond`, null =
  "follow global") instead of a separate bool. Rejected because `0` is already a meaningful value ("no
  limit") for BOTH the global setting and a per-item override — a nullable long conflates "no override" and
  "override = unlimited" unless a sentinel is invented; an explicit bool is clearer and matches the existing
  `DisabledPlugins`-style boolean-flag conventions elsewhere in this codebase.

### D6: Resize math anchored to a drag-start snapshot, not the window's own live state
In `ResizeGrips`, `OnPressed` captures once: the pointer's SCREEN position, and the window's `Position`
and `Bounds.Width/Height` at that instant. `OnMoved` computes `delta = currentPointerScreenPos -
startPointerScreenPos` (screen pixels) and derives every subsequent frame's width/height/position purely
from `(startPosition, startSize, delta)` — never re-reading `_window.Bounds`/`_window.Position` mid-drag.
This removes the one-frame-lag hazard entirely: each frame is computed fresh from an immutable baseline
instead of compounding on top of whatever the window's live state happens to currently be. East/South
(width/height only, no position) keep behaving identically; West/North gain the same non-compounding
property. Clamp to `MinWidth/MinHeight/MaxWidth/MaxHeight` as today, and additionally clamp the final
`Position` so the window's title-bar-equivalent (top edge) can never move further off any screen's working
area than some small margin — a last-resort guard even if the anchoring fix above isn't the complete story.
- **Alternative considered**: keep the current per-frame-relative-to-window math but throttle/coalesce
  pointer-move events. Rejected — treating the symptom (event rate) rather than the cause (a
  self-referential calculation that can compound error), and it would make the drag feel laggy.

## Risks / Trade-offs

- **[Risk] The scheduler theory (D1/D2) may not be the only path that can un-stop a download.** →
  Mitigation: D2's plain "no schedule at all" regression test is written FIRST and must pass against
  TODAY's code before concluding the scheduler is confirmed as (at least) A cause; if it already fails with
  no schedule configured, broaden investigation (candidates already ruled out during design: `Initialize`
  itself, `PumpQueue`'s callers, `Add`'s background name-resolution, the completion-event handler's
  Stopped/Paused guard) before declaring the fix complete.
- **[Risk] Windows notification code (D3) is unverifiable on this dev box/CI.** → Mitigation: keep the same
  try/catch/return-bool contract as the working Linux/macOS branches so a failure degrades to the existing
  in-app fallback instead of throwing; note in the PR/task that real verification needs an actual Windows
  machine.
- **[Risk] Persisting a per-item speed-limit override (D5) adds a new `DownloadItem` field that must
  round-trip through existing JSON configs.** → Mitigation: default `HasCustomSpeedLimit = false` so
  existing saved configs deserialize unaffected (missing JSON property → default `false`/`0`), matching the
  existing pattern for other additive `DownloadItem` fields (e.g. `ResolverPluginId`).
  fields.
- **[Risk] The resize fix (D6) touches input-handling code that's inherently hard to unit-test (real pointer
  drag, real window state).** → Mitigation: the math itself (given a start snapshot + a delta, compute the
  resulting width/height/position) is pure and CAN be unit-tested by extracting it into a small static
  method; the actual pointer-capture wiring still needs the author's manual verification (drag every edge,
  fast and slow) since headless Avalonia can't simulate a real multi-frame OS-level drag realistically.

## Migration Plan

No data migration beyond new `DownloadItem`/`DownloadSchedule` fields defaulting safely for old configs (see
Risks). No breaking API changes. Each of the six fixes is independently shippable/revertable; land and
verify them one at a time in the task order below rather than as one giant commit, so a regression in one
area is easy to isolate.

## Open Questions

- Should the scheduler's fixed 30-second first-tick evaluation itself be delayed slightly (e.g. skip
  evaluating on the very first tick after launch, only evaluate from the second tick on) as an additional
  safety margin, or is the persisted last-fired-date (D1) sufficient on its own? Current design treats D1 as
  sufficient; revisit only if the regression test from D2 still shows a gap.
- Exact pixel constants for the notch's expanded height (D4) — the design gives the accounting method, not
  a final number; the implementer should measure against the actual rendered content (via the existing
  `CaptureNotchMockups` gated test) rather than trust the rough arithmetic in Context item 4 verbatim.

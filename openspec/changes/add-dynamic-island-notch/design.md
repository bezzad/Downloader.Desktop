## Context

References: the boring.notch Swift app (macOS notch → dynamic-island hub) and the author's video link.
We build the equivalent as an Avalonia overlay window so one implementation serves all three OSes,
with per-OS positioning. Existing building blocks: `TrayService` (window-less UI patterns +
platform guards), the `StatsChanged`/row-VM event surface, `ThemeService` styling, rounded
transparent windows (`TransparencyLevelHint`, corner-clipped root border).

## Goals / Non-Goals

**Goals:**
- One `NotchService` managing a single overlay window: collapsed pill ⇄ expanded island on hover.
- Collapsed content: clock (Win/Linux) or minimal notch-hugging pill (macOS); tiny live ↓speed hint.
- Expanded content: active download rows (name, %, speed) + aggregate; click surfaces the app.
- Opt-in setting, live toggle, survives close-to-tray, multi-screen: primary display only (v1).

**Non-Goals (v1):**
- No true drawing inside the macOS notch/menu-bar (needs private APIs); we hug it.
- No music/media controls or third-party widgets (boring.notch scope creep) — downloads + clock only.
- No per-download actions in the island beyond click-to-open (pause/cancel stay in the main window).

## Decisions

- **One borderless `Window`** (`SystemDecorations=None`, `Topmost=true`, `ShowInTaskbar=false`,
  transparent background, rounded bottom corners like a notch): simplest cross-platform primitive
  Avalonia has; no per-OS native layer in v1.
- **Positioning:** top-center of the primary screen: `x = (workArea.Width - width)/2`, `y = 0`
  (macOS: `y = 0` puts it at the menu bar's bottom edge since the workArea excludes the menu bar —
  visually "under the notch"; acceptable v1 per the non-goal above).
- **Expand/collapse:** `PointerEntered`/`PointerExited` on the window swap collapsed/expanded
  content presenters and animate `Width`/`Height` (Avalonia `Transitions`); a small collapse delay
  (~300 ms) avoids flicker when the mouse skims past.
- **Clock:** a 1 s `DispatcherTimer` in `NotchViewModel` (only while visible) → `TimeText`.
- **Download data:** subscribe `manager.StatsChanged` + reuse `DownloadItemViewModel` rows filtered
  to `Status == Running` (top 3 + "and N more…"); zero new download plumbing.
- **Focus behavior:** the overlay must never steal focus — `Focusable=false`, activation suppressed;
  clicking a row calls the existing bring-to-front path (`TrayService.ShowWindow`-style marshaling).

## Risks / Trade-offs

- [Linux WM variance: always-on-top/transparency differ across compositors] → same stance as the
  tray: best-effort, verify on-device, fail soft (setting just doesn't show the overlay).
- [Overlay overlapping full-screen apps/games] → v1 keeps `Topmost` semantics simple; if intrusive,
  a later iteration can hide on full-screen detection.
- [macOS spaces/fullscreen: a normal window doesn't join all spaces] → v1 limitation; note in docs.

## Open Questions

- Author's default-width/height + exact visual (pill radius, colors) — mockup-first per repo rules
  before detailed XAML work.

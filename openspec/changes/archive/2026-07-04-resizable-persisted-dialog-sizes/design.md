## Context

Three modal window types exist: `AddDownloadItemView` (560×460, `CanResize="False"`), `PageDialogView` (900×640, `CanResize="True"`, hosts Queues/Scheduler/Settings pages), `DownloadDetailsView` (640×700, `CanResize="True"`). They're shown via three different `DialogHelper` entry points (`ShowDialog<TV,TVm,TResult>`, `ShowDetails`, `ShowPage`) that don't share a common "before show / after close" hook today. `Config` has no window-size fields at all. `FileService` already debounce-saves `Config` on any tracked change (`MainViewModel.SaveSoon()`), so persistence infrastructure exists — only the size fields and the save trigger are missing.

## Goals / Non-Goals

**Goals:**
- All three modal window types become (or stay) user-resizable, with sensible min bounds so they can't be shrunk to something unusable.
- Each window type remembers its own last size across the whole app session and across restarts (persisted in `Config`), independent of the other window types.
- Reuse the existing debounced save path — no new save timer/mechanism.

**Non-Goals:**
- No per-instance sizing (e.g. Settings vs Queues vs Scheduler, which all share `PageDialogView`, share ONE remembered size — not one each).
- No remembering window *position*, only size.
- No changes to `MainWindow`'s own sizing (already resizable, already has Min bounds) — this only touches the three modal dialogs.

## Decisions

- **Key sizes by window type name, not by dialog title.** `PageDialogView` hosts three different pages (Queues/Scheduler/Settings) that should share one remembered size (a "management dialog" concept the user resizes once), not three separate memories — the proposal explicitly settled this ("shared" for `PageDialogView`, `DownloadDetailsView` independent). Storage: `Dictionary<string, WindowSize>` on `Config`, keyed by a constant string per window type (`"AddDownload"`, `"PageDialog"`, `"Details"`), where `WindowSize` is a tiny persisted `{ double Width; double Height; }` POCO.
- **Add one shared helper in `DialogHelper`** — `ApplyPersistedSize(Window view, string key, Config config)` (call before `Show`) and `SavePersistedSize(Window view, string key, Config config)` (call in the window's `Closing`/`Closed` event) — rather than duplicating load/save logic in three call sites. `ShowDialog<TV,TVm,TResult>`, `ShowDetails`, and `ShowPage` each call these two around their existing `ShowDialog` call, passing their window's constant key.
- **Clamp on load, not just on save.** When restoring a persisted size, clamp `Width`/`Height` to `[MinWidth, MinHeight]` (from the window's own XAML-set minimums) and to the current screen's working-area size (via `TopLevel.Screens`/`Screen.WorkingArea` on the owner window) — protects against a corrupted config value or a size saved on a bigger monitor producing an off-screen/unusable window. No explicit max is stored; the clamp is applied at load time only.
- **`AddDownloadItemView` flips `CanResize="True"`** and gets `MinWidth`/`MinHeight` added (e.g. 480×360) — its current 560×460 becomes the fallback default when no persisted size exists yet.
- **Persist through the existing debounced save**, not a new immediate write: `SavePersistedSize` just mutates `Config.WindowSizes[key]` and calls the same `SaveSoon()`-style trigger already wired for other config mutations (or, if that trigger isn't reachable from `DialogHelper`, a direct `FileService.SaveAsync` call — whichever keeps this change from threading a new dependency through `DialogHelper`, decided during implementation by checking what's already injectable there).

## Risks / Trade-offs

- [A window resized very small before a min-size guard is added, or a size from a disconnected external monitor, could restore unusable] → Mitigation: clamp to `MinWidth`/`MinHeight` and current screen working area on every restore.
- [`DialogHelper` today is mostly static/stateless (no `Config` reference) — wiring persistence in means it needs access to the running `Config` instance] → Mitigation: pass `Config` explicitly into the new helper methods from each call site (all three already have it or can get it from `MainViewModel`/DI) rather than making `DialogHelper` itself stateful.
- [Saving on every resize-drag tick would thrash the save path] → Mitigation: only write on the window's `Closing`/`Closed` event (final size), not on live `Resized` events.

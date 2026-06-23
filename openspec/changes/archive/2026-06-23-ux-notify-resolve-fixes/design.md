## Context

Six independent UX fixes on the existing Avalonia/.NET 10 app. Grounded in current code:

- **Filters**: `DownloadsViewModel.Matches` (line ~219) maps `StatusFilter.Failed => Failed or Stopped`, and `MainViewModel.FailedFilterCount` (lines ~159–160) counts `Failed or Stopped`. That is why the Failed filter wrongly lists user-stopped items. `Stopped` currently has no other bucket; the `_ => true` (All) arm already covers it.
- **Add dialog**: `UrlResolver.ResolveFileInfoAsync(url)` already wraps the engine's `RemoteFileResolver.GetFileInfoAsync` (Downloader 5.9.0) and returns a `RemoteFileInfo` (resolved name + total size), gated + 8s-timeout, never throws. Nothing wires it into the Add dialog yet.
- **Notifications**: `NotificationService.Notify` tries native OS first (`TryNative`) then falls back to in-app (`ShowInApp`) — it does **not** consider focus. `ShowAction` always uses the in-app actionable toast. `DialogHelper.ActiveWindow` already tracks the active window.
- **Sample plugin**: `samples/Downloader.Desktop.SamplePlugin` source is already on the current SDK (`ILinkResolver`/`DownloadPlan`/`DownloadPart`), but it is **not in `Downloader.Desktop.sln`** and **no built DLL exists**, so `dotnet build` never rebuilds it. Installing it surfaces `Plugins_NoneFound` ("not a Downloader plugin") because the user installs a stale/absent DLL.
- **Expired links**: the sample link returns an anti-bot/expired response (HTML/redirect, HTTP 200 with a non-file body), which currently produces a confusing partial/complete state.

## Goals / Non-Goals

**Goals:**
- One clear notification channel at a time, chosen by app focus; no double-fire.
- Copyable toast text for bug reports.
- Pre-download name+size in the Add dialog for a single link; folder-only for multiple links.
- Correct Failed/Stopped filter semantics.
- Detect expired/invalid links and fail them with a clear message.
- The bundled sample plugin loads.

**Non-Goals:**
- No new notification *content* system or new toast framework — reuse `WindowNotificationManager`.
- No site-extractor / media plugin work (separate effort).
- No change to the engine; only consume existing `RemoteFileResolver`.
- No new "Stopped" filter pill (decided: Stopped lives under All only).

## Decisions

### 1. Filter buckets (Failed = failures only; Stopped → All)
Change the `Matches` `Failed` arm to `vm.Status is DownloadStatus.Failed` and `FailedFilterCount` to count `Failed` only. Stopped items fall through to the `_ => true` (All) arm — no new pill. *Alternative considered:* a dedicated Stopped pill — rejected per the author (keep the footer to its current 5 pills).

### 2. Add-dialog resolution (debounced, non-blocking)
On URL-text change in `AddDownloadItemViewModel`, count links. If exactly one: start a **debounced** (~600 ms) background `UrlResolver.ResolveFileInfoAsync`; on success set the File name box (if the user hasn't typed one) and a `SizeText`; show a transient "Resolving…" state; on failure/timeout fall back to the URL-derived name and clear the indicator. If more than one link: **disable** the File name box and ignore resolution (folder-only). Reverting to one link re-enables it. Cancel any in-flight resolve when the text changes again (CTS per keystroke). *Alternative considered:* resolve on focus-leave or a manual button — rejected (author chose auto-debounced).

### 3. Unknown size → single part
When the started download's size is unknown, force one part. The engine already uses a single connection without a known size + range support; we make it explicit so progress/part display is consistent. Implemented at the build/start point in `DownloadManager` (chunk count = 1 when size is unknown).

### 4. Expired/invalid link heuristic (content + size)
Mark a download **Failed — "Link expired or invalid"** when the response is non-file content or an implausibly small text body. Signals: resolved/served `Content-Type` is `text/html` (or otherwise markup) when a binary file was expected, **or** the finished body is a tiny text payload (small byte count with text content) that cannot be the requested file. Implemented as a pure, unit-testable predicate (mirrors the existing `LooksCorruptedAfterResume`/`LooksAlreadyDownloaded` helpers) consumed in `DownloadManager`'s completion/start path. *Alternative considered:* HTTP-status-only detection — rejected because the sample returns 200 with an HTML body, which status checks miss.

### 5. Sample plugin loads
Build the sample against the current SDK and verify it loads via a host-mirroring `AssemblyLoadContext` test (resolve `Abstractions` from Default, assert `is IDownloaderPlugin`). Add the sample project to `Downloader.Desktop.sln` (or an explicit build step) so a normal build keeps the DLL fresh — this is the actual root cause (stale/absent DLL), not source drift. The test downloads/loads the freshly built DLL the same way the host does.

### 6. Focus-aware notification routing
Track app focus in `NotificationService` (`AppFocused`), updated from window `Activated`/`Deactivated` (any app window active ⇒ focused). Route in `Notify`: focused ⇒ `ShowInApp` only; unfocused/tray ⇒ native OS only. For **actionable** messages (`ShowAction`): if focused, show the in-app actionable toast now; if unfocused, send a plain OS notification **and enqueue** the actionable toast to be re-shown on the next `Activated` (a small pending queue flushed on focus-gain), so the action is never lost. *Alternative considered:* always-in-app actionable — rejected (author chose OS-now + re-show).

### 7. Copyable toasts
Show in-app toasts with custom content (a small view: severity icon + selectable text + a copy button) posted through `WindowNotificationManager`, instead of a bare `Notification`. The copy button writes `"{title}: {message}"` to the clipboard via the top-level clipboard. Keeps a single in-app rendering path used by all messages.

## Risks / Trade-offs

- **Expired-link false positives** (a genuine tiny `.txt`/HTML file flagged) → Mitigation: require both "small" AND "text/markup content" with a conservative size threshold; only apply when a binary file was expected; cover with a "genuine small file is not mis-flagged" test.
- **Focus detection on Linux WMs without proper activation events** → Mitigation: default to in-app when focus state is unknown (focused-by-default is the safer, less-intrusive channel); rely on the already-working `ActiveWindow` tracking.
- **Avalonia custom toast content** (clipboard access, theming) → Mitigation: reuse the existing `ShowInApp` path and `TopLevel` clipboard; keep the toast view minimal.
- **Sample-in-solution build cost** → Mitigation: the sample is tiny; building it in the solution is negligible and prevents future drift.
- **Debounced resolve races** (stale result overwriting a newer link) → Mitigation: per-keystroke `CancellationTokenSource`; only apply a result if its URL still matches the current single link and the user hasn't typed a name.

## Migration Plan

No data migration. Filter semantics change is behavioral only (Stopped moves from Failed→All view). Ship together on `develop`; rollback is a straight revert of the change. Refresh `docs/screenshots/` if Add-dialog/footer visuals change.

## Open Questions

- Exact `RemoteFileInfo` property names for name/size (resolve at implementation time against the 5.9.0 package).
- Whether to also expose the resolved size as a column hint elsewhere (out of scope here; Add dialog only).

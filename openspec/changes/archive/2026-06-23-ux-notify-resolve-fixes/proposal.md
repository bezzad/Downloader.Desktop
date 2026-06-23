## Why

Six rough edges hurt everyday use: the Add dialog doesn't show a name/size before download, the "Failed" filter wrongly lists user-stopped items, expired/anti-bot links fail with confusing states, the bundled sample plugin no longer loads (predates the SDK rename), toast text can't be copied for bug reports, and in-app vs OS notifications fire inconsistently. Fixing them together raises the app's "easy + trustworthy" baseline before any media-plugin work.

## What Changes

- **Add dialog — pre-download name & size**: when a **single** link is entered, auto-resolve (debounced, non-blocking) the file name + size via the engine's remote-file resolver and prefill the File name box + show the size. When the size is unknown, the item downloads as **1 part**. When **multiple** links are entered, the File name box is **disabled** (only the folder matters).
- **Status filter fix**: the **Failed** footer filter shows real failures only; user-**Stopped** items appear only under **All** (no longer bucketed with Failed). **BREAKING** (filter semantics) vs the previous Failed=Failed+Stopped grouping.
- **Expired/invalid link detection**: a download whose response is non-file content (e.g. `text/html`) or an implausibly small text body is marked **Failed** with a clear "Link expired or invalid" message instead of a confusing partial/complete state.
- **Sample plugin compatibility**: update the bundled `samples/Downloader.Desktop.SamplePlugin` to the current SDK (`ILinkResolver`/`DownloadPart`) so it loads instead of erroring "not a Downloader plugin".
- **Copyable toasts**: every in-app toast gets a copy button that copies `title + message` to the clipboard.
- **Focus-aware notification routing**: when the app **is focused**, all messages (errors, completion, updates, failures, plugins…) show as **in-app toasts**; when the app is **unfocused or in tray**, they show as **OS notifications**. Actionable notifications (e.g. "Update available") send a plain OS notification while unfocused and **re-show the clickable in-app toast when the app regains focus**.

## Capabilities

### New Capabilities
- `notifications`: how user-facing messages are routed (focus-aware in-app vs OS) and presented (copyable content, actionable re-show on focus).
- `add-download`: pre-download name/size resolution for a single link and input handling for single vs multiple links.
- `download-status`: status-filter buckets in the main window and expired/invalid-link failure detection.
- `plugins`: the bundled sample plugin must implement the current SDK and load without error.

### Modified Capabilities
<!-- none — openspec/specs/ is currently empty -->

## Impact

- **Views/VMs**: `AddDownloadItemView(.axaml/.cs)` + `AddDownloadItemViewModel` (resolve + multi-link disable), `MainWindow.axaml`/`MainViewModel` + `DownloadsViewModel`/`Navigation` (filter buckets), toast UI in `NotificationService`/`MainView`.
- **Services**: `NotificationService` (focus routing, copy button, actionable re-show), `DownloadManager`/`UrlResolver` (expired-link heuristic, single-part-when-unknown-size), window-activation tracking.
- **Plugin sample**: `samples/Downloader.Desktop.SamplePlugin` rebuilt against current `Downloader.Desktop.Plugins.Abstractions`.
- **Engine**: uses the existing `Downloader` remote-file/`FilenameResolver` API (no new dependency).
- **Tests**: new headless/unit tests for filter buckets, expired-link heuristic, focus routing decision, and sample-plugin load.

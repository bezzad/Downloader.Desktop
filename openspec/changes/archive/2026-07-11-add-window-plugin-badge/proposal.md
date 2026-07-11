# Add-window resolver badge + page-download false-failure fix

## Why

Pasting a plain web-page URL (e.g. `https://hermes-agent.nousresearch.com/docs/`) gives no feedback about who will handle it, and the plain download then **falsely fails**: the saved file is small HTML, so the expired-link heuristic marks it Failed ("link expired") even though HTML is exactly what a page URL produces. The user can't tell whether a pasted link will be a plain engine download or go through a plugin.

## What Changes

- **Fix**: `LooksExpiredOrInvalid` is skipped when the item's own URL looks like a web page (no/HTML-ish path extension) — markup output is then expected, so the row completes normally.
- **Feature**: the Add window shows a badge naming the plugin whose resolver claims the current single pasted URL ("Handled by ‹plugin name›"), computed from the cheap sync `CanResolve` pass (fallback ordering respected). No badge = plain engine download.

## Capabilities

### New Capabilities
_None._

### Modified Capabilities
- `add-download`: resolver-badge requirement (who handles the pasted link).
- `download-status`: page-URL downloads must not be flagged as expired links.

## Impact

- `Services/DownloadManager.cs` (heuristic guard + pure helper), `Services/PluginManager.cs` (resolver plugin *name* lookup), `ViewModels/AddDownloadItemViewModel.cs` + `MainViewModel.cs` (badge seam), `Views/AddDownloadItemView.axaml`, i18n key in all 16 packs, unit/headless tests.

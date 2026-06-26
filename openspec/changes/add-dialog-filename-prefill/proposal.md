## Why

Users expect the Add dialog to show the real file name for the URL they are currently entering. Right now, if the URL changes after an auto-filled name is already present, the name can stay stale, which is confusing and makes users unsure what will be downloaded.

## What Changes

- Ensure single-link filename prefill always tracks the currently entered URL when the name field is still auto-managed.
- Preserve user control: if a user manually edits the File name field, their custom name is not overwritten by later resolver results.
- Keep existing non-blocking debounce and size-resolution behavior unchanged.

## Capabilities

### New Capabilities
- *(none)*

### Modified Capabilities
- `add-download`: tighten single-link filename prefill behavior so the displayed name reflects the current URL unless the user has explicitly typed a custom name.

## Impact

- Affected code: `src/Downloader.Desktop/ViewModels/AddDownloadItemViewModel.cs` (resolver apply logic and auto/manual name ownership handling).
- Affected tests: add or update unit/headless tests around URL changes, auto-prefill updates, and manual-override behavior.
- No new dependencies or external services.

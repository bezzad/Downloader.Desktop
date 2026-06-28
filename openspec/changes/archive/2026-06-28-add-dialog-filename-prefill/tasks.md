## 1. Add-dialog filename ownership logic

- [x] 1.1 Update `AddDownloadItemViewModel` so single-link resolver results refresh the filename when it is still auto-managed (including when URL changes from one single link to another).
- [x] 1.2 Preserve manual override behavior: once user types a custom filename, resolver updates must not overwrite it until the field is cleared.

## 2. Tests and verification

- [x] 2.1 Add/adjust tests for URL-change prefill updates and manual-override protection in Add dialog behavior.
- [x] 2.2 Run repository tests and confirm Add-dialog single-link resolution behavior matches the updated `add-download` spec scenarios.

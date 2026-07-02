## Context

The Add dialog already performs debounced single-link metadata resolution (`UrlResolver.ResolveFileInfoAsync`) and can prefill file name + size. The current apply logic only fills the filename when the textbox is empty. If a previously auto-filled name exists and the user changes the URL, the field can keep a stale value that no longer matches the current URL.

## Goals / Non-Goals

**Goals:**
- Keep filename prefill aligned with the currently entered single URL.
- Preserve explicit user edits to the filename textbox.
- Keep existing debounce/non-blocking behavior and size display behavior.

**Non-Goals:**
- Changing multi-link add behavior.
- Introducing new resolver services or external dependencies.
- Altering download-engine filename fallback behavior at start time.

## Decisions

1. Track filename ownership (auto-resolved vs user-entered) in the Add dialog VM.
   - Rationale: we need deterministic overwrite rules when URL changes.
   - Alternative considered: always overwrite on each resolve result; rejected because it would clobber user custom names.

2. Update auto-managed filename on each successful single-link resolve, not only when textbox is blank.
   - Rationale: ensures URL changes produce matching filename previews.
   - Alternative considered: only update when URL loses focus; rejected because it delays feedback and adds extra state complexity.

3. Keep user override sticky until user clears or replaces with empty value.
   - Rationale: respects user intent and existing UX expectation that manual typing wins.
   - Alternative considered: reset manual mode on URL change; rejected because it unexpectedly discards user edits.

## Risks / Trade-offs

- [Risk] Rapid URL edits can race resolver responses and update UI with stale data.
  - Mitigation: keep existing cancellation token + "input still matches resolved URL" checks before applying.
- [Risk] Misclassifying auto-updated text as user input can freeze future auto-updates.
  - Mitigation: set auto-filled values through an internal path that does not mark manual-entry ownership.
- [Trade-off] Extra VM state for ownership adds small complexity.
  - Mitigation: keep state local to `AddDownloadItemViewModel` and cover with focused tests.

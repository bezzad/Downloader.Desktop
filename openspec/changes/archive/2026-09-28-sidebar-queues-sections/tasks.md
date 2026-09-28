## 1. On by default

- [x] 1.1 `Config`: `IsCategorySidebarOpen` default true; `CurrentSchemaVersion` 2 → 3; `EnsureValid` shows the sidebar once when `SchemaVersion < 3`
- [x] 1.2 Tests: new config → shown; v2 config with hidden → shown once, then a saved "hidden" is kept

## 2. Queue filter + single selection

- [x] 2.1 `DownloadsViewModel.QueueFilter` in `Matches`; `MatchesExceptSidebar` for counts
- [x] 2.2 `MainViewModel.SelectedQueueId`; selecting a queue clears the category, a category clears the queue, All clears both; `ClearFilters` clears both
- [x] 2.3 `SidebarQueueRowViewModel` + `QueueRows`, rebuilt on `QueuesChanged` (UI thread); removed selected queue → All
- [x] 2.4 Tests: queue filter lists only that queue (any state) + combines with status/search; single selection rules; counts; new/renamed/removed queue updates the rows; removing the selected queue falls back to All; Clear filters resets both (drive the COMMAND, not the method — skill note)

## 3. Sections

- [x] 3.1 Two section headers (Categories, Queues) with chevrons + expanded flags (default true)
- [x] 3.2 Queue rows in the Queues section (name, count, de-emphasized when empty, tooltip for long names)
- [x] 3.3 i18n: section titles reuse the existing `Cat_Sidebar` ("Categories") and `Nav_Queues` ("Queues") keys, already in all 16 packs — no new keys
- [x] 3.4 Test: collapsing a section hides its rows

## 4. Accent bar

- [x] 4.1 3 px start-edge bar on `Button.cat.selected` (category, queue, All) bound to `SystemAccentColor`
- [x] 4.2 Tests: only the selected row shows the bar; `ApplyAccent` changes its brush live; RTL puts it on the right

## 5. Drag to reorder categories

- [x] 5.1 `CategoryService.MoveTo(id, index)` (clamped below All, persists, raises `Changed`)
- [x] 5.2 Hover grip on category rows (not All, not queues); pointer-capture drag with drop-target highlight; release → `MoveTo`
- [x] 5.3 Tests: `MoveTo` order + persistence + clamp; grip visibility rules; a simulated drag (pointer events on the grip) reorders

## 6. Finish

- [x] 6.1 Full solution `-t:Rebuild` → 0 warnings; `dotnet test` green
- [x] 6.2 Regenerate `docs/screenshots/` and add captures: sidebar with both sections and a selected queue (accent bar), a category row hovered with the grip, light + dark; view every changed PNG before committing
- [x] 6.3 Update CLAUDE.md (sidebar now on by default, sections) + skill notes; commit + push to `develop`

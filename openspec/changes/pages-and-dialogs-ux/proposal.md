# Pages & dialogs UX: sticky titles, settings search, section defaults, distinct modals, schedule naming

## Why

Five UX papercuts across the management pages and modals:

- **7 — Page titles scroll away.** On long pages the title scrolls out of view, so users lose context of which page they're on.
- **14 — Schedule name collides with the button.** A newly-created schedule is named the same as the "New schedule" button, so users can't tell the item from the action.
- **15 — No way to find a setting.** The Settings page has many options with no search; users hunt visually.
- **16 — Section defaults.** The Plugins section should be expanded by default; other Settings sections should be collapsible but expanded by default.
- **17 — Modals blend into the window.** Modal dialogs (About, Add, Details, etc.) share the main window's border/background, so users don't realize a modal is open (they click the blocked main window and nothing happens).

## What Changes

- **Sticky page titles**: each page's title stays pinned at the top while its content scrolls.
- **Numbered schedule names**: a new schedule is named `Schedule 1`, `Schedule 2`, … distinct from the "New schedule" button.
- **Settings search box**: a search field left of "Reset to defaults" that **filters** the visible options (hides unrelated ones, auto-expands matching sections, highlights the matched text); clearing restores everything. (Filter chosen over highlight-only — see design.)
- **Section defaults**: Plugins section expanded by default; all Settings sections collapsible and expanded by default.
- **Distinct modal chrome**: every modal gets a clearly different border/background (e.g. an accent border + subtle scrim/elevation) so it's obviously a foreground dialog over the disabled main window.

## Capabilities

### New Capabilities
- `settings`: a search box that filters/highlights options; sections collapsible & expanded by default with Plugins expanded.

### Modified Capabilities
- `ui-navigation`: page titles are sticky while scrolling; schedules get distinct numbered default names.
- `window-chrome`: modal dialogs are visually distinct (border/background/elevation) from the main window.

## Impact

- `Views/*.axaml` for each page (title in a non-scrolling header row above the `ScrollViewer`).
- `ViewModels/SchedulerViewModel.cs` (numbered default names, next-number logic).
- `ViewModels/SettingViewModel.cs` + `Views/SettingView.axaml` (search text → filter/highlight; `Expander` per section, Plugins `IsExpanded=true`).
- `App.axaml` / dialog window styles (`AddDownloadItemView`, `DownloadDetailsView`, `AboutView`, `UpdatePromptView`, `ShutdownView`) — distinct modal border/background.
- i18n keys (search placeholder, "Schedule {0}") in all 16 packs.
- Tests: schedule numbering; settings filter hides non-matches & auto-expands matches; sticky title + modal-distinct verified via headless screenshots.

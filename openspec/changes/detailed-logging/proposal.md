## Why

The author turned logging on to test the app by hand and found that the log records almost nothing he
did: no setting changes, no menu or button clicks, no page changes. Today about 80 log calls in 20
files cover errors, engine messages and a few lifecycle events, and an error keeps only its message,
not where it happened. A log that cannot answer "what did the user do, and what did the app do next"
is useless for real-usage testing and for bug reports — including the tray diagnosis that
`tray-popup-menu` needs from the author's Ubuntu machine.

## What Changes

- **Logging stays off by default. When the user turns it on, it is fully detailed** — one switch, no
  levels to choose.
- **Every user action is logged**: button, toolbar, menu and context-menu clicks, toggles, page
  navigation, dialogs opened/closed with their result, tray icon clicks and tray menu items.
- **Every setting change is logged as name + old → new value**; secret-like values are masked (`***`).
- **Everything the app does in response is logged**: download added / started / paused / resumed /
  stopped / retried (with the reason) / completed / failed, queue and scheduler decisions, URL
  failover and connection back-off, plugin calls with their duration, update checks, local-API routes.
- **Errors carry the full stack trace.**
- **URLs are written as scheme + host + path only** — the query, fragment and any user:password part
  are removed, so a signed link's token never reaches the file. Cookies, headers and the text the user
  types are never logged (existing contract, now enforced in one place).
- **Each app start writes a header**: app version, OS/arch, .NET runtime, language, theme, and the
  non-secret settings.
- **Log files older than 7 days are deleted automatically** (at startup and when the day changes).
- **Export log saves every kept log file (up to 7 days) as one `.zip`** and says when it could not
  (today it copies only today's file and fails silently).

## Capabilities

### New Capabilities
- `app-logging`: what is logged when logging is on, how it is protected, how long it is kept, and how
  it is exported.

### Modified Capabilities
<!-- none: logging had no spec before -->

## Impact

- `Services/AppLog.cs` — detailed level, stack traces, one open writer instead of re-opening the file
  per line, the 7-day clean-up, the URL sanitiser, the startup header.
- New `Services/UiActionLog.cs` — ONE place that logs clicks/toggles/navigation through Avalonia class
  handlers (`Button.ClickEvent`, `MenuItem.ClickEvent`, `ToggleButton` checked changes) registered in
  `App`, so no view or view model has to be edited one by one.
- `ViewModels/SettingViewModel.cs` / `MainViewModel.SaveSoon` — settings diff (old → new) logged on each
  save; a pure `SettingsDiff` with masking.
- `Services/DownloadManager*.cs`, `Services/TrayService.cs`, `Services/DialogHelper.cs`,
  `Services/PluginManager.cs`, `Services/UpdateFlow.cs` — the missing state-transition / decision lines,
  through `ILogger` from `AppLog.Factory` (the CLAUDE.md logging rule). Existing `AppLog.Info` calls are
  left as they are (they already reach the same file).
- `ViewModels/SettingViewModel.ExportLogs` — zip of the kept files + visible success/failure.
- Tests in `Unit/` and `UI/`. No UI layout change → no screenshots needed.

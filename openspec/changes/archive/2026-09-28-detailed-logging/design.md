## Context

- `Services/AppLog` (static): `SetEnabled`, `Info/Warn/Error`, `Write` → `File.AppendAllText` per line
  (opens the file every time), one file per day `downloader-yyyy-MM-dd.log`, and `Factory`
  (`ILoggerFactory`) that bridges `ILogger` (engine, plugins) into the same file. `Error` keeps
  `Type: Message` only. Nothing deletes old files.
- Settings: `DownloadSettings.EnableLogging` (default false); Logging section has Open logs folder /
  Export log / Email logs. `ExportLogs` copies today's file only and swallows failures.
- Existing privacy contracts (skill notes): never log the local-API request URL/query; cookies and
  headers are memory-only secrets.

## Goals / Non-Goals

**Goals:** a log that reads as a timeline of user actions and app reactions; safe to share; bounded
in size by age; no per-view boilerplate.

**Non-Goals:** logging while the switch is off; log levels in the UI; remote/telemetry upload; logging
keystrokes or typed text; rewriting the ~80 existing `AppLog.*` calls.

## Decisions

1. **One switch = everything.** When enabled, `ILogger` accepts Debug and above; when disabled nothing
   is written (unchanged). No second "detailed" option (author's decision).
2. **UI actions are logged centrally, not per command.** `UiActionLog.Register()` in `App.Initialize`
   adds class handlers: `Button.ClickEvent` (covers toolbar, dialog and icon buttons),
   `MenuItem.ClickEvent` (menus, context menus, flyouts), `ToggleButton.IsCheckedChanged` (checkboxes,
   toggle switches, the sidebar toggle). Each line names the control's label (header/content text or
   tooltip), its `x:Name` if any, the owning view, and the target (the `DataContext`'s display name, e.g.
   the download's file name). One file, testable by raising the events on real controls headlessly.
3. **Page navigation and dialogs** are logged where they already funnel: `MainViewModel.Navigate` and
   `DialogHelper` (`BeginModal` + the result on close).
4. **Settings: diff on save.** `SettingsDiff.Compute(before, after)` (pure) compares `DownloadSettings`
   public properties and returns `name: old → new` lines; values for names matching
   `password|secret|token|cookie|key` and the userinfo of `ProxyAddress` are masked. The snapshot is taken
   after load and after each save, so each change is logged once, including changes made by Reset or
   Import.
5. **URLs go through one sanitiser**: `LogText.Url(string)` → `scheme://host[:port]/path`; anything
   unparsable → `<url>`. Every new log line that mentions a URL uses it; the existing download-start
   lines that print a URL are routed through it too.
6. **Stack traces**: `Error(message, ex)` and the `ILogger` bridge write `ex.ToString()` (type, message,
   inner exceptions, stack).
7. **Writer**: one `StreamWriter` (append, `AutoFlush`) held under the existing lock and reopened when
   the date changes; detailed logging would otherwise reopen the file hundreds of times a minute.
   Download **progress ticks are never logged** — only transitions and per-attempt summaries — so a
   busy download does not flood the file.
8. **Retention**: `AppLog.Prune(now, days: 7)` deletes `downloader-*.log` files whose date in the name
   is older than 7 days; runs at startup and on day roll-over. Pure date logic, tested with a temp
   folder; never touches other files in the folder.
9. **Startup header** (first line of each session): version, OS/arch, runtime, language, theme, and
   the non-secret settings (through the same masking as decision 4).
10. **Export**: all `downloader-*.log` files kept by retention → one zip (`System.IO.Compression`),
    through the existing save picker; a success or failure message is shown (NotificationService.Inform).
11. **How the author sends a log** (documented in the Settings hint and the skill): Settings → Logging
    → Export log → attach the saved zip in the chat with the AI (or to a GitHub issue).

## Risks / Trade-offs

- [Class handlers log labels in the current language] → accepted: the log is read by the author, and
  the view + control name make the source unambiguous.
- [A click on a control whose DataContext is a secret-bearing object] → only display names are read,
  never URLs/cookies; the target text passes through `LogText.Url` when it looks like a URL.
- [File size in detailed mode] → no progress ticks + 7-day retention keep it bounded.
- [A bug in the writer must never break the app] → the existing "logging never throws" rule stays;
  a failing write is dropped.

## 1. Core logger

- [ ] 1.1 `AppLog`: accept Debug+ when enabled; `Error`/`ILogger` bridge write `ex.ToString()`
- [ ] 1.2 One held `StreamWriter` (append, AutoFlush) reopened on date change; writes never throw
- [ ] 1.3 `LogText.Url` sanitiser (scheme+host+port+path; strip query, fragment, userinfo) + tests (signed link, userinfo, IPv6, unparsable)
- [ ] 1.4 `AppLog.Prune(now, 7)` at startup + day roll-over + tests (old deleted, recent kept, foreign file kept)
- [ ] 1.5 Startup header (version, OS/arch, runtime, language, theme, masked settings) + test

## 2. User actions (one place)

- [ ] 2.1 `UiActionLog.Register()` in `App.Initialize`: class handlers for `Button.ClickEvent`, `MenuItem.ClickEvent`, `ToggleButton` checked changes → label/name/view/target line
- [ ] 2.2 `MainViewModel.Navigate` logs the page; `DialogHelper` logs dialog open + result
- [ ] 2.3 Tray: icon clicks + menu items logged (feeds the `tray-popup-menu` diagnosis)
- [ ] 2.4 Tests (headless): clicking a real Button / MenuItem / ToggleSwitch writes the expected line; a TextBox edit writes nothing

## 3. Settings diff

- [ ] 3.1 Pure `SettingsDiff.Compute(before, after)` with masking; snapshot after load and each save; also after Reset and Import
- [ ] 3.2 Tests: number change `3 → 5`; proxy userinfo masked; Reset logs every changed value once; unchanged save logs nothing

## 4. App reactions

- [ ] 4.1 `DownloadManager`: add/start/pause/resume/stop/retry(reason)/complete/fail, pump + scheduler decisions, fail-over, back-off (URLs via `LogText.Url`; no progress ticks)
- [ ] 4.2 `PluginManager` calls with duration; `UpdateFlow` checks; local-API route names (never the query)
- [ ] 4.3 Tests: a failed download logs a stack trace; one minute of progress writes no tick lines; an extension add with cookies/headers leaves none in the file

## 5. Export

- [ ] 5.1 Export log = zip of all kept `downloader-*.log` files; success/failure/nothing-to-export message
- [ ] 5.2 Settings hint text: how to send a log (Export log → attach the zip); i18n in `en.json` + all 16 packs
- [ ] 5.3 Tests: three days → one zip with three entries; no files → "nothing to export"

## 6. Finish

- [ ] 6.1 Full solution `-t:Rebuild` → 0 warnings; `dotnet test` green (+ extension suites per the standing rule)
- [ ] 6.2 Skill note: how to read/ask for a log, the sanitiser rule for new log lines; commit + push to `develop`

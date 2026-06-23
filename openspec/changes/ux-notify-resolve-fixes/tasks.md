## 1. Status filter fix (#2)

- [ ] 1.1 In `DownloadsViewModel.Matches`, change the `StatusFilter.Failed` arm to `vm.Status is DownloadStatus.Failed` (drop `or Stopped`)
- [ ] 1.2 In `MainViewModel.FailedFilterCount`, count `Failed` only (drop `or DownloadStatus.Stopped`)
- [ ] 1.3 Add/adjust a headless test asserting Stopped items show under All but not under Failed

## 2. Add dialog — name & size pre-resolution (#1)

- [ ] 2.1 Identify `RemoteFileInfo`'s name + size property names against the Downloader 5.9.0 package
- [ ] 2.2 In `AddDownloadItemViewModel`, count links on URL-text change; expose `IsSingleLink`, `SizeText`, and a `Resolving` flag
- [ ] 2.3 On a single link, debounce ~600 ms and call `UrlResolver.ResolveFileInfoAsync`; per-keystroke `CancellationTokenSource`; apply result only if URL still matches and the user hasn't typed a name
- [ ] 2.4 Bind the resolved name into the File name box and show the size + a transient "Resolving…" indication in `AddDownloadItemView.axaml`
- [ ] 2.5 When more than one link is entered, disable the File name box (bind `IsEnabled` to `IsSingleLink`); re-enable on revert to one link
- [ ] 2.6 Add an i18n key for the size/"Resolving…" label in `en.json`
- [ ] 2.7 Force single-part download when the started item's size is unknown (chunk count = 1 in `DownloadManager`)
- [ ] 2.8 Headless test: single-link resolves name+size (loopback server); multi-link disables the name box

## 3. Expired/invalid link detection (#3)

- [ ] 3.1 Add a pure predicate (e.g. `LooksExpiredOrInvalid(contentType, finalBytes, expectedBinary)`) alongside the existing `LooksCorruptedAfterResume`/`LooksAlreadyDownloaded` helpers
- [ ] 3.2 Wire it into `DownloadManager`'s completion/start path: when triggered, set `Status=Failed` with a localized "Link expired or invalid" message
- [ ] 3.3 Add an i18n key for the failure message in `en.json`
- [ ] 3.4 Unit tests: html content-type → flagged; tiny text body → flagged; genuine small real file → NOT flagged

## 4. Sample plugin loads (#4)

- [ ] 4.1 Add `samples/Downloader.Desktop.SamplePlugin` to `Downloader.Desktop.sln` (or an explicit build step) so a normal build keeps the DLL fresh
- [ ] 4.2 Build the sample and confirm it produces a loadable DLL + `.deps.json` against the current Abstractions
- [ ] 4.3 Add a host-mirroring `AssemblyLoadContext` test that loads the built sample DLL and asserts `is IDownloaderPlugin` + a registered resolver
- [ ] 4.4 Manually verify Install Plugins on the freshly-built sample shows "Plugin installed", not "not a Downloader plugin"

## 5. Copyable toasts (#5)

- [ ] 5.1 Replace the bare `Notification` in `NotificationService.ShowInApp` with a small custom toast view (icon + text + copy button)
- [ ] 5.2 Wire the copy button to write `"{title}: {message}"` to the `TopLevel` clipboard
- [ ] 5.3 Verify copy works for an error toast and a normal toast

## 6. Focus-aware notification routing (#6)

- [ ] 6.1 Add `NotificationService.AppFocused`, updated from window `Activated`/`Deactivated` (any app window active ⇒ focused); subscribe in `MainViewModel.SetupAppShell()`
- [ ] 6.2 In `Notify`, route by focus: focused ⇒ in-app only; unfocused/tray ⇒ native OS only (no double-fire)
- [ ] 6.3 In `ShowAction`, when unfocused send a plain OS notification and enqueue the actionable toast; flush the queue on next `Activated` (re-show in-app)
- [ ] 6.4 Audit all callers (complete/fail/all-complete/update/plugins) so each goes through the focus-routed path
- [ ] 6.5 Unit test the routing decision (focused→in-app, unfocused→OS) and the actionable re-show queue

## 7. Verification & wrap-up

- [ ] 7.1 `dotnet build Downloader.Desktop.sln` clean (0 warnings)
- [ ] 7.2 `dotnet test` green (including the new tests)
- [ ] 7.3 If Add-dialog/footer UI changed, regenerate `docs/screenshots/` (`DLDESKTOP_CAPTURE=1 dotnet test --filter ...CaptureScreenshots`) and eyeball them
- [ ] 7.4 Append recurring patterns (focus routing, debounced resolve, expired heuristic, sample-in-solution) to `.claude/skills/downloader-desktop/SKILL.md`
- [ ] 7.5 Update `PLAN.md` (and `TASKS.md`) with this batch; commit + push to `develop`

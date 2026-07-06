## 1. Durable download state across restart

- [x] 1.1 Add a plain regression test: no schedules configured, several items Stopped (incl. via Stop All), `Initialize` runs on a fresh `DownloadManager` → assert every item is still Stopped and nothing auto-starts. This must reproduce the reported bug (or rule out `Initialize` itself as the cause) before any fix lands.
  > `DownloadStateDurabilityTests.StopAll_survives_a_restart_with_no_schedules_configured` — confirmed `Initialize` itself was already correct (only Running/Paused get normalized); the bug needed a second manager instance + a schedule to reproduce (see 1.2).
- [x] 1.2 Add a scheduler-specific regression test: an enabled schedule whose window contains "now", simulating that it already fired earlier the same day, then a fresh process's first `EvaluateSchedules` tick → assert it does NOT re-trigger `StartQueue` and the previously-stopped items stay Stopped.
  > `Schedule_that_already_fired_today_does_not_refire_on_restart` — reproduced the bug against the OLD code (in-memory `_firedKeys`/`_firedDay` reset every process, so this failed before the fix) and now passes against the fix.
- [x] 1.3 Add `LastFiredStartDate`/`LastFiredStopDate` (nullable date) to `DownloadSchedule`, persisted with `Config`.
- [x] 1.4 Update `EvaluateSchedules`/`TriggerStart`/`TriggerStop` to check/set the persisted date instead of (or alongside) the in-memory `_firedKeys`/`_firedDay`, so a schedule that already fired today does not re-fire after a restart; a schedule that hasn't fired yet today still fires normally (including on a restart that lands inside its window for the first time that day).
  > Replaced `_firedKeys`/`_firedDay` entirely (no longer needed — per-schedule date comparison already resets naturally each new day). `EvaluateSchedules` made `internal` so tests can drive it directly.
- [x] 1.5 Confirm the tests from 1.1/1.2 pass; add a "schedule fires normally when it hasn't fired today yet" test alongside them so the fix doesn't regress the legitimate catch-up case.
  > `Schedule_that_has_not_fired_today_still_fires_normally` — asserts the target queue's item actually starts AND `LastFiredStartDate` is recorded (so it won't re-fire again later the same day). All 3 tests green; full suite 300/300.

## 2. Focus-aware notifications on Windows

- [x] 2.1 Add a Windows branch to `Services/NotificationService.TryNative` that shows a native OS notification (spawn a system utility — e.g. a `powershell.exe` invocation of the WinRT toast API — no new NuGet dependency), matching the existing try/catch/return-bool contract of the Linux/macOS branches.
  > New `Services/WindowsNotifier.cs` (mirrors `MacNotifier`'s shape): spawns `powershell.exe -EncodedCommand <base64>` invoking `Windows.UI.Notifications.ToastNotificationManager`. Text travels via `-EncodedCommand` (no command-line escaping needed) and is XML-escaped with `SecurityElement.Escape` before being embedded in the script's single-quoted PowerShell string (which also strips any raw `'` that could break out of that string). Wired into `TryNative` as a new `OperatingSystem.IsWindows()` branch, same try/catch/return-bool contract.
- [x] 2.2 Verify the existing `PreferOsChannel`/`InAppVisible` routing logic needs no changes (it should already call the new Windows branch through the same `TryNative` path).
  > Confirmed unchanged — `Notify`/`ShowAction` call `TryNative` unconditionally on any non-in-app-visible path; the new Windows branch is just another case inside it. 5 existing notification/channel tests + full suite (300/300) still green, no regressions on macOS/Linux (branch is a no-op there since `OperatingSystem.IsWindows()` is false).
- [x] 2.3 Note in the change (and flag to the author) that this cannot be verified on the current dev box or CI — needs manual confirmation on an actual Windows machine.
  > Flagged in `WindowsNotifier`'s doc comment and here: this dev box is macOS and CI has no Windows runner for this repo's test suite, so the actual toast call is unverified — needs the author (or a Windows CI job) to confirm a real toast appears when the app is unfocused/tray-hidden on Windows.

## 3. Notch overlay expanded size

- [ ] 3.1 Replace `NotchView.axaml.cs`'s hardcoded `ExpandedWidth/Height = 400, 210` with named constants computed from the actual content metrics (header height, top/bottom padding, per-row height including its progress bar and inter-row spacing, × `NotchViewModel.MaxRows`).
- [ ] 3.2 Regenerate the gated `CaptureNotchMockups` PNGs (Linux-only per standing convention) and visually confirm the expanded panel now hugs its content with only a small margin below the third row.

## 4. Global vs. per-item speed limit

- [ ] 4.1 Add `HasCustomSpeedLimit` (bool) and `CustomSpeedLimitBytesPerSecond` (long) to `Models/DownloadItem.cs`, defaulting to `false`/`0` so existing saved configs deserialize unaffected.
- [ ] 4.2 Mirror both fields on `DownloadItemViewModel` (write-through to `_item`, like `Status`).
- [ ] 4.3 Update `DownloadDetailsViewModel.SpeedLimitKb`'s setter to also set `Item.HasCustomSpeedLimit = true` and persist `Item.CustomSpeedLimitBytesPerSecond`, alongside the existing live `Item.Configuration.MaximumBytesPerSecond` mutation.
- [ ] 4.4 Add a small "Use global limit" toggle/button next to the details dialog's speed-limit `NumericUpDown` that clears `HasCustomSpeedLimit` and immediately re-applies the current global value.
- [ ] 4.5 Update `DownloadManager.Start(vm)` to apply `vm.GetItem().CustomSpeedLimitBytesPerSecond` to the freshly-built `DownloadConfiguration` when `HasCustomSpeedLimit` is true, instead of always taking the current Settings value.
- [ ] 4.6 Add `DownloadManager.ApplyGlobalSpeedLimit(long bytesPerSecond)`: iterate `Items`, for every item WITHOUT `HasCustomSpeedLimit`, set `vm.Configuration.MaximumBytesPerSecond` live (no-op safely if `Configuration` is null/stopped).
- [ ] 4.7 Call `ApplyGlobalSpeedLimit` from `SettingViewModel`'s speed-limit setter after writing through to `Settings.MaximumBytesPerSecond`, mirroring the existing `MaxConcurrentDownloads` → `DefaultQueue` sync-on-change pattern.
- [ ] 4.8 Tests: global limit change reaches a Running item without a custom limit; a custom-limited Running item is untouched by a global change; a custom limit survives Stop → Resume and a simulated restart (`Initialize` + `Start`); reverting to "use global" re-applies the current global value and re-subscribes the item to future global changes.

## 5. Main window resize from the left/top edge

- [ ] 5.1 Extract the resize math (given a drag-start snapshot — pointer position, window position, window size — plus a current pointer position, compute the new width/height/position) into a small pure/static method so it can be unit-tested without a real pointer drag.
- [ ] 5.2 Update `ResizeGrips.OnPressed` to capture the drag-start snapshot (screen-space pointer position + `_window.Position` + `_window.Bounds.Width/Height`) once.
- [ ] 5.3 Update `ResizeGrips.OnMoved` to compute every frame's width/height/position from that fixed snapshot plus the current pointer's screen-space delta — not by re-reading `_window.Bounds`/`_window.Position` (today's per-frame-relative-to-window approach that can compound error across rapid events).
- [ ] 5.4 Keep the existing `MinWidth/MinHeight/MaxWidth/MaxHeight` clamps; add a final clamp so the window's position can't end up entirely outside every screen's working area, as a last-resort guard.
- [ ] 5.5 Unit test the extracted math (5.1) with a simulated fast multi-step drag sequence (many small deltas in quick succession) from each edge/corner, asserting the final size/position matches a single equivalent large delta — the property that was broken before (compounding error).
- [ ] 5.6 Manual verification (author): drag every edge and corner of the main window, both slowly and quickly, and confirm the window never disappears or ends up off-screen. Headless Avalonia cannot simulate a real multi-frame OS-level drag, so this step cannot be automated.

## 6. Wrap-up

- [ ] 6.1 Run the full standing verification: `dotnet build Downloader.Desktop.sln`, `dotnet test` (all suites green).
- [ ] 6.2 Append any non-obvious gotchas found while implementing (scheduler date-tracking, speed-limit propagation, Windows notification mechanism, resize-anchor math) to `.claude/skills/downloader-desktop/SKILL.md`.
- [ ] 6.3 If any view's UI changed (notch panel size, details dialog's new toggle), regenerate `docs/screenshots/`/notch mockups on Linux and visually verify before committing.

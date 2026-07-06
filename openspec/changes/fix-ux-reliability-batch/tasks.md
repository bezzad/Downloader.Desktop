## 1. Durable download state across restart

- [ ] 1.1 Add a plain regression test: no schedules configured, several items Stopped (incl. via Stop All), `Initialize` runs on a fresh `DownloadManager` → assert every item is still Stopped and nothing auto-starts. This must reproduce the reported bug (or rule out `Initialize` itself as the cause) before any fix lands.
- [ ] 1.2 Add a scheduler-specific regression test: an enabled schedule whose window contains "now", simulating that it already fired earlier the same day, then a fresh process's first `EvaluateSchedules` tick → assert it does NOT re-trigger `StartQueue` and the previously-stopped items stay Stopped.
- [ ] 1.3 Add `LastFiredStartDate`/`LastFiredStopDate` (nullable date) to `DownloadSchedule`, persisted with `Config`.
- [ ] 1.4 Update `EvaluateSchedules`/`TriggerStart`/`TriggerStop` to check/set the persisted date instead of (or alongside) the in-memory `_firedKeys`/`_firedDay`, so a schedule that already fired today does not re-fire after a restart; a schedule that hasn't fired yet today still fires normally (including on a restart that lands inside its window for the first time that day).
- [ ] 1.5 Confirm the tests from 1.1/1.2 pass; add a "schedule fires normally when it hasn't fired today yet" test alongside them so the fix doesn't regress the legitimate catch-up case.

## 2. Focus-aware notifications on Windows

- [ ] 2.1 Add a Windows branch to `Services/NotificationService.TryNative` that shows a native OS notification (spawn a system utility — e.g. a `powershell.exe` invocation of the WinRT toast API — no new NuGet dependency), matching the existing try/catch/return-bool contract of the Linux/macOS branches.
- [ ] 2.2 Verify the existing `PreferOsChannel`/`InAppVisible` routing logic needs no changes (it should already call the new Windows branch through the same `TryNative` path).
- [ ] 2.3 Note in the change (and flag to the author) that this cannot be verified on the current dev box or CI — needs manual confirmation on an actual Windows machine.

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

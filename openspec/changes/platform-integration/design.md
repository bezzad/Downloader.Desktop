# Design — platform-integration

## Context
Tray/startup/update live in `Services/{TrayService,StartupService,UpdateService,UpdateFlow}` and `SingleInstanceService`. Close-to-tray hides the window; `TrayIcon.Clicked → ShowWindow` is wired but on some DEs Clicked doesn't fire (see skill notes). The self-update swaps files on exit via `App.DesktopOnShutdownRequested → UpdateFlow.ApplyPendingOnExit`. Packaging targets today: GitHub releases, Snap, winget, Homebrew, curl script — no AUR. `DownloadManager` already computes aggregate progress for the notch/queues.

## Goals / Non-Goals
**Goals:** OS taskbar/dock progress where supported; AUR install path; reliable first-click window restore; correctly diagnose (and fix if in-scope) the winget install and Windows update-restart failures.
**Non-Goals:** guaranteeing headless verification of Windows-only behaviors (taskbar overlay, update swap, winget) — those are flagged for on-device confirmation; supporting non-Arch distros via AUR (out of AUR's scope).

## Decisions
1. **Taskbar progress, platform-gated.** Aggregate progress = mean of active items' `Progress` (reuse the notch/queues computation), 0..1. Windows: `ITaskbarList3::SetProgressValue`/`SetProgressState` via COM interop (Avalonia 12 has no cross-platform taskbar-progress API, so Windows needs interop against the main window HWND). Linux: `com.canonical.Unity.LauncherEntry` DBus signal (`progress`/`progress-visible`) — honored by GNOME (with the extension)/KDE/Unity/Dock docks; no-op elsewhere. macOS: Dock tile progress via `NSDockTile`/badge if reachable through the mac workload, else a documented skip. All behind a small `ITaskbarProgress` abstraction with per-OS impls + a headless no-op; only the *value* computation is unit-tested.
2. **First-click show.** Ensure `TrayService.ShowWindow` (already `Dispatcher.UIThread.Post` + topmost flip) is invoked on the first activation. The "second click needed" symptom suggests the first activation only toggles icon state without reaching `Show()/Activate()`, or the window is shown but behind. Fix by making the tray/single-instance activation always call the full show sequence (Show → WindowState=Normal → Activate → topmost flip → un-topmost) and, on Windows, force-foreground. Verify what is verifiable headlessly (the show path runs on first activation); flag the visual behavior for on-device check.
3. **winget R&D.** Reproduce with the current `packaging/winget/*` manifests (InstallerType zip + NestedInstallerType portable, `Downloader.exe` at root). Likely suspects: nested-installer path/portable handling, missing `AppsAndFeaturesEntries`, or the portable symlink. If a manifest bug is found, fix the manifests + note it; if the error is environmental/unclear, record an exact repro and the info to collect (the detailed winget error text/log) as a findings note in this change and a reminder for the author.
4. **Update-restart R&D.** Hypothesis: with tray active, the old process (or a second tray-held instance) survives close, so the swap script can't replace a locked binary and/or the relaunch attaches to the dying process group. Investigate: (a) does close-to-tray keep the process alive past the intended quit during update? (b) is `SingleInstanceService`'s lock still held? (c) the Windows swap `.cmd` waits for the PID — confirm it targets the right PID and that tray teardown happens before the swap. Add tests around the quit/exit-swap ordering (the macOS owned-windows fix is precedent). If it can't be reproduced off-Windows, produce a precise on-device test plan and tell the author to run it on the target machine.
5. **AUR = `downloader-bin`.** A binary package repacking the released `Downloader-linux-x64.tar.gz` (fastest, no build deps), `pkgver` from the release tag, `sha256sums` from the asset, `.desktop`+icon installed like `install.sh`. `scripts/release.sh` gains `submit_aur` (clone `ssh://aur@aur.archlinux.org/downloader-bin.git`, update PKGBUILD+`.SRCINFO`, commit, push) — **the first publish requires the author's AUR account + registered SSH key**; the step no-ops with a clear message if the remote/key isn't configured. Mirror the PKGBUILD in-repo under `packaging/aur/`.

## Risks / Trade-offs
- [Windows taskbar interop + macOS Dock aren't testable on the Linux CI box] → ship behind the abstraction with a no-op default; unit-test only the aggregate value; mark visual behavior for on-device verification.
- [winget/update failures may be un-reproducible without the Windows machine] → the change explicitly allows a "documented findings + on-device plan" outcome per the author's instructions (tasks 3 and 9 both permit that).
- [AUR first publish blocked on credentials] → automation lands now; actual push happens when the author provides the AUR SSH key.

## Findings (R&D tasks 3 & 9 — recorded 2026-07-17)

### Task #3 — winget install error
Everything verifiable off-Windows checks out:
- `bezzad.Downloader` IS published in winget-pkgs through **2.1.0** (versions 1.5.0…2.1.0 all merged; PR #400962 for 2.1.0 is merged).
- The merged 2.1.0 installer manifest matches the in-repo mirror byte-for-byte in the fields that matter.
- The released `Downloader-win-x64.zip` was downloaded and verified: sha256 `0AD44A7F…FEFA4` **matches** the manifest, and `Downloader.exe` sits at the zip root exactly as `NestedInstallerFiles.RelativeFilePath` declares.

So the manifest/package is NOT the bug. Most likely user-side causes, in order:
1. **Outdated App Installer / winget** — zip+portable installs need winget ≥ 1.4; older Windows 10 clients fail with "installer type not supported" (0x8a150049-family errors).
2. **Portable alias/symlink creation** without Developer Mode → winget falls back (usually a warning, sometimes surfaced as an error by wrappers).
3. A **known winget-cli limitation**: portable-in-zip may not carry the zip's `plugins/` subfolder into the install dir (install "succeeds" but built-in plugins are missing) — worth checking after any successful install.

**To close this out, collect from the failing machine:** `winget --version`, the exact command used, the full error text/code, and `%LOCALAPPDATA%\Packages\Microsoft.DesktopAppInstaller_8wekyb3d8bbwe\LocalState\DiagOutputDir\` logs from a `winget install --verbose-logs bezzad.Downloader` run.

### Task #9 — Windows update couldn't restart/replace (v1.7 → 2.1)
The v1.7 quit path already let a staged update close the app through the tray (`Closing` checks `UpdateFlow.IsReady`), so the tray-cancel theory doesn't hold for the MAIN process. Two concrete defects WERE found in the v1.7-era Windows swap script (both fixed now):
1. `timeout /t 1 /nobreak` **fails instantly** ("input redirection is not supported") in the windowless cmd we spawn → the PID wait becomes a hot spin (works, but fragile).
2. The extraction was a **single silent attempt**: if `Downloader.exe` was still locked when `Expand-Archive` ran — a *stale tray-held instance from an earlier session* (matches the author's tray suspicion) or an antivirus scanning the fresh download — the script fell through and `start`-ed the **old** exe: "downloaded successfully but couldn't replace/restart".
Fix shipped: redirect-safe `ping` sleeps + extraction retried up to ~60s until the exe is replaceable, relaunching only afterwards (guarded by `UpdateSwapScriptTests`).

**If it recurs on the target machine, collect:** Task Manager → how many `Downloader.exe` processes exist BEFORE clicking restart; whether `%TEMP%\downloader-update-<pid>.cmd` exists afterwards; and the app dir's `Downloader.exe` timestamp (replaced or not).

# Platform integration: taskbar progress, tray fixes, winget/update R&D, AUR/yay

## Why

Five platform/OS-integration items, several investigative:

- **3 — winget install fails on Windows (R&D).** A user hit an error installing via winget. Root-cause it; fix if found, else record the exact failure to reproduce and a note to gather the detailed error message.
- **4 — Taskbar progress.** Windows users want the app's taskbar icon to show overall download progress (like a progress overlay). Implement it if Avalonia supports it on Windows; extend to Linux/macOS if their platforms support an equivalent (Unity launcher progress / macOS Dock badge).
- **5 — AUR/yay for Arch users.** Arch users don't use Snap; they use `yay` (AUR). Publish the app to the AUR so `yay -S downloader-bin` works, wired into `release.sh`.
- **6 — Tray first-click doesn't show the window.** With the tray active and the app hidden, the first taskbar click flips the icon to "open" state but doesn't surface the main window; a second click does. Fix the first click to show the window (if feasible; else document why not).
- **9 — Update can't restart on Windows (tray suspected, R&D).** A user on v1.7 updated to 2.1: download succeeded but the app couldn't restart/replace itself after closing — suspected the tray keeps a background process alive that blocks the swap. Add tests to find it; if not reproducible headlessly, tell the author to test on the target Windows machine.

## What Changes

- **Taskbar progress**: overall progress (aggregate of active downloads) shown on the taskbar/dock where the platform supports it; cleared when idle.
- **winget R&D**: investigate + fix the install error, or record a precise repro + the info needed.
- **AUR packaging**: a `downloader-bin` PKGBUILD repacking the released `linux-x64` tarball + a `release.sh` step to push it (first publish gated on the author's AUR account/SSH key).
- **Tray first-click**: surface the main window on the first tray/taskbar activation.
- **Update-restart R&D**: tests around the tray/single-instance/quit path that could block the self-swap; a clear go/no-go on whether an on-device Windows test is needed.

## Capabilities

### New Capabilities
- `taskbar-progress`: the OS taskbar/dock shows aggregate download progress where supported.
- `platform-distribution`: the app is installable from the AUR (yay) on Arch-based distros.

### Modified Capabilities
- `system-tray`: the first tray/taskbar activation shows the main window; the tray does not block the update self-swap on exit.

## Impact

- `Program.cs`/`Services` for taskbar progress (Windows `ITaskbarList3` via platform interop or Avalonia API if available; Linux `com.canonical.Unity.LauncherEntry` DBus; macOS Dock tile) + wiring to `DownloadManager` aggregate progress.
- `Services/TrayService.cs`/`SingleInstanceService.cs`/`UpdateService.cs`/`UpdateFlow.cs` (first-click show; exit-swap not blocked by tray).
- `packaging/aur/PKGBUILD` (+ `.SRCINFO`), `scripts/release.sh` `submit_aur` step, README/CONTRIBUTING docs.
- `packaging/winget/*` if the winget error is a manifest bug; otherwise a findings note in this change.
- Tests: aggregate-progress value; first-activation shows window (as far as headless allows); update-exit-swap ordering with tray active; winget/update findings documented.

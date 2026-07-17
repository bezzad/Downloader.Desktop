# Tasks — platform-integration

Each task is TDD where a runtime seam exists; investigative tasks (3, 9) may end in a documented findings note + on-device plan per the author's instructions. Keep build + full `dotnet test` green, commit to `develop`, push, confirm GitHub Actions green before the next task.

## 1. Taskbar/dock progress (task #4)

- [x] 1.1 Test: the aggregate-progress value (mean of active items' progress, 0..1; 0/cleared when idle) is correct for a set of rows.
- [x] 1.2 Add `ITaskbarProgress` abstraction + Windows (ITaskbarList3 interop), Linux (Unity LauncherEntry DBus), macOS (Dock if reachable) impls + headless no-op; wire to `DownloadManager` stats. Make 1.1 pass; note Windows/macOS visual behavior needs on-device check. Build + tests green; commit/push; wait for green CI.

## 2. Tray first-click shows window (task #6)

- [x] 2.1 Test (as far as headless allows): the first tray/single-instance activation invokes the full show sequence (Show→Normal→Activate→topmost flip).
- [x] 2.2 Ensure first activation always runs the full show/foreground path (Windows force-foreground). Make 2.1 pass; flag visual confirm for on-device. Build + tests green; commit/push; wait for green CI.

## 3. AUR / yay packaging (task #5)

- [x] 3.1 Add `packaging/aur/PKGBUILD` + `.SRCINFO` for `downloader-bin` (repacks `Downloader-linux-x64.tar.gz`, `.desktop`+icon install); a test/CI lint that the PKGBUILD version+sha placeholders are wired.
- [x] 3.2 Add `submit_aur` to `scripts/release.sh` (updates PKGBUILD/.SRCINFO, pushes to AUR; no-ops with a message when credentials absent) + document the one-time AUR account/SSH setup. Build + tests green; commit/push; wait for green CI.

## 4. winget install error R&D (task #3)

- [x] 4.1 Reproduce with the current `packaging/winget/*` manifests; identify whether it's a manifest bug (nested/portable path, missing entries) or environmental.
- [x] 4.2 If a manifest bug: fix the manifests + regression note. Else: record an exact repro + the detailed error text to collect, as a findings note in this change and a reminder for the author. Commit/push; wait for green CI.

## 5. Windows update-restart R&D (task #9)

- [x] 5.1 Add tests around the quit/exit-swap ordering with tray active (does close-to-tray keep the process alive during update? is the single-instance lock released? does the swap target the right PID?).
- [x] 5.2 If a fix is found (e.g. tray teardown before swap, correct PID wait), apply it with a regression test. Else: produce a precise on-device Windows test plan and tell the author to run it on the target machine. Build + tests green; commit/push; wait for green CI.

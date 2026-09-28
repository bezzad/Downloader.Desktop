## 1. On-device diagnosis (BLOCKS the rest — needs the author's Ubuntu machine)

- [x] 1.1 Add a diagnostic switch (`DLDESKTOP_TRAY_DIAG=1`): log every tray event (`Clicked`, menu opening, item clicks) with its thread; `DLDESKTOP_TRAY_DIAG=nomenu` also skips attaching the `NativeMenu`
- [x] 1.2 Write the exact steps for the author (dev-run.sh with logging on; left + right click in both variants; `dbus-monitor "interface='org.kde.StatusNotifierItem'"`; send the zip from Settings → Logging → Export log). Build `detailed-logging` first so the tray events land in that log.
- [ ] 1.3 AUTHOR: run the steps and send the log + dbus output
- [ ] 1.4 Record the findings in design.md (decision 1 outcome) and in the skill file; pick the Linux path from them

## 2. Menu model

- [ ] 2.1 `TrayMenuViewModel`: Show downloads / Settings / Notifications switch / Exit commands, wired to the existing paths (BringToFront + Navigate, NotificationService toggle + persist, `_onQuit`), UI-thread marshalled
- [ ] 2.2 i18n keys `Tray_ShowDownloads`, `Tray_Settings`, `Tray_Notifications`, `Tray_Exit` in `en.json` + all 16 packs; remove the hard-coded English labels
- [ ] 2.3 Tests: each command reaches the right page / toggles and persists notifications / Exit calls the quit path; labels change on language switch

## 3. Popup window (Linux + Windows)

- [ ] 3.1 Pure `TrayMenuPlacement.Place(bounds, workingArea, size, rtl)` + tests for bottom/top/left/right taskbar and no inset
- [ ] 3.2 `TrayMenuView`: borderless, topmost, no taskbar entry, rounded themed card, icon rows, hover, switch row; closes on Deactivated / Esc / after a command; single reused instance
- [ ] 3.3 `TrayService`: on Linux/Windows, click (left/right as the diagnosis allows) → place + show the popup; keep the unconditional `Clicked` subscription and the 64×64 icon (skill: settled)
- [ ] 3.4 macOS: native menu with the same four items and translated labels
- [ ] 3.5 Tests (headless): the view renders all four rows in light, dark and RTL; Esc closes it; choosing a row closes it

## 4. Device checks

- [ ] 4.1 AUTHOR (Ubuntu): menu opens on left + right click, items work, closes on outside click
- [ ] 4.2 Windows smoke test (left + right click); macOS smoke test (native menu, labels)

## 5. Finish

- [ ] 5.1 Full solution `-t:Rebuild` → 0 warnings; `dotnet test` green
- [ ] 5.2 Screenshots: add a capture of the tray menu view (light + dark); view the PNGs before committing
- [ ] 5.3 Skill note replacing the "Linux tray right-click is OPEN" note with what the evidence showed; commit + push to `develop`

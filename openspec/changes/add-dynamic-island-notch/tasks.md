## 1. Mockup first (repo rule)

- [ ] 1.1 Produce a visual mockup of collapsed pill + expanded island (light/dark) for the author to
  approve before detailed XAML.

## 2. Overlay window & service

- [ ] 2.1 `Services/NotchService`: create/destroy the overlay window, top-center positioning on the
  primary screen (workArea math), fail-soft platform guards (like TrayService).
- [ ] 2.2 `Views/NotchView.axaml(.cs)`: borderless topmost transparent window, `Focusable=false`,
  `ShowInTaskbar=false`, rounded notch-style bottom corners; collapsed/expanded content presenters
  with animated size `Transitions`; hover expand + ~300 ms delayed collapse.
- [ ] 2.3 `ViewModels/NotchViewModel`: 1 s clock timer (runs only while visible), `StatsChanged`
  subscription, running rows (top 3 + overflow count), total speed text; click → bring-to-front.

## 3. Settings & lifecycle

- [ ] 3.1 `DownloadSettings.EnableNotch` (default off) + Settings row (toggle + hint); live start/stop
  from `SettingViewModel`; startup wiring in `MainViewModel.SetupAppShell` (works with close-to-tray).
- [ ] 3.2 i18n: new keys translated in ALL 16 language packs (standing rule).

## 4. Tests & verification

- [ ] 4.1 Unit: NotchViewModel clock/rows/aggregate + positioning math (pure part).
- [ ] 4.2 Headless: overlay window builds, collapsed/expanded presenters toggle.
- [ ] 4.3 Author verifies on-device on macOS (notch fit), Windows and Linux (top-center + hover);
  Linux WM quirks noted like the tray's.

## 5. Docs

- [ ] 5.1 README feature bullet + screenshot/GIF once approved; SKILL.md notes for the overlay
  window pattern.

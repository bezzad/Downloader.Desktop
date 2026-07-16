# Tasks — pages-and-dialogs-ux

Each task is TDD: failing test first (or a headless screenshot check for pure-visual items), make it pass, keep build + full `dotnet test` green, commit to `develop`, push, confirm GitHub Actions green before the next task.

## 1. Numbered schedule names (task #14)

- [ ] 1.1 Test: creating schedules yields "Schedule 1", "Schedule 2"; next-number skips existing names.
- [ ] 1.2 Implement numbered default name in `SchedulerViewModel` + `Sched_DefaultName` in all 16 packs. Make 1.1 pass; build + tests green; commit/push; wait for green CI.

## 2. Settings search box (task #15)

- [ ] 2.1 Test: `SettingViewModel.SearchText` filters options — matching rows visible + section expanded, non-matching hidden; empty restores all.
- [ ] 2.2 Implement search/filter/highlight + the search box left of "Reset to defaults" + placeholder i18n. Make 2.1 pass; build + tests green; commit/push; wait for green CI.

## 3. Section defaults (task #16)

- [ ] 3.1 Test: Settings sections are `Expander`s with `IsExpanded=true`, Plugins expanded by default.
- [ ] 3.2 Convert sections to expanders (expanded default); flip Plugins to expanded. Make 3.1 pass; build + tests green; commit/push; wait for green CI.

## 4. Sticky page titles (task #7)

- [ ] 4.1 Restructure each page so the title is outside the `ScrollViewer` (pinned top).
- [ ] 4.2 Regenerate screenshots; verify the title stays on scroll (headless capture with scrolled content). Build + tests green; commit/push; wait for green CI.

## 5. Distinct modal chrome (task #17)

- [ ] 5.1 Add a shared distinct modal look (accent border + elevation) to all modal windows.
- [ ] 5.2 Capture an About-open screenshot and verify the modal is clearly distinct from the main window. Build + tests green; commit/push; wait for green CI.

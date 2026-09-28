## Why

The main window spends a whole row (the top bar) on things that fit in the title bar, and the search
box sits beside the link box where it reads as part of "add a download". The author wants the window
to look like a modern single-bar app: the title centered, the search box at the top right, and the
search box growing when it is used. (Step 1 of 4 in the author's UI batch of 2026-09-28; order:
`titlebar-search` → `checkbox-follows-selection` → `row-context-menu` → `sidebar-queues-sections`.)

## What Changes

- The main window's title bar shows the app title **centered in the window**.
- The **search box moves from the top bar into the title bar**, just left of the window buttons.
- The search box **grows when it gets focus** (smooth width animation) and shrinks back when focus
  leaves and the box is empty. With text in it, it stays wide.
- The **Update / Donate (♥) / About (i)** buttons STAY in the top bar (the author reverted moving
  them after review, 2026-09-28).
- No menu bar (File / Tasks / Help …) is added — explicitly out of scope.
- Dialogs keep their current title bar (left-aligned title, no search, no extra buttons).

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `window-chrome`: the main window's title bar gains a centered title, the search box and the
  app-level buttons; the search box expands on focus.

## Impact

- `Views/TitleBar.axaml(.cs)` — two optional properties (`CenterTitle`, `RightContent`); dialogs do
  not set them, so they are unchanged.
- `Views/MainWindow.axaml` — search box + Update/Donate/About move into the `TitleBar`; top bar shrinks.
- `App.axaml` or `MainWindow` styles — the search box width transition.
- No view-model changes (the same `SearchText`, `ApplyUpdateCommand`, `DonateCommand`,
  `ShowAboutCommand` bindings).
- `docs/screenshots/` — every main-window capture changes; a new "search focused" capture.

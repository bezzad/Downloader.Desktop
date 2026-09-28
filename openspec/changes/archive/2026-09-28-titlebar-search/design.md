## Context

`Views/TitleBar` is one control shared by the main window and every dialog: app icon + title on the
left, caption buttons on the right, the whole bar is the drag region (`PointerPressed="OnDrag"`),
double-tap toggles maximize. The main window then has a separate top bar (sidebar toggle, link box,
Add, search box, Update, Donate, About).

## Goals / Non-Goals

**Goals:** centered title, search in the title bar, search grows on focus, app buttons in the title
bar, dialogs untouched.

**Non-Goals:** a menu bar; a keyboard shortcut for search; moving the link box or Add.

## Decisions

1. **Extend `TitleBar`, don't fork it.** Add `bool CenterTitle` (default false) and
   `object RightContent` (default null, rendered in a `ContentPresenter` just left of the caption
   buttons). Only `MainWindow` sets them. One control keeps drag / maximize / close behaviour identical
   everywhere.
2. **Dragging still works around the new controls.** The `TextBox` and `Button`s mark their own
   pointer-press as handled, so the bar's `OnDrag` handler (which is not `handledEventsToo`) does not
   start a window drag when the user clicks into the search box. Empty title-bar space still drags.
3. **Truly centered title, never overlapping.** The title is laid out centered across the whole bar
   (not centered in the leftover space, which would look off-center because the right side is much
   wider than the left). If the right-side content would reach the title (narrow window + expanded
   search), the title is hidden; it comes back when there is room. This is a small size check in
   `TitleBar` code-behind on layout, a pure `bool TitleFits(barWidth, titleWidth, leftWidth, rightWidth)`
   so it is unit-testable.
4. **Grow on focus = a style + a transition, no code.** Normal width 200, focused width 280.
   `TextBox.Transitions` with a `DoubleTransition` on `Width` (≈150 ms, ease-out); a
   `TextBox.search:focus-within` style sets the wider width; a `TextBox.search:not(:focus-within)`
   with text keeps it wide (a small `HasText` class via binding is enough if a pure selector cannot
   express "not empty"). The Fluent focus border already uses the accent color, so it follows the theme
   accent automatically.
5. **Button sizes in the title bar** match the caption area (height ~30, same `Button.icon` class)
   so the 42 px bar does not grow.

## Risks / Trade-offs

- [Minimum window width 840] At minimum width with the search focused and the Update button visible,
  the right side is wide → mitigated by decision 3 (title hides rather than overlaps).
- [Headless tests can't show a real focus animation] → assert the widths before/after focus (after the
  transition completes / by reading the target value), and view the "search focused" capture.
- [RTL languages] `FlowDirection` mirrors the grid, so the search sits on the left in fa/ar — the
  expected mirror; checked in one RTL test.

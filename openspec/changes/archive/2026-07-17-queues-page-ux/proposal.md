# Queues page: clearer toolbar + collapsible queues

## Why

1. **Confusing toolbar.** The per-queue header packs text, a checkbox/toggle, more text, a button and more text tightly together (…text · toggle · text · button · text…), so users can't tell which elements are clickable. It reads as one run of controls with no affordance grouping.
2. **Too much scrolling with many queues.** With 10 queues of 100+ items each, reaching a queue near the bottom means scrolling past hundreds of rows. There's no way to collapse a queue.

## What Changes

- **Refactor the queue header toolbar** so interactive controls are visually grouped and spaced, with clear affordance (labels attached to their control, clickable elements separated by whitespace/dividers) — a user can tell at a glance what's clickable.
- **Make each queue collapsible** (expander per queue card, like the Settings sections), plus a top-right **Collapse all / Expand all** toggle so users can fold queues and jump to the target one quickly.

## Capabilities

### Modified Capabilities
- `queues`: per-queue card is collapsible; a toolbar Collapse-all/Expand-all control; the per-queue header controls are regrouped for clear clickability.

## Impact

- `Views/QueuesView.axaml` (wrap each queue card body in an `Expander`; regroup the header controls with spacing/dividers; add the collapse/expand-all button in the page toolbar).
- `ViewModels/QueuesViewModel.cs` / `QueueRowViewModel` (`IsExpanded` per queue; `CollapseAll`/`ExpandAll` commands; persist expansion in-session).
- i18n keys for the new toolbar labels/tooltips in all 16 packs.
- Tests: collapse-all sets every queue `IsExpanded=false` (and expand-all the reverse); a headless screenshot verifies the clearer toolbar and collapsed state.

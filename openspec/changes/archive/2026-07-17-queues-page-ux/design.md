# Design — queues-page-ux

## Context
Each queue is a card (`QueuesView.axaml`) with live aggregate stats, a run/pause `ToggleSwitch`, the concurrency cap `NumericUpDown`, start/stop-queue buttons and the item list. The header lays these out in a tight row so labels and controls blur together. The page is a single `ScrollViewer` of stacked cards; with many large queues the target queue is far down. Settings already uses `Expander` sections — the same pattern fits here.

## Goals / Non-Goals
**Goals:** obvious clickable affordances in the queue header; per-queue collapse + a global collapse/expand-all; less scrolling to reach a queue.
**Non-Goals:** virtualizing the queue list itself (separate perf concern); reordering queues; changing queue semantics.

## Decisions
1. **Header regroup.** Group by function with whitespace/dividers: (a) queue name + summary (static text), (b) the run/pause toggle with its own label, (c) the cap stepper with its label, (d) an action cluster (start/stop-queue) as `Button.tool`-style buttons. Buttons get button chrome so they read as clickable; static text stays plain. Use consistent spacing (e.g. an 8–12px gap and a thin separator between groups), mirroring the app's toolbar style.
2. **Collapsible card.** Wrap each card's body (stats + item list) in an `Expander`; the header stays always visible (so aggregate stats/toggle remain reachable when collapsed) — i.e. the Expander header is the queue title row, content is the item list. Add `QueueRowViewModel.IsExpanded` (default true), bound two-way.
3. **Collapse/Expand all.** A single toolbar toggle button (top-right of the Queues page) with `CollapseAll()`/`ExpandAll()` on `QueuesViewModel` iterating the queue rows' `IsExpanded`. Label/tooltip toggles ("Collapse all" ⇄ "Expand all"). State is in-session only (not persisted) — matches Settings sections.

## Risks / Trade-offs
- [Expander header must still show live aggregate stats when collapsed] → put stats in the Expander header, not its content, so a collapsed queue still shows running/done counts + speed.
- [Regrouping could shift the numeric-coerce/behaviors already attached] → keep the same controls, only change layout containers/spacing; behaviors travel with the controls.

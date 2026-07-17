# Dialog polish: modal chrome refinement, queue-remove confirmation, in-app Donate

## Why
(a) The new accent modal border works but is heavy at 2px and the square-cornered inner content pokes into the rounded top corners (screenshot 2026-07-17). (b) Removing a queue that still has uncompleted items deletes silently. (c) Donate opens a browser page — users can't tell anything happened; it should be an in-app modal. (The author's VISA card cannot RECEIVE payments — card numbers only pull; so the modal presents the existing working channels, not a card number.)

## What Changes
- Modal border 2px → 1px; inner content clipped to the rounded corners (top corners match bottom).
- Removing a queue with uncompleted items asks for confirmation first.
- Donate becomes an in-app modal (identity + thank-you + the working channels/links); the toolbar heart opens it.

## Capabilities
### Modified
- `window-chrome`: refined modal chrome (1px accent + fully rounded clip).
- `queues`: destructive queue removal is confirmed when items are uncompleted.
- `ui-navigation`: Donate is an in-app modal.

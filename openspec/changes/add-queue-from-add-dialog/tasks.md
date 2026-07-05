## 1. ViewModel

- [x] 1.1 `AddDownloadItemViewModel`: optional `IDownloadManager manager` ctor param (MainViewModel passes
  the singleton); `AddQueueCommand`/`ConfirmAddQueueCommand`/`CancelAddQueueCommand` + `IsAddingQueue`/
  `NewQueueName`/`CanAddQueue`; `ConfirmAddQueue` creates via `manager.AddQueue`, refreshes `Queues`
  (fresh-list-per-read so the ComboBox re-enumerates — the MenuFlyout lesson) + `ShowQueuePicker`, and
  selects the new queue. Empty/whitespace name → no-op.

## 2. View

- [x] 2.1 "Add queue" button left of the Add button (same accent classes/padding per the author's spec);
  inline name TextBox + ✓/✕ buttons, Enter confirms / Esc cancels (`OnQueueNameKeyDown`); hidden when the
  dialog has no manager (design-time/legacy constructions).

## 3. i18n

- [x] 3.1 `Add_AddQueue`/`Add_QueueNamePlaceholder`/`Add_QueueConfirm`/`Add_QueueCancel` translated in
  ALL 16 language packs (verified zero drift).

## 4. Tests

- [x] 4.1 `Add_dialog_can_create_and_select_a_new_queue`: queue added to config with the settings-seeded
  cap, picker appears, new queue selected, `QueueId` stamped; empty-name no-op. Suite 216/216.
- [ ] 4.2 **Author re-test:** open Add link → "Add queue" → name → confirm → the picker shows/selects it
  and the added download lands in that queue (visible on the Queues page). Stays in-progress until
  confirmed.

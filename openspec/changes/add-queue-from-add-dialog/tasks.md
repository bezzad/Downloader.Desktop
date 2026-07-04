## 1. ViewModel

- [ ] 1.1 `AddDownloadItemViewModel`: optional `IDownloadManager` ctor param; `AddQueueCommand` +
  inline-name state (`IsAddingQueue`, `NewQueueName`, confirm/cancel); create via `manager.AddQueue`,
  refresh `Queues`/`ShowQueuePicker`, select the new queue. Empty name → no-op.

## 2. View

- [ ] 2.1 "Add queue" button left of the Add button (same accent style); inline name TextBox +
  confirm/cancel row, Enter confirms; hidden when no manager (design-time).

## 3. i18n

- [ ] 3.1 New keys (button, placeholder, tooltip) in ALL 16 language packs.

## 4. Tests

- [ ] 4.1 Creating a queue adds it to config with settings-seeded cap, selects it, and
  `StartDownload` stamps its `QueueId` on the items; empty-name no-op.

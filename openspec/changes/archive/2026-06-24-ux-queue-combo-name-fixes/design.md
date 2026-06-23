# Design: ux-queue-combo-name-fixes

## 1. Live queue menus

**Problem.** `DownloadManager.Queues` is `IReadOnlyList<DownloadQueue>` backed by `_config.Queues` (a plain
`List`). `DownloadsViewModel.StartQueueTargets`/`StopQueueTargets` are computed `=>` properties bound via
`MenuFlyout.ItemsSource`. `AddQueue`/`RemoveQueue` mutate the list but raise no notification, so the bound
menus never re-materialize until the VM is recreated (app restart).

**Decision.** Add a plain `event Action QueuesChanged` to `DownloadManager`, raised at the end of `AddQueue`
and `RemoveQueue`. `DownloadsViewModel` subscribes in its ctor and, on the UI thread, re-raises
`StartQueueTargets`, `StopQueueTargets`, and `ShowQueue`. This is the minimal fix consistent with the existing
event style (the manager already exposes `StatsChanged`/`AllDownloadsCompleted` as plain events).

- Use a plain `event Action`, not `ObservableCollection`, to avoid changing the `Queues` return type and the
  ripple through callers.
- Marshal the re-raise through `Dispatcher.UIThread` defensively (queue edits originate on the UI thread today,
  but the re-raise touching bound properties should be UI-safe regardless).

## 2. ComboBox padding

**Decision.** Extend the existing global `ComboBox` style in `App.axaml` with `Padding="10 6"` (matches the
TextBox/button insets used elsewhere). Add a `ComboBoxItem` padding setter so dropdown rows are inset too.
No per-view overrides — one global style covers Settings, the Add dialog, and any future combo.

## 3. Full name on hover

**Decision.** In `DownloadsView.axaml`, change the Name cell `TextBlock.ToolTip.Tip` from `{Binding ErrorMessage}`
to the full name. Add a read-only VM helper `NameTooltip` on `DownloadItemViewModel` that returns `DisplayName`,
and appends `"\n<error>"` when `ErrorMessage` is non-empty — so the tooltip is useful for both long names and
failures. Bind the cell tooltip to `NameTooltip`. The column keeps `TextTrimming="CharacterEllipsis"`.

## Risks / trade-offs

- Tooltip always present (even for short names) — harmless; Avalonia only shows it on hover and the text equals
  the visible name when not trimmed.
- `QueuesChanged` is fire-and-forget; no unsubscribe needed for the app-lifetime `DownloadsViewModel`, but we
  unsubscribe in line with existing patterns if the VM exposes teardown (it does not today — single root VM).

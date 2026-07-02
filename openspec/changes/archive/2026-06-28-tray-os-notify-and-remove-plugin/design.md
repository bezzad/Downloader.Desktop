# Design: tray-os-notify-and-remove-plugin

## Notification channel decision

`NotificationService.PreferOsChannel(bool appFocused, bool? windowVisible) => !appFocused || windowVisible == false`
is pure and unit-tested. `Notify`/`ShowAction` now branch on `InAppVisible` (= `!PreferOsChannel(AppFocused,
(_topLevel as Window)?.IsVisible)`): in-app only when a window is actually on screen AND focused; otherwise OS
(with the existing in-app fallback when no native channel exists, e.g. Windows). The visibility check is the
real fix — macOS doesn't fire `Deactivated` on Hide, so focus alone was stale. `MainViewModel` additionally
calls `NotificationService.SetFocused(false)` right after `window.Hide()` (close-to-tray and `--minimized`
start) so the event-driven state is also correct (and the actionable-prompt flush-on-focus-return still works).

## Plugin removal

`PluginManager` now keeps the collectible `AssemblyLoadContext` and the source DLL path on each `LoadedPlugin`
(previously the ALC was discarded). `RemovePlugin(id)`:
1. removes the plugin from `_plugins` under the lock (it stops contributing resolvers/providers at once),
2. unloads the collectible ALC,
3. best-effort deletes the DLL + `deps.json` (retry once after a GC for Windows file-lock; if it still can't
   delete, it's left for the next launch and logged).

The Plugins page passes a `RemoveRow` callback into each `PluginRowViewModel`; its `RemoveCommand` calls
`RemovePlugin`, clears any persisted disabled-id, drops the row, and shows an in-app confirmation toast. No
blocking confirm dialog (the repo avoids modal alert dialogs; reinstall is one click).

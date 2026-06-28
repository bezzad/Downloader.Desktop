# Tasks: tray-os-notify-and-remove-plugin

## 1. Tray ⇒ OS notifications
- [x] 1.1 Add pure `PreferOsChannel(focused, windowVisible)` + visibility-aware `InAppVisible`; route `Notify`/`ShowAction` through it.
- [x] 1.2 Set `SetFocused(false)` on close-to-tray and `--minimized` start (macOS doesn't fire Deactivated on Hide).
- [x] 1.3 Tests: channel matrix (incl. focused-but-hidden ⇒ OS) + unknown-visibility fallback.

## 2. Remove plugin
- [x] 2.1 Track the collectible ALC + source DLL path on each loaded plugin.
- [x] 2.2 `PluginManager.RemovePlugin(id)`: drop from registry, unload ALC, delete DLL + deps.json (best-effort).
- [x] 2.3 Row `RemoveCommand` + `PluginsViewModel.RemoveRow` (refresh list + toast); trash button in `PluginsView`; i18n keys.
- [x] 2.4 Test: `RemovePlugin` drops the plugin and its resolver; second remove is a no-op.

## 3. Verify
- [x] 3.1 `dotnet test` green (151) Debug; screenshots unchanged (Plugins section not captured).
- [x] 3.2 Skill note for both patterns; commit + push to `develop`.

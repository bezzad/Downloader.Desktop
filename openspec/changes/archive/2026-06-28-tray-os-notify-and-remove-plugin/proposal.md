# Proposal: tray-os-notify-and-remove-plugin

## Why

1. **Tray notifications wrong on macOS.** When the app is hidden in the system tray it should show **OS**
   notifications, but on macOS it still showed in-app toasts (which are invisible — the window is hidden).
   Channel selection was driven only by focus events (`Activated`/`Deactivated`), and macOS does not fire
   `Deactivated` when a window is hidden to the tray, so the app still thought it was focused.
2. **No way to remove a plugin.** The Plugins list let the user install and enable/disable plugins but never
   uninstall one — a stale or unwanted plugin could only be removed by hand from the plugins folder.

## What Changes

1. **Visibility-aware notification routing.** Notifications use the OS channel whenever the app is not the
   visible, focused foreground — i.e. **unfocused OR hidden to the tray**. A pure `PreferOsChannel(focused,
   windowVisible)` decision now also checks the main window's `IsVisible`, and the close-to-tray / start-minimized
   paths set the unfocused state explicitly (belt-and-suspenders for macOS).
2. **Remove plugin.** Each installed plugin row gets a trash button. Removing a plugin drops it from the
   registry (stops it contributing immediately), unloads its collectible load context, and deletes its DLL
   (+ sidecar `deps.json`) from disk so it doesn't reload next launch; an in-app toast confirms.

## Impact

- Affected specs: `notifications` (tray ⇒ OS), `plugins` (remove/uninstall).
- Affected code: `Services/NotificationService.cs` (decision + visibility), `ViewModels/MainViewModel.cs`
  (set unfocused on hide), `Services/PluginManager.cs` (`RemovePlugin`, track ALC + source path),
  `ViewModels/PluginsViewModel.cs` (+ row `RemoveCommand`), `Views/PluginsView.axaml` (trash button),
  `Assets/i18n/en.json` (two keys).
- Tests: +6 (channel-decision matrix incl. focused-but-hidden, unknown-visibility; `RemovePlugin` drops the
  plugin + its resolver). 151 green.

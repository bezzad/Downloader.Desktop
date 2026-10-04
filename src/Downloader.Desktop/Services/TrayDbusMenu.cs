using System;
using System.Collections.Generic;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Platform;

namespace Downloader.Desktop.Services;

/// <summary>
/// Linux: serves the tray's native menu at <see cref="Path"/> instead of Avalonia's
/// <c>/net/avaloniaui/dbusmenu/&lt;guid&gt;</c>.
/// <para>Why: inside the snap, AppArmor (the <c>unity7</c> interface) lets the shell read a dbusmenu only
/// at <c>/MenuBar…</c> or <c>/com/canonical/…</c>, so the menu at Avalonia's path came up empty. And the
/// GNOME AppIndicator extension sends the app nothing on a single click — it opens the dbusmenu itself and
/// reports only a DOUBLE click (<c>Activate</c>). So an empty menu meant a single click did nothing at all,
/// whatever the app did with its click handler.</para>
/// <para>Avalonia hard-codes that path and exposes no hook, so this reaches its internals by reflection:
/// it creates a second Avalonia menu exporter at <see cref="Path"/> on the tray's own D-Bus connection and
/// points the StatusNotifierItem's <c>Menu</c> property at it, before the item registers with the shell.
/// Anything missing ⇒ nothing changes (the caller keeps Avalonia's menu); <see cref="MissingMembers"/> is
/// pinned by a test so an Avalonia upgrade that renames one fails the build, not the user's tray.</para>
/// </summary>
internal static class TrayDbusMenu
{
    /// <summary>A dbusmenu path the snap's AppArmor profile allows the shell to read.</summary>
    internal const string Path = "/MenuBar";

    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly PropertyInfo ImplProperty = typeof(TrayIcon).GetProperty("Impl", Any);
    private static readonly Assembly FreeDesktop = LoadFreeDesktop();
    private static readonly Type ImplType = FreeDesktop?.GetType("Avalonia.FreeDesktop.DBusTrayIconImpl");
    private static readonly FieldInfo ConnectionField = ImplType?.GetField("_connection", Any);
    private static readonly FieldInfo ItemField = ImplType?.GetField("_statusNotifierItemDbusObj", Any);
    private static readonly PropertyInfo MenuPathProperty = ItemField?.FieldType.GetProperty("Menu", Any);
    private static readonly MethodInfo CreateExporter = FreeDesktop?
        .GetType("Avalonia.FreeDesktop.DBusMenuExporter")?
        .GetMethod("TryCreateDetachedNativeMenu", Any);

    private static Assembly LoadFreeDesktop()
    {
        try { return Assembly.Load("Avalonia.FreeDesktop"); }
        catch { return null; }
    }

    /// <summary>The Avalonia internals this relies on that the running Avalonia does not have (empty = all there).</summary>
    internal static IReadOnlyList<string> MissingMembers()
    {
        var missing = new List<string>();
        if (ImplProperty == null) missing.Add("TrayIcon.Impl");
        if (ImplType == null) missing.Add("DBusTrayIconImpl");
        if (ConnectionField == null) missing.Add("DBusTrayIconImpl._connection");
        if (ItemField == null) missing.Add("DBusTrayIconImpl._statusNotifierItemDbusObj");
        if (MenuPathProperty == null || !MenuPathProperty.CanWrite) missing.Add("StatusNotifierItem.Menu");
        if (CreateExporter == null) missing.Add("DBusMenuExporter.TryCreateDetachedNativeMenu");
        return missing;
    }

    /// <summary>Exports <paramref name="menu"/> at <see cref="Path"/> for <paramref name="tray"/>. Returns the
    /// exporter (dispose it with the tray), or null when this is not a D-Bus tray or anything is missing.</summary>
    internal static IDisposable TryExport(TrayIcon tray, NativeMenu menu) =>
        TryExport(ImplProperty?.GetValue(tray), menu);

    /// <summary>Same, given Avalonia's tray implementation object (testable without a <see cref="TrayIcon"/>).</summary>
    internal static IDisposable TryExport(object trayImpl, NativeMenu menu)
    {
        if (trayImpl == null || trayImpl.GetType() != ImplType || MissingMembers().Count > 0)
            return null;

        var connection = ConnectionField.GetValue(trayImpl);
        var item = ItemField.GetValue(trayImpl);
        if (connection == null || item == null)
            return null; // no session bus: Avalonia has no tray here either

        var exporter = (INativeMenuExporter)CreateExporter.Invoke(null, new[] { Path, connection });
        exporter.SetNativeMenu(menu);
        MenuPathProperty.SetValue(item, Activator.CreateInstance(MenuPathProperty.PropertyType, Path));
        return exporter as IDisposable;
    }

    /// <summary>The menu path the tray item currently advertises (for tests).</summary>
    internal static string AdvertisedPath(object trayImpl) =>
        MenuPathProperty?.GetValue(ItemField?.GetValue(trayImpl))?.ToString();
}

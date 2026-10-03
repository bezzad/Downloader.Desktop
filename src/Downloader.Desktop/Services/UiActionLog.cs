using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Downloader.Desktop.ViewModels;

namespace Downloader.Desktop.Services;

/// <summary>
/// Logs every click on a button, toolbar button, menu or context-menu item, checkbox or switch — in ONE
/// place, through class handlers, so no view has to be edited. Typed text is never read.
/// </summary>
public static class UiActionLog
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered)
            return;
        _registered = true;
        // ToggleButton (CheckBox, ToggleSwitch) is a Button, and raises Click after it has toggled.
        Button.ClickEvent.AddClassHandler<Button>((b, e) => OnClick(b, e), handledEventsToo: true);
        MenuItem.ClickEvent.AddClassHandler<MenuItem>((m, e) => OnClick(m, e), handledEventsToo: true);
    }

    private static void OnClick(Control control, RoutedEventArgs e)
    {
        // A click bubbles through parent buttons/menus; log it once, where it happened.
        if (!AppLog.IsEnabled || !ReferenceEquals(e.Source, control))
            return;
        AppLog.Info(Describe(control));
    }

    /// <summary>The log line for a click on <paramref name="control"/>.</summary>
    internal static string Describe(Control control)
    {
        var line = $"UI: click \"{Label(control)}\" ({control.GetType().Name})";
        if (!string.IsNullOrEmpty(control.Name))
            line += $" #{control.Name}";
        if (control is ToggleButton toggle)
            line += toggle.IsChecked == true ? " → on" : " → off";
        var view = OwnerView(control);
        if (view != null)
            line += $" in {view}";
        var target = Target(control.DataContext);
        if (target != null)
            line += $" on \"{target}\"";
        return line;
    }

    private static string Label(Control control)
    {
        var content = control is MenuItem m ? m.Header : (control as ContentControl)?.Content;
        var text = content as string ?? (content as Control)?.GetVisualDescendants()
            .Prepend((Visual)content).OfType<TextBlock>().Select(t => t.Text)
            .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
        text ??= ToolTip.GetTip(control) as string;
        return string.IsNullOrWhiteSpace(text) ? control.Name ?? "" : text.Trim();
    }

    // The page or window the control belongs to. A context-menu item lives in a popup, so the logical
    // tree (menu → the control it opened on) is tried before the visual one.
    private static string OwnerView(Control control)
    {
        var owner = (Control)control.FindLogicalAncestorOfType<UserControl>()
                    ?? control.FindAncestorOfType<UserControl>()
                    ?? (Control)control.FindLogicalAncestorOfType<Window>()
                    ?? TopLevel.GetTopLevel(control) as Window;
        return owner?.GetType().Name;
    }

    // What the click acted on: a download's display name. URLs are shortened, never logged whole.
    private static string Target(object dataContext)
    {
        var name = dataContext switch
        {
            DownloadItemViewModel d => d.DisplayName,
            QueueItemViewModel q => q.Item?.DisplayName,
            _ => null
        };
        if (string.IsNullOrWhiteSpace(name))
            return null;
        return name.Contains("://") ? LogText.Url(name) : name;
    }
}

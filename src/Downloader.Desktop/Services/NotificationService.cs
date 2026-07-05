using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Downloader.Desktop.Services;

/// <summary>
/// User-facing messages (download complete/failed, updates, plugins, errors). Routing is
/// <b>focus-aware</b>: when the app is focused the message shows as an in-app toast; when the app is
/// unfocused or minimized to the tray it shows as an OS notification (Linux <c>notify-send</c>, macOS
/// in-process banner). Exactly one channel fires per message — no more ambiguous in-app+OS double-fire.
/// In-app toasts carry selectable text and a Copy button so users can grab the text for bug reports.
/// No external dependency — the app stays self-contained.
/// </summary>
public static class NotificationService
{
    /// <summary>Master switch, mirrors the user setting.</summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether any app window is currently active. Drives channel selection (focused ⇒ in-app,
    /// unfocused/tray ⇒ OS). Defaults true so an unknown focus state uses the gentler in-app channel.
    /// </summary>
    public static bool AppFocused { get; private set; } = true;

    private static WindowNotificationManager _inApp;
    private static TopLevel _topLevel;

    /// <summary>
    /// Channel decision: use the OS channel when the app is NOT the visible, focused foreground — i.e. it's
    /// unfocused OR hidden to the tray. Use the in-app toast only when a window is actually on screen and
    /// focused. <paramref name="windowVisible"/> null = unknown ⇒ treat as visible (fall back to focus).
    /// This is why macOS showed in-app toasts from the tray: hiding to the tray there does not reliably fire
    /// the window's Deactivated event, so <see cref="AppFocused"/> stayed true — the visibility check fixes it.
    /// </summary>
    public static bool PreferOsChannel(bool appFocused, bool? windowVisible)
        => !appFocused || windowVisible == false;

    // True only when a window is on screen AND focused; an in-app toast can't be seen otherwise.
    private static bool InAppVisible => !PreferOsChannel(AppFocused, (_topLevel as Window)?.IsVisible);

    // Actionable prompts (e.g. "Update available") raised while unfocused are re-shown in-app on focus-gain
    // so their action is never lost.
    private static readonly List<(string title, string message, Action onClick, string actionText)> _pendingActions = new();
    private static readonly object _gate = new();

    /// <summary>Attach the in-app toast host to the main window (called once at startup).</summary>
    public static void Attach(TopLevel topLevel)
    {
        _topLevel = topLevel;
        if (topLevel != null)
            _inApp = new WindowNotificationManager(topLevel) { MaxItems = 3, Position = NotificationPosition.BottomRight };
    }

    /// <summary>
    /// Called by the shell whenever app activation changes (any window Activated/Deactivated). On focus-gain,
    /// flushes any actionable prompts that were sent to the OS while unfocused, re-showing them in-app.
    /// </summary>
    public static void SetFocused(bool focused)
    {
        AppFocused = focused;
        if (!focused)
            return;

        List<(string title, string message, Action onClick, string actionText)> pending;
        lock (_gate)
        {
            if (_pendingActions.Count == 0)
                return;
            pending = new List<(string, string, Action, string)>(_pendingActions);
            _pendingActions.Clear();
        }
        foreach (var (t, m, a, txt) in pending)
            ShowActionInApp(t, m, a, txt);
    }

    public static void NotifyCompleted(string fileName) =>
        Notify("Download complete", string.IsNullOrWhiteSpace(fileName) ? "A download finished." : $"{fileName} finished.", false);

    public static void NotifyFailed(string fileName, string reason) =>
        Notify("Download failed", $"{(string.IsNullOrWhiteSpace(fileName) ? "A download" : fileName)} failed. {reason}".Trim(), true);

    public static void NotifyAllCompleted(int count) =>
        Notify("All downloads complete", count > 0 ? $"All {count} downloads finished." : "All downloads finished.", false);

    /// <summary>Passive message, routed by focus to exactly one channel.</summary>
    public static void Notify(string title, string message, bool isError)
    {
        if (!Enabled)
            return;

        if (InAppVisible)
        {
            ShowInApp(title, message, isError);
            return;
        }

        // Unfocused/tray: prefer the OS. If no native channel exists (e.g. Windows), fall back to in-app.
        if (!TryNative(title, message, isError))
            ShowInApp(title, message, isError);
    }

    /// <summary>
    /// Actionable prompt the user can act on (e.g. "Update available — click to install"). Ignores the
    /// on/off switch (it's a prompt, not a passive alert). Focused ⇒ in-app actionable toast now; unfocused
    /// ⇒ a plain OS notification now, then the actionable toast is re-shown in-app when focus returns.
    /// </summary>
    public static void ShowAction(string title, string message, Action onClick, string actionText = null)
    {
        if (InAppVisible)
        {
            ShowActionInApp(title, message, onClick, actionText);
            return;
        }

        TryNative(title, message, false);
        lock (_gate)
            _pendingActions.Add((title, message, onClick, actionText));
    }

    /// <summary>Always-shown in-app toast for direct feedback to an action the user just performed
    /// (e.g. "Plugin installed"), independent of focus and the on/off switch.</summary>
    public static void Inform(string title, string message, bool isError) => ShowInApp(title, message, isError);

    private static void ShowActionInApp(string title, string message, Action onClick, string actionText = null)
    {
        // The button carries the ACTION'S name (e.g. "Add to Ollama") — a bare "Open" reads as
        // open/unzip the file and confused the author on the Ollama flow.
        void Show() => _inApp?.Show(
            BuildToast(title, message, NotificationType.Information, onClick, actionText ?? "Open"),
            NotificationType.Information, TimeSpan.FromSeconds(20));

        Dispatch(Show);
    }

    private static void ShowInApp(string title, string message, bool isError)
    {
        var type = isError ? NotificationType.Error : NotificationType.Success;
        void Show() => _inApp?.Show(BuildToast(title, message, type), type);
        Dispatch(Show);
    }

    /// <summary>Builds the in-app toast content: severity stripe, bold selectable title, selectable
    /// message, a Copy button (title+message → clipboard), and an optional action button.</summary>
    private static Control BuildToast(string title, string message, NotificationType type, Action action = null, string actionText = null)
    {
        var accent = type switch
        {
            NotificationType.Error => Color.Parse("#E5484D"),
            NotificationType.Success => Color.Parse("#2E9E6B"),
            _ => Color.Parse("#2F7DD1"),
        };

        var titleBlock = new SelectableTextBlock
        {
            Text = title,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };

        var copyBtn = new Button
        {
            Content = "Copy",
            FontSize = 11,
            Padding = new Thickness(8, 2, 8, 2),
            Cursor = new Cursor(StandardCursorType.Hand),
            VerticalAlignment = VerticalAlignment.Top
        };
        copyBtn.Click += (_, _) => _topLevel?.Clipboard?.SetTextAsync($"{title}: {message}");

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(titleBlock, 0);
        Grid.SetColumn(copyBtn, 1);
        header.Children.Add(titleBlock);
        header.Children.Add(copyBtn);

        var stack = new StackPanel { Spacing = 4, MinWidth = 240 };
        stack.Children.Add(header);
        stack.Children.Add(new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Opacity = 0.9 });

        if (action != null)
        {
            var actBtn = new Button
            {
                Content = actionText ?? "Open",
                Classes = { "accent" },
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 6, 0, 0),
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            actBtn.Click += (_, _) => action();
            stack.Children.Add(actBtn);
        }

        return new Border
        {
            BorderBrush = new SolidColorBrush(accent),
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(10, 4, 4, 4),
            Child = stack
        };
    }

    private static void Dispatch(Action show)
    {
        if (Dispatcher.UIThread.CheckAccess())
            show();
        else
            Dispatcher.UIThread.Post(show);
    }

    private static bool TryNative(string title, string message, bool isError)
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                // Standard freedesktop icon names — themed by the desktop environment. Success uses the
                // green "checkmark" emblem (not the blue info icon); failures use the red error icon.
                var icon = isError ? "dialog-error" : "emblem-default";
                Run("notify-send", new[] { "-i", icon, "Downloader", $"{title}: {message}" });
                return true;
            }

            if (OperatingSystem.IsMacOS())
            {
                // Post in-process so the banner shows the app's own icon. (osascript "display
                // notification" always shows Script Editor's generic script icon and can't be
                // overridden.) Falls through to the in-app toast if the native call fails.
                return MacNotifier.TryNotify("Downloader", title, message);
            }
        }
        catch
        {
            // fall through to the in-app toast
        }

        return false; // Windows + fallbacks use the in-app toast
    }

    private static void Run(string file, string[] args)
    {
        var psi = new ProcessStartInfo { FileName = file, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        Process.Start(psi);
    }
}

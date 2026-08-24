using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Downloader.Desktop.Models;
using Downloader.Desktop.ViewModels;

namespace Downloader.Desktop.Services;

/// <summary>
/// Shows the overall download progress on the OS taskbar/dock where the platform supports it (#4):
/// Windows via ITaskbarList3 (the green fill on the taskbar button), Linux via the Unity
/// LauncherEntry DBus signal (honored by KDE/Unity/Dash-to-Dock; a silent no-op elsewhere).
/// macOS has no reachable Dock-progress API from the plain net10.0 build — documented skip.
/// Everything is best-effort: taskbar decoration must never break a download.
/// </summary>
public static class TaskbarProgressService
{
    /// <summary>Aggregate for the taskbar: mean progress (0..1) of the RUNNING downloads; hidden when
    /// none are running (a finished batch must not leave a stale full bar on the icon).</summary>
    public static (bool Visible, double Fraction) Aggregate(IEnumerable<DownloadItemViewModel> items)
    {
        var running = items?.Where(i => i.Status == DownloadStatus.Running).ToList();
        if (running == null || running.Count == 0)
            return (false, 0d);
        var fraction = Math.Clamp(running.Average(i => i.Progress) / 100.0, 0, 1);
        return (true, fraction);
    }

    private static bool _lastVisible;
    private static int _lastPermille = -1;

    /// <summary>Applies the given progress to the window's taskbar button/dock icon. Cheap to call on
    /// every stats tick — it skips the platform call when nothing changed.</summary>
    public static void Update(Window window, bool visible, double fraction)
    {
        var permille = visible ? (int)(fraction * 1000) : 0;
        if (visible == _lastVisible && permille == _lastPermille)
            return;
        _lastVisible = visible;
        _lastPermille = permille;

        try
        {
            if (OperatingSystem.IsWindows())
                WindowsTaskbar.Set(window, visible, fraction);
            else if (OperatingSystem.IsLinux())
                UnityLauncher.Set(visible, fraction);
            // macOS: no supported Dock progress path from the portable build — needs the native
            // AppKit bundle; verified skip (see the change's design notes).
        }
        catch
        {
            // never let taskbar decoration break the app
        }
    }

    // ---- Windows: ITaskbarList3 (COM) ----

    private static class WindowsTaskbar
    {
        private static object _taskbar; // ITaskbarList3, created lazily on first use

        public static void Set(Window window, bool visible, double fraction)
        {
            var hwnd = window?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (hwnd == IntPtr.Zero)
                return;

            if (_taskbar == null)
            {
                var taskbar = (ITaskbarList3)new TaskbarListCom();
                taskbar.HrInit();
                _taskbar = taskbar;
            }

            var t = (ITaskbarList3)_taskbar;
            if (!visible)
            {
                t.SetProgressState(hwnd, TbpFlag.NoProgress);
                return;
            }
            t.SetProgressState(hwnd, TbpFlag.Normal);
            t.SetProgressValue(hwnd, (ulong)Math.Max(1, fraction * 1000), 1000);
        }

        [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090"), ClassInterface(ClassInterfaceType.None)]
        private class TaskbarListCom { }

        private enum TbpFlag
        {
            NoProgress = 0,
            Indeterminate = 1,
            Normal = 2,
            Error = 4,
            Paused = 8
        }

        [ComImport, Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ITaskbarList3
        {
            // ITaskbarList
            void HrInit();
            void AddTab(IntPtr hwnd);
            void DeleteTab(IntPtr hwnd);
            void ActivateTab(IntPtr hwnd);
            void SetActiveAlt(IntPtr hwnd);
            // ITaskbarList2
            void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
            // ITaskbarList3 (only the two members we use need exact vtable order up to here)
            void SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
            void SetProgressState(IntPtr hwnd, TbpFlag state);
        }
    }

    // ---- Linux: com.canonical.Unity.LauncherEntry Update signal over the session bus ----

    private static class UnityLauncher
    {
        private static Tmds.DBus.Protocol.DBusConnection _connection;

        public static void Set(bool visible, double fraction)
        {
            _connection ??= Tmds.DBus.Protocol.DBusConnection.Session;

            using var writer = _connection.GetMessageWriter();
            writer.WriteSignalHeader(
                path: "/com/canonical/unity/launcherentry/1",
                @interface: "com.canonical.Unity.LauncherEntry",
                signature: "sa{sv}",
                member: "Update");
            // The URI must match the installed .desktop file (scripts/install.sh → downloader.desktop).
            writer.WriteString("application://downloader.desktop");
            var dict = writer.WriteDictionaryStart();
            writer.WriteDictionaryEntryStart();
            writer.WriteString("progress");
            writer.WriteVariantDouble(fraction);
            writer.WriteDictionaryEntryStart();
            writer.WriteString("progress-visible");
            writer.WriteVariantBool(visible);
            writer.WriteDictionaryEnd(dict);
            _connection.TrySendMessage(writer.CreateMessage());
        }
    }
}

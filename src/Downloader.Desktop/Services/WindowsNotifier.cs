using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Downloader.Desktop.Services;

/// <summary>
/// Posts a native Windows notification <b>in-process</b> via the shell notification API
/// (<c>Shell_NotifyIconW</c> + <c>NIF_INFO</c>), which Windows 10/11 surface as a real toast and keep
/// in the Action Center. Mirrors <see cref="MacNotifier"/>: a small, dependency-free native call — no
/// NuGet package, no <c>net*-windows</c> TFM, and above all <b>no child process</b>.
///
/// <para><b>Why in-process matters (issue #4).</b> This used to spawn
/// <c>powershell.exe -EncodedCommand …</c> to reach the WinRT toast API. An unsigned binary spawning
/// hidden, base64-encoded PowerShell is the exact parent→child chain that behavioral antivirus engines
/// score as malicious: Bitdefender's Advanced Threat Defense blocked and quarantined the app for
/// <c>Downloader.exe → powershell.exe → conhost.exe</c>. Nothing the script did was unsafe — the
/// <i>shape</i> of the action was. A direct API call has no such shape.
/// <c>NoShellSpawnTests</c> fails the build if a shell spawn ever comes back.</para>
///
/// <para>A hidden message-only window owns the notification icon: the shell needs an HWND + icon id to
/// hang a balloon on. No message pump is required because we never ask for click callbacks. Everything
/// is best-effort — any failure returns false and the notification is simply skipped, the same contract
/// the other native branches have.</para>
///
/// <para>Not verifiable on this dev box or in CI (no Windows runner) — needs a manual check on
/// Windows.</para>
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsNotifier
{
    public static bool TryNotify(string appName, string title, string message)
        => TryNotify(appName, title, message, isError: false);

    public static bool TryNotify(string appName, string title, string message, bool isError)
    {
        try
        {
            lock (Gate)
            {
                var hwnd = EnsureWindow();
                if (hwnd == IntPtr.Zero)
                    return false;

                var data = new NOTIFYICONDATAW
                {
                    cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
                    hWnd = hwnd,
                    uID = IconId,
                    uFlags = NIF_ICON | NIF_TIP | NIF_INFO,
                    hIcon = EnsureIcon(),
                    szTip = Clamp(appName ?? AppFallback, TipMax),
                    szInfoTitle = Clamp(title ?? string.Empty, InfoTitleMax),
                    szInfo = Clamp(message ?? string.Empty, InfoMax),
                    dwInfoFlags = isError ? NIIF_ERROR : NIIF_INFO,
                };

                // First call adds the icon, later ones just retarget the balloon at it. If the icon was
                // lost (explorer restart), NIM_MODIFY fails — fall back to adding it again.
                if (!_iconAdded)
                {
                    _iconAdded = Shell_NotifyIconW(NIM_ADD, ref data);
                    return _iconAdded;
                }

                if (Shell_NotifyIconW(NIM_MODIFY, ref data))
                    return true;

                _iconAdded = Shell_NotifyIconW(NIM_ADD, ref data);
                return _iconAdded;
            }
        }
        catch
        {
            return false;
        }
    }

    // ---- state ----

    private const string AppFallback = "Downloader";
    private const uint IconId = 1;

    private static readonly object Gate = new();
    private static IntPtr _hwnd;
    private static IntPtr _icon;
    private static bool _iconAdded;
    private static bool _classRegistered;

    // The delegate handed to the window class must outlive the window, or the shell calls into freed
    // memory the first time it dispatches anything to us.
    private static WndProcDelegate _wndProc;

    private static IntPtr EnsureWindow()
    {
        if (_hwnd != IntPtr.Zero)
            return _hwnd;

        const string className = "DownloaderNotifySink";
        if (!_classRegistered)
        {
            _wndProc = DefWindowProcW;
            var cls = new WNDCLASSW
            {
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                lpszClassName = className,
            };
            // 0 also means "already registered", which is fine — we only ever use one class name.
            RegisterClassW(ref cls);
            _classRegistered = true;
        }

        // HWND_MESSAGE: a message-only window. Never shown, never in the taskbar, no paint cost.
        _hwnd = CreateWindowExW(0, className, AppFallback, 0, 0, 0, 0, 0,
            HwndMessage, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        return _hwnd;
    }

    private static IntPtr EnsureIcon()
    {
        if (_icon != IntPtr.Zero)
            return _icon;

        // Our own exe icon, so the notification is attributed to this app and not to a generic host.
        var exe = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exe) && ExtractIconExW(exe, 0, out _, out var small, 1) > 0 && small != IntPtr.Zero)
            return _icon = small;

        return _icon = LoadIconW(IntPtr.Zero, IdiApplication);
    }

    /// <summary>Truncate to what the fixed-size shell buffers hold (including their NUL), so marshalling
    /// a long title/message can never overrun or throw.</summary>
    internal static string Clamp(string value, int max)
        => string.IsNullOrEmpty(value) ? string.Empty
            : value.Length <= max - 1 ? value
            : value[..(max - 1)];

    // ---- interop ----

    private const int TipMax = 128, InfoMax = 256, InfoTitleMax = 64;
    private const uint NIM_ADD = 0x0, NIM_MODIFY = 0x1;
    private const uint NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_INFO = 0x10;
    private const uint NIIF_INFO = 0x1, NIIF_ERROR = 0x3;
    private const int IdiApplication = 32512;
    private static readonly IntPtr HwndMessage = new(-3);

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = TipMax)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = InfoMax)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = InfoTitleMax)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSW
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconExW(string lpszFile, int nIconIndex, out IntPtr phiconLarge,
        out IntPtr phiconSmall, uint nIcons);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassW(ref WNDCLASSW lpWndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(uint dwExStyle, string lpClassName, string lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu,
        IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadIconW(IntPtr hInstance, int lpIconName);
}

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Downloader.Desktop.Services;

/// <summary>
/// Posts a native macOS notification from inside the app process via the Objective-C runtime
/// (NSUserNotification). Posting in-process is what makes the notification show the app bundle's
/// own icon (downloader.icns / bundle id com.bezzad.downloader). The old approach shelled out to
/// <c>osascript "display notification"</c>, which macOS always attributes to Script Editor — so the
/// banner showed a generic script icon instead of ours, with no way to override it.
///
/// Implemented with P/Invoke into <c>/usr/lib/libobjc.dylib</c> so it needs no extra NuGet package
/// and no <c>net*-macos</c> workload (the CI release builds plain net10.0). It is AOT/trim-safe.
/// NSUserNotification is deprecated by Apple but still functions on current macOS and is the only
/// dependency-free option; on any failure the caller falls back to the in-app toast.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacNotifier
{
    private const string Objc = "/usr/lib/libobjc.dylib";

    [DllImport(Objc)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(Objc)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(Objc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport(Objc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg1);

    /// <summary>
    /// Delivers a notification. Returns false if the native call could not be made, so the caller
    /// can fall back to the in-app toast.
    /// </summary>
    public static bool TryNotify(string title, string subtitle, string message)
    {
        try
        {
            IntPtr nuClass = objc_getClass("NSUserNotification");
            IntPtr centerClass = objc_getClass("NSUserNotificationCenter");
            if (nuClass == IntPtr.Zero || centerClass == IntPtr.Zero)
                return false; // NSUserNotification unavailable on this OS — fall back

            IntPtr notification = objc_msgSend(objc_msgSend(nuClass, Sel("alloc")), Sel("init"));
            if (notification == IntPtr.Zero)
                return false;

            objc_msgSend(notification, Sel("setTitle:"), NsString(title));
            objc_msgSend(notification, Sel("setSubtitle:"), NsString(subtitle));
            objc_msgSend(notification, Sel("setInformativeText:"), NsString(message));

            IntPtr center = objc_msgSend(centerClass, Sel("defaultUserNotificationCenter"));
            if (center == IntPtr.Zero)
                return false;

            objc_msgSend(center, Sel("deliverNotification:"), notification);

            // The center retains the notification, so release our +1 from alloc to avoid a leak.
            objc_msgSend(notification, Sel("release"));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static IntPtr Sel(string name) => sel_registerName(name);

    /// <summary>Creates an autoreleased NSString from a UTF-8 C string.</summary>
    private static IntPtr NsString(string value)
    {
        IntPtr cls = objc_getClass("NSString");
        IntPtr sel = sel_registerName("stringWithUTF8String:");
        byte[] utf8 = Encoding.UTF8.GetBytes((value ?? string.Empty) + "\0");
        IntPtr buffer = Marshal.AllocHGlobal(utf8.Length);
        try
        {
            Marshal.Copy(utf8, 0, buffer, utf8.Length);
            return objc_msgSend(cls, sel, buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}

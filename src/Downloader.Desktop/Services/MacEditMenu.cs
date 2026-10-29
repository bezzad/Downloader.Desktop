using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Downloader.Desktop.Services;

/// <summary>
/// Adds a standard Edit menu (Cut / Copy / Paste / Select All) to the macOS menu bar.
///
/// The system file and folder pickers (NSOpenPanel / NSSavePanel, including their "New Folder" name
/// box) are Cocoa windows, not Avalonia ones. In Cocoa, Cmd+X/C/V/A in a text field work only through
/// the key equivalents of the main menu's Edit items, which send <c>cut:</c>/<c>copy:</c>/<c>paste:</c>/
/// <c>selectAll:</c> to the focused field. Avalonia creates no Edit menu, so paste did nothing there.
/// Our own Avalonia text boxes are not affected: they handle these keys themselves, and the items are
/// disabled (so they do not take the key) while an Avalonia window has focus.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacEditMenu
{
    private const string Objc = "/usr/lib/libobjc.dylib";
    private const string Title = "Edit";

    /// <summary>The menu items: title, Cocoa action selector, key (used with Cmd).</summary>
    internal static readonly (string Title, string Action, string Key)[] Items =
    [
        ("Cut", "cut:", "x"),
        ("Copy", "copy:", "c"),
        ("Paste", "paste:", "v"),
        ("Select All", "selectAll:", "a"),
    ];

    [DllImport(Objc)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(Objc)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(Objc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport(Objc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg1);

    [DllImport(Objc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2);

    [DllImport(Objc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2, IntPtr arg3);

    /// <summary>
    /// Adds the Edit menu once. Safe to call again (it is skipped when already there). Must run on the
    /// UI (main) thread. On any failure nothing changes.
    /// </summary>
    public static void Ensure()
    {
        try
        {
            IntPtr app = objc_msgSend(objc_getClass("NSApplication"), Sel("sharedApplication"));
            IntPtr mainMenu = app == IntPtr.Zero ? IntPtr.Zero : objc_msgSend(app, Sel("mainMenu"));
            if (mainMenu == IntPtr.Zero)
                return;
            if (objc_msgSend(mainMenu, Sel("itemWithTitle:"), MacNotifier.NsString(Title)) != IntPtr.Zero)
                return; // already added

            IntPtr menu = objc_msgSend(objc_msgSend(objc_getClass("NSMenu"), Sel("alloc")),
                Sel("initWithTitle:"), MacNotifier.NsString(Title));
            foreach (var (title, action, key) in Items)
            {
                // No target: Cocoa sends the action to whatever has focus (the picker's text field).
                objc_msgSend(menu, Sel("addItemWithTitle:action:keyEquivalent:"),
                    MacNotifier.NsString(title), Sel(action), MacNotifier.NsString(key));
            }

            IntPtr editItem = objc_msgSend(mainMenu, Sel("addItemWithTitle:action:keyEquivalent:"),
                MacNotifier.NsString(Title), IntPtr.Zero, MacNotifier.NsString(string.Empty));
            objc_msgSend(mainMenu, Sel("setSubmenu:forItem:"), menu, editItem);

            // The main menu retains the submenu, so release our +1 from alloc.
            objc_msgSend(menu, Sel("release"));
        }
        catch
        {
            // Not fatal: only paste in the system pickers is missing.
        }
    }

    private static IntPtr Sel(string name) => sel_registerName(name);
}

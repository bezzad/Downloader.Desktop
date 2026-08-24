using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Downloader.Desktop.Services;

/// <summary>
/// Windows: self-register a Start-menu shortcut on first run. winget's zip/portable install puts the
/// exe on PATH but creates NO Start-menu entry — users reported "installed successfully but I can't
/// find it anywhere". Idempotent (skips when the shortcut exists), best-effort, per-user (no admin).
/// Removed by deleting %APPDATA%\Microsoft\Windows\Start Menu\Programs\Downloader.lnk.
///
/// <para><b>Written in-process via the shell's IShellLink COM object (issue #4).</b> This used to shell
/// out to create the .lnk, which meant an unsigned binary spawning a script host and then writing a
/// Start-menu entry — a parent→child chain plus a persistence-shaped write, which is what behavioral
/// antivirus engines score. The COM call does the same job with no child process at all.
/// <c>NoShellSpawnTests</c> fails the build if a shell spawn comes back.</para>
/// </summary>
public static class StartMenuShortcut
{
    private const string Description = "Downloader — fast multi-connection download manager";

    public static void EnsureOnWindows()
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe))
                return;
            var programs = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft", "Windows", "Start Menu", "Programs");
            var lnk = Path.Combine(programs, "Downloader.lnk");
            if (File.Exists(lnk))
                return;
            Directory.CreateDirectory(programs);

            Create(lnk, exe);
        }
        catch
        {
            // a missing shortcut must never break startup
        }
    }

    /// <summary>Writes the .lnk through the shell's own shortcut object. Windows-only by construction —
    /// the COM class doesn't exist elsewhere, hence the platform guard in <see cref="EnsureOnWindows"/>.
    /// The attribute states that for the analyzer, which cannot see the guard across the call.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void Create(string lnkPath, string exePath)
    {
        var type = Type.GetTypeFromCLSID(ShellLinkClsid)
                   ?? throw new PlatformNotSupportedException("ShellLink COM class unavailable.");
        var instance = Activator.CreateInstance(type);
        try
        {
            var link = (IShellLinkW)instance;
            link.SetPath(exePath);
            link.SetWorkingDirectory(ResolveWorkingDirectory(exePath));
            link.SetDescription(Description);
            ((IPersistFile)instance).Save(lnkPath, true);
        }
        finally
        {
            if (instance is not null && Marshal.IsComObject(instance))
                Marshal.ReleaseComObject(instance);
        }
    }

    /// <summary>The shortcut's "Start in" directory. Pure — unit-tested. Split on '\' explicitly so the
    /// helper behaves the same when the tests run it on Linux (Path.GetDirectoryName doesn't parse
    /// Windows paths there).</summary>
    internal static string ResolveWorkingDirectory(string exePath)
    {
        var cut = exePath.LastIndexOf('\\');
        return cut > 0 ? exePath[..cut] : exePath;
    }

    // ---- interop ----

    private static readonly Guid ShellLinkClsid = new("00021401-0000-0000-C000-000000000046");

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        // Declaration order IS the vtable order — do not reorder or drop members, even unused ones.
        void GetPath([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }
}

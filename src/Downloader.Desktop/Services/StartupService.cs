using System;
using System.IO;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Downloader.Desktop.Services;

/// <summary>
/// Enables/disables launching the app when the OS starts, hidden to the system tray
/// (passes <c>--minimized</c>). Cross-platform, no extra dependencies:
/// Windows = HKCU Run key (via the in-process registry API), Linux = XDG autostart .desktop,
/// macOS = LaunchAgent plist. All operations are best-effort and never throw.
///
/// <para>The Windows branch writes the Run key <b>in-process</b> rather than spawning <c>reg.exe</c>
/// (issue #4). Writing an autostart key is inherently persistence-shaped, so behavioral antivirus
/// engines weigh it; doing it through a spawned command-line tool adds a parent→child chain on top and
/// pushes the app over the threshold. Same registry write, no child process.</para>
/// </summary>
public static class StartupService
{
    private const string AppName = "Downloader";
    private const string StartupArg = "--minimized";

    /// <summary>The path to the running executable (apphost), used as the autostart command.</summary>
    private static string ExePath => Environment.ProcessPath;

    public static void Apply(bool enabled)
    {
        try
        {
            if (enabled) Enable();
            else Disable();
        }
        catch
        {
            // Autostart is a convenience; never let it break the app.
        }
    }

    private static void Enable()
    {
        var exe = ExePath;
        if (string.IsNullOrWhiteSpace(exe))
            return;

        if (OperatingSystem.IsWindows())
            WriteRunKey($"\"{exe}\" {StartupArg}");
        else if (OperatingSystem.IsMacOS())
            File.WriteAllText(MacPlistPath, MacPlist(exe));
        else
            File.WriteAllText(LinuxDesktopPath, LinuxDesktop(exe));
    }

    private static void Disable()
    {
        if (OperatingSystem.IsWindows())
            WriteRunKey(null);
        else if (OperatingSystem.IsMacOS())
            Delete(MacPlistPath);
        else
            Delete(LinuxDesktopPath);
    }

    public static bool IsEnabled()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return ReadRunKey() is not null;

            return File.Exists(OperatingSystem.IsMacOS() ? MacPlistPath : LinuxDesktopPath);
        }
        catch
        {
            return false;
        }
    }

    // ---- Windows ----
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Writes (or, with a null command, removes) our HKCU Run entry. In-process — no child process.</summary>
    [SupportedOSPlatform("windows")]
    private static void WriteRunKey(string command)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (key is null)
            return;
        if (command is null)
            key.DeleteValue(AppName, throwOnMissingValue: false);
        else
            key.SetValue(AppName, command, RegistryValueKind.String);
    }

    [SupportedOSPlatform("windows")]
    private static string ReadRunKey()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(AppName) as string;
    }

    // ---- Linux ----
    private static string LinuxDesktopPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "autostart", "downloader.desktop");

    private static string LinuxDesktop(string exe)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LinuxDesktopPath)!);
        return "[Desktop Entry]\n" +
               "Type=Application\n" +
               "Name=Downloader\n" +
               $"Exec=\"{exe}\" {StartupArg}\n" +
               "Icon=downloader\n" +
               "Terminal=false\n" +
               "X-GNOME-Autostart-enabled=true\n";
    }

    // ---- macOS ----
    private static string MacPlistPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "LaunchAgents", "com.bezzad.downloader.plist");

    private static string MacPlist(string exe)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MacPlistPath)!);
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
               "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n" +
               "<plist version=\"1.0\"><dict>\n" +
               "  <key>Label</key><string>com.bezzad.downloader</string>\n" +
               $"  <key>ProgramArguments</key><array><string>{exe}</string><string>{StartupArg}</string></array>\n" +
               "  <key>RunAtLoad</key><true/>\n" +
               "</dict></plist>\n";
    }

    private static void Delete(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}

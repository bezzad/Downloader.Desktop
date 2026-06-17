using System;
using System.Diagnostics;
using System.IO;

namespace Downloader.Desktop.Services;

/// <summary>
/// Enables/disables launching the app when the OS starts, hidden to the system tray
/// (passes <c>--minimized</c>). Cross-platform, no extra dependencies:
/// Windows = HKCU Run key (via <c>reg.exe</c>), Linux = XDG autostart .desktop, macOS = LaunchAgent plist.
/// All operations are best-effort and never throw.
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
            RunReg(new[] { "add", RegKey, "/v", AppName, "/t", "REG_SZ", "/d", $"\"{exe}\" {StartupArg}", "/f" });
        else if (OperatingSystem.IsMacOS())
            File.WriteAllText(MacPlistPath, MacPlist(exe));
        else
            File.WriteAllText(LinuxDesktopPath, LinuxDesktop(exe));
    }

    private static void Disable()
    {
        if (OperatingSystem.IsWindows())
            RunReg(new[] { "delete", RegKey, "/v", AppName, "/f" });
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
            {
                var psi = new ProcessStartInfo("reg") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
                foreach (var a in new[] { "query", RegKey, "/v", AppName }) psi.ArgumentList.Add(a);
                using var p = Process.Start(psi);
                var output = p?.StandardOutput.ReadToEnd() ?? string.Empty;
                p?.WaitForExit(3000);
                return output.Contains(AppName, StringComparison.OrdinalIgnoreCase);
            }

            return File.Exists(OperatingSystem.IsMacOS() ? MacPlistPath : LinuxDesktopPath);
        }
        catch
        {
            return false;
        }
    }

    // ---- Windows ----
    private const string RegKey = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run";

    private static void RunReg(string[] args)
    {
        var psi = new ProcessStartInfo("reg") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi);
        p?.WaitForExit(3000);
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

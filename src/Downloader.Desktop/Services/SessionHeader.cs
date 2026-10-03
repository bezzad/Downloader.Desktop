using System.Runtime.InteropServices;
using Downloader.Desktop.Models;

namespace Downloader.Desktop.Services;

/// <summary>The lines written at the top of each logging session.</summary>
public static class SessionHeader
{
    public static string Build(Config config)
    {
        var s = config?.Settings;
        return $"""
                App version: {UpdateService.CurrentVersion}
                OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}, process {RuntimeInformation.ProcessArchitecture})
                Runtime: {RuntimeInformation.FrameworkDescription}
                Language: {s?.Language}
                Theme: {config?.ThemeMode}, accent {s?.AccentColor}
                Settings: {SettingsDiff.Describe(s)}
                """;
    }
}

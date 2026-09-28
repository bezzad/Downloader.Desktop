using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Downloader.Desktop.Models;

namespace Downloader.Desktop.Services;

/// <summary>
/// Turns settings into log lines: what changed between two snapshots (<c>name: old → new</c>) and a
/// full listing for the session header. Secret-like values are masked.
/// </summary>
public static class SettingsDiff
{
    private static readonly PropertyInfo[] Props = typeof(DownloadSettings)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
        .ToArray();

    private static readonly Regex SecretName = new("password|secret|token|cookie|key", RegexOptions.IgnoreCase);

    /// <summary>A detached copy, so later edits to the live settings do not change it.</summary>
    public static DownloadSettings Snapshot(DownloadSettings settings) => settings == null
        ? null
        : JsonSerializer.Deserialize<DownloadSettings>(JsonSerializer.Serialize(settings));

    /// <summary>One <c>name: old → new</c> line per changed setting.</summary>
    public static List<string> Compute(DownloadSettings before, DownloadSettings after)
    {
        var lines = new List<string>();
        if (before == null || after == null)
            return lines;
        foreach (var p in Props)
        {
            var oldText = Show(p, p.GetValue(before));
            var newText = Show(p, p.GetValue(after));
            if (oldText != newText)
                lines.Add($"{p.Name}: {oldText} → {newText}");
        }
        return lines;
    }

    /// <summary>Every setting as <c>name=value</c>, masked — for the session header.</summary>
    public static string Describe(DownloadSettings settings) => settings == null
        ? ""
        : string.Join(", ", Props.Select(p => $"{p.Name}={Show(p, p.GetValue(settings))}"));

    private static string Show(PropertyInfo p, object value)
    {
        if (value == null)
            return "(none)";
        if (SecretName.IsMatch(p.Name))
            return "***";
        var text = value switch
        {
            string s => s,
            System.Collections.IEnumerable => JsonSerializer.Serialize(value),
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
        };
        return p.Name == nameof(DownloadSettings.ProxyAddress) ? LogText.MaskUserInfo(text) : text;
    }
}

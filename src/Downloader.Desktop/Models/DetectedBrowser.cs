using Downloader.Desktop.Services;

namespace Downloader.Desktop.Models;

/// <summary>Which install steps and which limitations apply to a browser.</summary>
public enum BrowserFamily
{
    /// <summary>Chrome, Edge, Brave, Vivaldi, Opera, Chromium — "Load unpacked" behind Developer mode.</summary>
    Chromium,

    /// <summary>Firefox, LibreWolf — a manually loaded add-on is TEMPORARY (gone on browser restart).</summary>
    Gecko,
}

/// <summary>
/// A browser found on this machine by <see cref="BrowserDetector"/>. Carries only what the install flow
/// needs: what to call it, which steps apply, and the executable to launch for a store listing.
/// <b>Deliberately nothing about the user's profile</b> — see <see cref="BrowserDetector"/>.
/// </summary>
public sealed class DetectedBrowser
{
    /// <summary>Stable key (e.g. <c>chrome</c>, <c>firefox</c>) — also the catalog target lookup key.</summary>
    public string Id { get; init; }

    /// <summary>Display name, e.g. "Google Chrome".</summary>
    public string Name { get; init; }

    public BrowserFamily Family { get; init; }

    /// <summary>Absolute path to the browser's executable. Launched by absolute path, never a bare name
    /// PATH could hijack (CLAUDE.md, issue #4).</summary>
    public string ExecutablePath { get; init; }
}

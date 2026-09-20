using System.Collections.Generic;

namespace Downloader.Desktop.Models;

/// <summary>
/// A file-type category: the unit the downloads sidebar lists, the Type column shows, and the list
/// filters by. Built-in categories ship with the app; the user can add their own, and can rename,
/// recolor, re-icon, reorder and re-scope any of them.
/// </summary>
/// <remarks>
/// Deliberately free of file-system paths and platform-specific values so the whole list can be
/// exported on one operating system and imported on another (see
/// <see cref="Services.SettingsPortability"/>).
/// </remarks>
public class DownloadCategory
{
    /// <summary>The identifier for "Other" — the category a file that nothing else claims falls to.</summary>
    public const string OtherId = "other";

    /// <summary>Stable identifier. Referenced by <see cref="DownloadItem.CategoryId"/>, so it must
    /// never change once the category exists.</summary>
    public string Id { get; set; }

    /// <summary>The name as the user typed it. Stored verbatim and <b>never translated</b> — a user
    /// running the app in English may well name their categories in another script. Built-in
    /// categories are named in the app's language at the moment they are created and keep those
    /// names afterwards.</summary>
    public string Name { get; set; }

    /// <summary>Key of one of the icons the app ships with (see
    /// <see cref="Converters.FileKindToIconConverter"/>). An unrecognized key renders as the default
    /// icon and is <b>preserved</b>, so a list exported by a newer app survives a round trip.</summary>
    public string Icon { get; set; }

    /// <summary>The icon's color as <c>#RRGGBB</c>.</summary>
    public string Color { get; set; }

    /// <summary>File extensions this category claims, without the leading dot, lowercase.</summary>
    public List<string> Extensions { get; set; } = new();

    /// <summary>Top-level media types this category claims (e.g. <c>video</c>), used when a file has
    /// no usable extension and the server reported a <c>Content-Type</c>. Not surfaced in the editor.</summary>
    public List<string> MediaTypes { get; set; } = new();

    /// <summary>Position in the user's order. One number doing three jobs: the order of the sidebar
    /// list, the sort key of the grid's Type column, and — when two categories claim the same
    /// extension — which one wins (the earliest).</summary>
    public int Position { get; set; }

    /// <summary>True for the categories the app ships with. They can be edited and reordered but
    /// not deleted.</summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>Reserved for a future user-supplied icon, always null today. Present so that a file
    /// exported now stays valid once uploaded icons exist.</summary>
    public string IconData { get; set; }

    /// <summary>A copy, so an editor can be cancelled without having mutated the live list.</summary>
    public DownloadCategory Clone() => new()
    {
        Id = Id,
        Name = Name,
        Icon = Icon,
        Color = Color,
        Extensions = new List<string>(Extensions ?? new List<string>()),
        MediaTypes = new List<string>(MediaTypes ?? new List<string>()),
        Position = Position,
        IsBuiltIn = IsBuiltIn,
        IconData = IconData
    };
}

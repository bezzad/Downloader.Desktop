using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Downloader.Desktop.Models;

namespace Downloader.Desktop.Services;

/// <summary>
/// The single authority on file-type categories: holds the user's ordered list and answers which
/// category a download belongs to. Everything that shows or filters by type — the sidebar, the
/// grid's Type column, the list filter, the Add dialog, the Details window, the row menu — goes
/// through here, so they agree by construction.
/// </summary>
/// <remarks>
/// Resolution order is: the user's explicit choice on the download, then its file extension, then
/// the <c>Content-Type</c> the server or browser reported, then "Other". A download therefore always
/// has exactly one category, including while its name is still being fetched.
/// </remarks>
public class CategoryService
{
    private readonly object _gate = new();
    private List<DownloadCategory> _categories = new();

    /// <summary>Raised when the list changes (added, edited, removed, reordered, imported), so
    /// views re-resolve. Anything a category could change is derived, never cached to disk.</summary>
    public event Action Changed;

    /// <summary>The categories in the user's order.</summary>
    public IReadOnlyList<DownloadCategory> Categories
    {
        get { lock (_gate) return _categories.ToList(); }
    }

    /// <summary>Points the service at the loaded configuration's list. The list instance is shared,
    /// so edits made here are what gets saved.</summary>
    public void Initialize(Config config)
    {
        lock (_gate)
            _categories = config?.Categories ?? new List<DownloadCategory>();

        Renumber();
        Changed?.Invoke();
    }

    // ---- lookup ------------------------------------------------------------------------------

    /// <summary>The category a download belongs to, never null.</summary>
    public DownloadCategory Resolve(DownloadItem item) =>
        item == null
            ? Other()
            : Resolve(item.CategoryId, !string.IsNullOrWhiteSpace(item.FileName) ? item.FileName : item.PreviewName,
                item.ContentType);

    /// <summary>The category for an explicit choice plus what is known about the file, never null.</summary>
    public DownloadCategory Resolve(string categoryId, string fileName, string contentType)
    {
        var all = Categories;

        // The user's choice wins over everything the app can work out for itself.
        if (!string.IsNullOrWhiteSpace(categoryId))
        {
            var chosen = all.FirstOrDefault(c => c.Id == categoryId);
            if (chosen != null)
                return chosen;
        }

        return Detect(all, fileName, contentType);
    }

    /// <summary>What the app works out on its own, ignoring any explicit choice. Pure over the list
    /// it is given, so the whole precedence is testable without a service instance.</summary>
    public static DownloadCategory Detect(IReadOnlyList<DownloadCategory> categories, string fileName,
        string contentType)
    {
        var ext = ExtensionOf(fileName);
        if (!string.IsNullOrEmpty(ext))
        {
            var byExtension = ByExtension(categories, ext);
            if (byExtension != null)
                return byExtension;
        }

        var byContentType = ByContentType(categories, contentType);
        if (byContentType != null)
            return byContentType;

        return OtherOf(categories);
    }

    /// <summary>The category by id, or null.</summary>
    public DownloadCategory ById(string id) =>
        string.IsNullOrWhiteSpace(id) ? null : Categories.FirstOrDefault(c => c.Id == id);

    /// <summary>The "Other" category — the floor of every lookup.</summary>
    public DownloadCategory Other() => OtherOf(Categories);

    private static DownloadCategory OtherOf(IReadOnlyList<DownloadCategory> categories) =>
        categories.FirstOrDefault(c => c.Id == DownloadCategory.OtherId)
        ?? categories.LastOrDefault()
        // A list with nothing in it would leave every download pointing at a category that does not
        // exist, so answer with a throwaway rather than null. Import refuses an empty list for the
        // same reason; this is the belt to that braces.
        ?? new DownloadCategory { Id = DownloadCategory.OtherId, Name = DownloadCategory.OtherId, Icon = "file" };

    /// <summary>The file extension, lowercase and without its dot, or empty. Callers may pass a raw
    /// URL (the Add dialog does, before anything has resolved a real file name) rather than a bare
    /// file name — a signed download link's <c>?token=…&amp;expires=…</c> query string has no dots of
    /// its own, so without stripping it <see cref="Path.GetExtension"/> reads everything after the
    /// real extension as part of it and nothing matches.</summary>
    public static string ExtensionOf(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return string.Empty;

        var path = fileName.Split('?', '#')[0];
        return Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
    }

    /// <summary>The first category claiming this extension. "First" is by the user's order, which is
    /// what resolves two categories claiming the same one.</summary>
    private static DownloadCategory ByExtension(IReadOnlyList<DownloadCategory> categories, string ext) =>
        categories.OrderBy(c => c.Position)
            .FirstOrDefault(c => c.Extensions != null &&
                                 c.Extensions.Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase)));

    /// <summary>The category for a reported media type, or null when it says nothing useful.</summary>
    private static DownloadCategory ByContentType(IReadOnlyList<DownloadCategory> categories, string contentType)
    {
        var (top, sub) = SplitContentType(contentType);
        if (string.IsNullOrEmpty(top))
            return null;

        // A media type the app knows an extension for is answered by the extension table, so a
        // category the user added claiming "zip" also catches application/zip.
        if (MediaTypeExtensions.TryGetValue($"{top}/{sub}", out var mapped))
        {
            var byMapped = ByExtension(categories, mapped);
            if (byMapped != null)
                return byMapped;
        }

        // Then the top-level type, which is what makes video/x-anything still read as a video.
        var byTop = categories.OrderBy(c => c.Position)
            .FirstOrDefault(c => c.MediaTypes != null &&
                                 c.MediaTypes.Any(m => string.Equals(m, top, StringComparison.OrdinalIgnoreCase)));
        if (byTop != null)
            return byTop;

        // Last, the subtype read as if it were an extension (application/pdf, application/zip).
        return string.IsNullOrEmpty(sub) ? null : ByExtension(categories, sub);
    }

    /// <summary>Splits a <c>Content-Type</c> into its lowercase top-level type and subtype, dropping
    /// any parameters. <c>application/octet-stream</c> yields nothing — it is the header's way of
    /// saying it does not know.</summary>
    public static (string Top, string Sub) SplitContentType(string contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return (string.Empty, string.Empty);

        var value = contentType.Split(';')[0].Trim().ToLowerInvariant();
        if (value.Length == 0 || value == "application/octet-stream" || value == "binary/octet-stream")
            return (string.Empty, string.Empty);

        var slash = value.IndexOf('/');
        return slash <= 0
            ? (value, string.Empty)
            : (value[..slash], value[(slash + 1)..]);
    }

    /// <summary>Media types whose subtype is not simply the file extension.</summary>
    private static readonly Dictionary<string, string> MediaTypeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["application/x-zip-compressed"] = "zip",
        ["application/x-7z-compressed"] = "7z",
        ["application/x-rar-compressed"] = "rar",
        ["application/vnd.rar"] = "rar",
        ["application/gzip"] = "gz",
        ["application/x-tar"] = "tar",
        ["application/x-msdownload"] = "exe",
        ["application/vnd.microsoft.portable-executable"] = "exe",
        ["application/x-msi"] = "msi",
        ["application/vnd.android.package-archive"] = "apk",
        ["application/x-debian-package"] = "deb",
        ["application/vnd.debian.binary-package"] = "deb",
        ["application/x-redhat-package-manager"] = "rpm",
        ["application/x-apple-diskimage"] = "dmg",
        ["application/x-iso9660-image"] = "iso",
        ["application/epub+zip"] = "epub",
        ["application/msword"] = "doc",
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = "docx",
        ["application/vnd.ms-excel"] = "xls",
        ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = "xlsx",
        ["application/vnd.ms-powerpoint"] = "ppt",
        ["application/vnd.openxmlformats-officedocument.presentationml.presentation"] = "pptx",
        ["text/plain"] = "txt",
        ["text/csv"] = "csv",
        ["text/markdown"] = "md",
        ["application/rtf"] = "rtf",
        ["application/x-mpegurl"] = "m3u8",
        ["application/vnd.apple.mpegurl"] = "m3u8"
    };

    // ---- editing -----------------------------------------------------------------------------

    /// <summary>Adds a category at the end of the list.</summary>
    public void Add(DownloadCategory category)
    {
        if (category == null)
            return;

        lock (_gate)
        {
            category.Id ??= NewId();
            category.Position = _categories.Count;
            _categories.Add(category);
        }

        Renumber();
        Changed?.Invoke();
    }

    /// <summary>Applies an edited copy over the live category with the same id.</summary>
    public void Update(DownloadCategory edited)
    {
        if (edited == null)
            return;

        lock (_gate)
        {
            var live = _categories.FirstOrDefault(c => c.Id == edited.Id);
            if (live == null)
                return;

            live.Name = edited.Name;
            live.Icon = edited.Icon;
            live.Color = edited.Color;
            live.Extensions = NormalizeExtensions(edited.Extensions);
            live.IconData = edited.IconData;
        }

        Changed?.Invoke();
    }

    /// <summary>Removes a user-created category. Built-in ones are never removable.</summary>
    /// <returns>True when it was removed.</returns>
    public bool Remove(string id)
    {
        lock (_gate)
        {
            var live = _categories.FirstOrDefault(c => c.Id == id);
            if (live == null || live.IsBuiltIn)
                return false;

            _categories.Remove(live);
        }

        Renumber();
        Changed?.Invoke();
        return true;
    }

    /// <summary>Moves a category one place up (-1) or down (+1).</summary>
    /// <returns>True when the order changed.</returns>
    public bool Move(string id, int delta)
    {
        lock (_gate)
        {
            var index = _categories.FindIndex(c => c.Id == id);
            if (index < 0)
                return false;

            var target = index + delta;
            if (target < 0 || target >= _categories.Count)
                return false;

            var item = _categories[index];
            _categories.RemoveAt(index);
            _categories.Insert(target, item);
        }

        Renumber();
        Changed?.Invoke();
        return true;
    }

    /// <summary>Whether a name is free — two categories with the same name are indistinguishable in
    /// the sidebar. <paramref name="exceptId"/> lets a category keep its own name while editing.</summary>
    public bool IsNameAvailable(string name, string exceptId = null) =>
        !string.IsNullOrWhiteSpace(name) &&
        !Categories.Any(c => c.Id != exceptId &&
                             string.Equals(c.Name?.Trim(), name.Trim(), StringComparison.CurrentCultureIgnoreCase));

    /// <summary>A fresh identifier for a user-created category.</summary>
    public static string NewId() => "cat_" + Guid.NewGuid().ToString("N")[..12];

    /// <summary>Lowercases, de-dots and de-duplicates an extension list.</summary>
    public static List<string> NormalizeExtensions(IEnumerable<string> extensions) =>
        (extensions ?? Enumerable.Empty<string>())
        .Select(e => e?.Trim().TrimStart('.').ToLowerInvariant())
        .Where(e => !string.IsNullOrEmpty(e))
        .Distinct()
        .ToList();

    /// <summary>Splits the editor's comma/space separated extension text into a clean list.</summary>
    public static List<string> ParseExtensions(string text) =>
        NormalizeExtensions((text ?? string.Empty).Split(new[] { ',', ';', ' ', '\t', '\n', '\r' },
            StringSplitOptions.RemoveEmptyEntries));

    private void Renumber()
    {
        lock (_gate)
        {
            for (var i = 0; i < _categories.Count; i++)
                _categories[i].Position = i;
        }
    }

    /// <summary>Re-raises <see cref="Changed"/> after the list was replaced wholesale (an import).</summary>
    public void Reload(Config config) => Initialize(config);

    // ---- defaults ----------------------------------------------------------------------------

    /// <summary>
    /// The categories the app ships with, carrying exactly the extensions it recognized before
    /// categories existed. <paramref name="name"/> supplies each one's display name — they are
    /// named in the app's language at the moment they are created and keep those names afterwards,
    /// because from then on the name is the user's data, not interface text.
    /// </summary>
    public static List<DownloadCategory> CreateDefaults(Func<string, string> name = null)
    {
        name ??= key => Localizer.Instance[key];

        var defaults = new[]
        {
            ("video", "Cat_Video", "video", "#4F8DF5", "video",
                "mp4 mkv avi mov webm flv wmv m4v mpeg mpg m3u8 ts"),
            ("audio", "Cat_Audio", "audio", "#2DBED6", "audio",
                "mp3 wav flac aac ogg m4a wma opus"),
            ("image", "Cat_Image", "image", "#35B87F", "image",
                "jpg jpeg png gif bmp webp svg ico tif tiff heic"),
            ("document", "Cat_Document", "document", "#E0A23C", null,
                "pdf doc docx txt rtf xls xlsx ppt pptx csv md epub"),
            ("archive", "Cat_Archive", "archive", "#B07CE8", null,
                "zip rar 7z tar gz bz2 xz zst"),
            ("app", "Cat_App", "app", "#E2705A", null,
                "exe msi apk deb rpm dmg appimage pkg"),
            ("disc", "Cat_Disc", "disc", "#7A8CA6", null,
                "iso img bin vhd"),
            (DownloadCategory.OtherId, "Cat_Other", "file", "#8A93A0", null, "")
        };

        return defaults.Select((d, index) => new DownloadCategory
        {
            Id = d.Item1,
            Name = name(d.Item2),
            Icon = d.Item3,
            Color = d.Item4,
            MediaTypes = d.Item5 == null ? new List<string>() : new List<string> { d.Item5 },
            Extensions = ParseExtensions(d.Item6),
            Position = index,
            IsBuiltIn = true
        }).ToList();
    }
}

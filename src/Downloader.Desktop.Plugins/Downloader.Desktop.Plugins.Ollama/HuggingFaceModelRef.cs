namespace Downloader.Desktop.Plugins.Ollama;

/// <summary>
/// A parsed HuggingFace model repository reference: owner, repo, the revision (default <c>main</c>) and,
/// when the link points at one file, that file's path inside the repo.
/// <para>
/// Accepts the shapes people actually paste — the repo page, a <c>/tree/&lt;rev&gt;</c> browse link, a
/// <c>/blob/&lt;rev&gt;/&lt;file&gt;</c> viewer link and a <c>/resolve/&lt;rev&gt;/&lt;file&gt;</c> direct
/// download link — and refuses everything on the site that is not a model repository: datasets, spaces,
/// user profiles and HuggingFace's own pages. Pure: parsing never touches the network, because the host
/// asks "can you resolve this?" on every keystroke in the Add window.
/// </para>
/// </summary>
public sealed class HuggingFaceModelRef
{
    public const string Host = "huggingface.co";

    public string Owner { get; }
    public string Repo { get; }
    public string Revision { get; }

    /// <summary>The file inside the repository the link named, or null for a whole-repo reference (which
    /// is what makes the plugin offer the repo's model files as variants).</summary>
    public string? FilePath { get; }

    public bool HasFile => !string.IsNullOrEmpty(FilePath);

    /// <summary>"owner/repo", as HuggingFace's own API addresses a repository.</summary>
    public string RepoId => $"{Owner}/{Repo}";

    public override string ToString() =>
        HasFile ? $"{RepoId}@{Revision}/{FilePath}" : $"{RepoId}@{Revision}";

    private HuggingFaceModelRef(string owner, string repo, string revision, string? filePath)
    {
        Owner = owner;
        Repo = repo;
        Revision = revision;
        FilePath = filePath;
    }

    /// <summary>The same repository, pointing at one of its files.</summary>
    public HuggingFaceModelRef WithFile(string filePath) =>
        new(Owner, Repo, Revision, filePath);

    /// <summary>The direct download address for this reference's file.</summary>
    public string DownloadUrl => HasFile
        ? $"https://{Host}/{Owner}/{Repo}/resolve/{Revision}/{FilePath}?download=true"
        : throw new InvalidOperationException("This reference does not name a file.");

    /// <summary>Sections of the site that are never a model repository. A first path segment matching one
    /// of these is refused outright, so a dataset or a space is left to the ordinary download path.</summary>
    private static readonly HashSet<string> ReservedSections = new(StringComparer.OrdinalIgnoreCase)
    {
        "datasets", "spaces", "models", "docs", "blog", "posts", "papers", "collections", "organizations",
        "settings", "pricing", "join", "login", "logout", "search", "new", "chat", "learn", "tasks",
        "api", "static-proxy", "front", "brand", "terms-of-service", "privacy", "huggingface", "welcome",
    };

    public static bool TryParse(string? input, out HuggingFaceModelRef? modelRef)
    {
        modelRef = null;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        if (!Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri))
            return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;

        var host = uri.Host;
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            host = host[4..];
        if (!host.Equals(Host, StringComparison.OrdinalIgnoreCase)
            && !host.Equals("hf.co", StringComparison.OrdinalIgnoreCase))
            return false;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // A single segment is a user or organisation profile, not a repository.
        if (segments.Length < 2)
            return false;
        if (ReservedSections.Contains(segments[0]))
            return false;

        var owner = Uri.UnescapeDataString(segments[0]);
        var repo = Uri.UnescapeDataString(segments[1]);
        if (!IsName(owner) || !IsName(repo))
            return false;

        var revision = "main";
        string? file = null;

        if (segments.Length > 2)
        {
            var kind = segments[2];
            var isFileLink = kind is "resolve" or "blob" or "raw";
            if (!isFileLink && kind != "tree")
                return false; // /discussions, /commits, … are pages about the repo, not the repo

            if (segments.Length > 3)
                revision = Uri.UnescapeDataString(segments[3]);
            if (isFileLink && segments.Length > 4)
                file = string.Join('/', segments.Skip(4).Select(Uri.UnescapeDataString));
        }

        modelRef = new HuggingFaceModelRef(owner, repo, revision, file);
        return true;
    }

    private static bool IsName(string value) =>
        value.Length > 0 && value.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.');
}

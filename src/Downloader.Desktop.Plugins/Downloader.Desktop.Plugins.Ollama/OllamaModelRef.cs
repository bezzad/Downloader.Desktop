using System.Text.RegularExpressions;

namespace Downloader.Desktop.Plugins.Ollama;

/// <summary>
/// A parsed Ollama model reference: namespace (default "library"), model and tag (default "latest").
/// Accepts ollama.com library URLs (<c>https://ollama.com/library/gemma3:12b</c>) and bare
/// <c>name[:tag]</c> / <c>user/name[:tag]</c> inputs. Pure and unit-testable.
/// </summary>
public sealed partial class OllamaModelRef
{
    public string Namespace { get; }
    public string Model { get; }
    public string Tag { get; }

    /// <summary>"library/gemma3:12b" style display / manifest path pieces.</summary>
    public string PathNamespaceModel => $"{Namespace}/{Model}";
    public override string ToString() => $"{PathNamespaceModel}:{Tag}";

    private OllamaModelRef(string ns, string model, string tag)
    {
        Namespace = ns; Model = model; Tag = tag;
    }

    // name segments: start alnum, then alnum . _ - ; tag: alnum . _ - (e.g. "12b", "latest", "v1.5-q4_K_M")
    [GeneratedRegex(@"^(?:(?<ns>[a-z0-9][a-z0-9._-]*)/)?(?<model>[a-z0-9][a-z0-9._-]*)(?::(?<tag>[A-Za-z0-9][A-Za-z0-9._-]*))?$",
        RegexOptions.IgnoreCase)]
    private static partial Regex BareRef();

    /// <summary>File extensions a bare token must NOT end with — a pasted "video.mp4" is a file name the
    /// user mistyped, never an Ollama model. (Model names with dots like "llama3.2" stay claimable.)</summary>
    private static readonly string[] FileExtensions =
    {
        ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".iso", ".exe", ".msi", ".dmg", ".pkg",
        ".deb", ".rpm", ".apk", ".appimage", ".mp4", ".mkv", ".avi", ".mov", ".webm", ".mp3", ".m4a",
        ".flac", ".wav", ".jpg", ".jpeg", ".png", ".gif", ".webp", ".pdf", ".doc", ".docx", ".xls",
        ".xlsx", ".ppt", ".pptx", ".txt", ".csv", ".json", ".xml", ".html", ".htm", ".m3u8", ".ts",
        ".srt", ".gguf", ".bin", ".dll", ".so", ".dylib"
    };

    public static bool TryParse(string? input, out OllamaModelRef? modelRef)
    {
        modelRef = null;
        if (string.IsNullOrWhiteSpace(input))
            return false;
        input = input.Trim();

        // ollama.com library URL: https://ollama.com/library/gemma3:12b (or /library/gemma3)
        if (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(input, UriKind.Absolute, out var uri))
                return false;
            var host = uri.Host.ToLowerInvariant();
            if (host != "ollama.com" && host != "www.ollama.com")
                return false;
            var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            // /library/<model[:tag]>  or  /<user>/<model[:tag]> (community models)
            if (segments.Length != 2)
                return false;
            return TryParseBare($"{segments[0]}/{Uri.UnescapeDataString(segments[1])}", out modelRef);
        }

        // Bare name[:tag] — must not look like a path or a file name.
        if (input.Contains(' ') || input.Contains('\\') || input.StartsWith('/') || input.StartsWith('~') ||
            input.Contains("://"))
            return false;
        var lower = input.ToLowerInvariant();
        var nameOnly = lower.Contains(':') ? lower[..lower.IndexOf(':')] : lower;
        if (FileExtensions.Any(ext => nameOnly.EndsWith(ext, StringComparison.Ordinal)))
            return false;

        return TryParseBare(input, out modelRef);
    }

    private static bool TryParseBare(string input, out OllamaModelRef? modelRef)
    {
        modelRef = null;
        var m = BareRef().Match(input);
        if (!m.Success)
            return false;
        var ns = m.Groups["ns"].Success ? m.Groups["ns"].Value : "library";
        var tag = m.Groups["tag"].Success ? m.Groups["tag"].Value : "latest";
        modelRef = new OllamaModelRef(ns.ToLowerInvariant(), m.Groups["model"].Value.ToLowerInvariant(), tag);
        return true;
    }
}

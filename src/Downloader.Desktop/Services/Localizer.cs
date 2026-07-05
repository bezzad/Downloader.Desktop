using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using Avalonia.Media;
using Avalonia.Platform;

namespace Downloader.Desktop.Services;

/// <summary>
/// Runtime localization service. Holds the active language's key→text map (with English as a
/// fallback) and exposes it through a string indexer so XAML can bind <c>{i18n:Tr Some_Key}</c> and
/// update live when the language changes. Translations live in <c>Assets/i18n/{lang}.json</c>
/// (embedded as Avalonia resources, so the app stays fully self-contained).
/// </summary>
public sealed class Localizer : INotifyPropertyChanged
{
    /// <summary>Supported languages: code → native display name. English is always the default.</summary>
    public static readonly IReadOnlyList<LanguageOption> Languages = new[]
    {
        new LanguageOption("en", "English"),
        new LanguageOption("fa", "فارسی"),
        new LanguageOption("es", "Español"),
        new LanguageOption("fr", "Français"),
        new LanguageOption("ar", "العربية"),
        new LanguageOption("eo", "Esperanto"),
        new LanguageOption("tr", "Türkçe"),
        new LanguageOption("az", "Azərbaycan"),
        new LanguageOption("de", "Deutsch"),
        new LanguageOption("it", "Italiano"),
        new LanguageOption("pt", "Português"),
        new LanguageOption("ru", "Русский"),
        new LanguageOption("hi", "हिन्दी"),
        new LanguageOption("zh", "中文"),
        new LanguageOption("ja", "日本語"),
        new LanguageOption("ko", "한국어"),
    };

    private static readonly HashSet<string> RtlLanguages = new(StringComparer.OrdinalIgnoreCase) { "fa", "ar" };

    // NOTE: declared after the static fields above — the constructor calls Load() which reads
    // RtlLanguages, so Instance must be initialized last to avoid a static-init NRE.
    public static Localizer Instance { get; } = new();

    private Dictionary<string, string> _map = new();
    private Dictionary<string, string> _fallback = new();

    private Localizer() => Load("en");

    public string Current { get; private set; } = "en";

    /// <summary>
    /// Bumped on every <see cref="Load"/>. <c>{i18n:Tr}</c> bindings watch this normal property
    /// (via a converter) so they re-evaluate reliably on language change — Avalonia indexer-change
    /// notifications proved unreliable for already-rendered bindings.
    /// </summary>
    public int Tick { get; private set; }

    /// <summary>RightToLeft for Persian/Arabic, otherwise LeftToRight. Bound by each window.</summary>
    public FlowDirection FlowDirection { get; private set; } = FlowDirection.LeftToRight;

    /// <summary>Translated text for <paramref name="key"/> (falls back to English, then the key itself).</summary>
    public string this[string key]
    {
        get
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            if (_map.TryGetValue(key, out var v)) return v;
            if (_fallback.TryGetValue(key, out var f)) return f;
            return key;
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;

    public static bool IsRtl(string lang) => lang != null && RtlLanguages.Contains(lang);

    /// <summary>Switches the active language and refreshes every bound string.</summary>
    public void Load(string lang)
    {
        lang = string.IsNullOrWhiteSpace(lang) ? "en" : lang.Trim().ToLowerInvariant();
        _fallback = LoadMap("en");
        _map = lang == "en" ? _fallback : LoadMap(lang);
        Current = lang;
        FlowDirection = IsRtl(lang) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        unchecked { Tick++; }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Tick)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FlowDirection)));
    }

    private static Dictionary<string, string> LoadMap(string lang)
    {
        try
        {
            var uri = new Uri($"avares://Downloader.Desktop/Assets/i18n/{lang}.json");
            using var stream = AssetLoader.Open(uri);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
                   ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }
}

/// <summary>A selectable UI language (code + native name + country flag).</summary>
public sealed class LanguageOption
{
    public LanguageOption(string code, string name)
    {
        Code = code;
        Name = name;
    }

    public string Code { get; }
    public string Name { get; }
    public override string ToString() => Name;

    private Avalonia.Media.Imaging.Bitmap _flag;
    private bool _flagLoaded;

    /// <summary>Raster height the SVG flags are rendered at — 3x the 15px display size so they stay
    /// crisp on HiDPI screens.</summary>
    private const int FlagRasterHeight = 45;

    /// <summary>Small country flag shown beside the language (lazy-rendered from Assets/flags/{code}.svg
    /// via Svg.Skia — vector sources, so any raster size stays sharp).</summary>
    public Avalonia.Media.Imaging.Bitmap Flag
    {
        get
        {
            if (_flagLoaded)
                return _flag;
            _flagLoaded = true;
            try
            {
                using var s = Avalonia.Platform.AssetLoader.Open(
                    new Uri($"avares://Downloader.Desktop/Assets/flags/{Code}.svg"));
                _flag = RenderSvg(s, FlagRasterHeight);
            }
            catch
            {
                _flag = null; // missing/broken flag asset → just show the name
            }
            return _flag;
        }
    }

    /// <summary>Rasterizes an SVG stream to an Avalonia bitmap at the given pixel height (width follows
    /// the SVG's own aspect ratio).</summary>
    private static Avalonia.Media.Imaging.Bitmap RenderSvg(System.IO.Stream stream, int targetHeight)
    {
        using var svg = new Svg.Skia.SKSvg();
        var picture = svg.Load(stream);
        if (picture == null || picture.CullRect.Height <= 0)
            return null;

        var scale = targetHeight / picture.CullRect.Height;
        var width = (int)Math.Ceiling(picture.CullRect.Width * scale);
        var info = new SkiaSharp.SKImageInfo(width, targetHeight,
            SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul);

        using var surface = SkiaSharp.SKSurface.Create(info);
        surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
        surface.Canvas.Scale(scale);
        surface.Canvas.DrawPicture(picture);
        surface.Canvas.Flush();

        using var image = surface.Snapshot();
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        using var ms = new System.IO.MemoryStream(data.ToArray());
        return new Avalonia.Media.Imaging.Bitmap(ms);
    }
}

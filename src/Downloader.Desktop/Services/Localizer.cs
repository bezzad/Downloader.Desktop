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

/// <summary>A selectable UI language (code + native name).</summary>
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
}

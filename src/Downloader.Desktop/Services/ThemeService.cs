using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Downloader.Desktop.Models;

namespace Downloader.Desktop.Services;

/// <summary>A selectable accent color (key + display name + base color).</summary>
public sealed class AccentOption
{
    public AccentOption(string key, string name, Color color)
    {
        Key = key;
        Name = name;
        Color = color;
    }

    public string Key { get; }
    public string Name { get; }
    public Color Color { get; }
    /// <summary>Swatch brush for the picker.</summary>
    public IBrush Brush => new SolidColorBrush(Color);
}

/// <summary>
/// Applies the app theme: the Light/Dark variant plus a user-chosen accent color. The accent is applied
/// by overriding the standard Fluent accent color resources (and a translucent row-selection tint) at the
/// Application level, so every accent-driven surface (nav selection, accent buttons, links, selected row)
/// follows the choice in both themes. Keeps the existing Light/Dark palettes otherwise.
/// </summary>
public static class ThemeService
{
    public static readonly IReadOnlyList<AccentOption> Accents = new[]
    {
        new AccentOption("Teal",   "Teal",   Color.FromRgb(0x16, 0xA4, 0xC2)),
        new AccentOption("Blue",   "Blue",   Color.FromRgb(0x2F, 0x7D, 0xE1)),
        new AccentOption("Purple", "Purple", Color.FromRgb(0x8A, 0x60, 0xE6)),
        new AccentOption("Green",  "Green",  Color.FromRgb(0x2B, 0xA8, 0x6B)),
        new AccentOption("Amber",  "Amber",  Color.FromRgb(0xE2, 0x92, 0x2E)),
    };

    public static AccentOption Find(string key) =>
        Accents.FirstOrDefault(a => a.Key == key) ?? Accents[0];

    /// <summary>Apply both the Light/Dark variant and the accent from the given config.</summary>
    public static void Apply(Config config)
    {
        var app = Application.Current;
        if (app == null)
            return;
        if (app.Styles.Count > 0 && app.Styles[0] is FluentTheme)
            app.RequestedThemeVariant = config.ThemeMode;
        ApplyAccent(config.Settings?.AccentColor);
    }

    /// <summary>Override the accent color resources (and the selected-row tint) from an accent key.</summary>
    public static void ApplyAccent(string key)
    {
        var app = Application.Current;
        if (app == null)
            return;

        var c = Find(key).Color;
        var res = app.Resources;
        res["SystemAccentColor"] = c;
        // Fluent derives accent variants from these; light shades are used by the dark theme and vice
        // versa, so provide both directions tinted toward white / black.
        res["SystemAccentColorLight1"] = Mix(c, Colors.White, 0.20);
        res["SystemAccentColorLight2"] = Mix(c, Colors.White, 0.40);
        res["SystemAccentColorLight3"] = Mix(c, Colors.White, 0.60);
        res["SystemAccentColorDark1"] = Mix(c, Colors.Black, 0.20);
        res["SystemAccentColorDark2"] = Mix(c, Colors.Black, 0.40);
        res["SystemAccentColorDark3"] = Mix(c, Colors.Black, 0.60);

        // Selected-row tint follows the accent (low alpha so the row text stays readable in both themes).
        res["RowSelectionBrush"] = new SolidColorBrush(c, 0.28);
    }

    /// <summary>Linear blend of two colors (t=0 returns a, t=1 returns b).</summary>
    private static Color Mix(Color a, Color b, double t)
    {
        byte L(byte x, byte y) => (byte)(x + (y - x) * t);
        return Color.FromRgb(L(a.R, b.R), L(a.G, b.G), L(a.B, b.B));
    }
}

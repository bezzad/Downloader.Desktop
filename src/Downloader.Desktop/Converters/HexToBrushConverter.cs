using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Downloader.Desktop.Converters;

/// <summary>
/// Turns a category's <c>#RRGGBB</c> color into a brush. A missing or unparsable value falls back to
/// the app accent, so a hand-edited or imported category always renders.
/// </summary>
public class HexToBrushConverter : IValueConverter
{
    public static readonly HexToBrushConverter Instance = new();

    private static readonly Dictionary<string, IBrush> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The brush for a hex color, or the accent when it says nothing usable.</summary>
    public static IBrush BrushFor(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return Accent();

        lock (Cache)
        {
            if (Cache.TryGetValue(hex, out var cached))
                return cached;

            if (!Color.TryParse(hex, out var color))
                return Accent();

            var brush = new SolidColorBrush(color);
            brush.ToImmutable();
            Cache[hex] = brush;
            return brush;
        }
    }

    private static IBrush Accent() =>
        Application.Current?.TryGetResource("SystemAccentColor", null, out var value) == true && value is Color color
            ? new SolidColorBrush(color)
            : Brushes.Gray;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => BrushFor(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Downloader.Desktop.Converters;

/// <summary>
/// Converts a <see cref="DownloadItemViewModel"/> file-kind string (video/audio/image/...) into an
/// icon <see cref="Geometry"/> so each download row shows what kind of file it is.
/// </summary>
public class FileKindToIconConverter : IValueConverter
{
    public static readonly FileKindToIconConverter Instance = new();

    // 24x24 grid SVG path data per kind.
    private static readonly Dictionary<string, string> Paths = new()
    {
        ["video"] = "M4 7.25C4 6.01 5.01 5 6.25 5h7.5C14.99 5 16 6.01 16 7.25v.7l3.13-1.8A1.25 1.25 0 0 1 21 7.43v9.14a1.25 1.25 0 0 1-1.87 1.08L16 15.05v.7C16 16.99 14.99 18 13.75 18h-7.5C5.01 18 4 16.99 4 15.75v-8.5Z",
        ["audio"] = "M18 4.25a.75.75 0 0 0-.92-.73l-8 2A.75.75 0 0 0 8.5 6.25v8.06A3 3 0 1 0 10 16.75V9.34l6.5-1.62v4.59A3 3 0 1 0 18 15.25V4.25Z",
        ["image"] = "M5.75 3A2.75 2.75 0 0 0 3 5.75v12.5A2.75 2.75 0 0 0 5.75 21h12.5A2.75 2.75 0 0 0 21 18.25V5.75A2.75 2.75 0 0 0 18.25 3H5.75ZM4.5 5.75c0-.69.56-1.25 1.25-1.25h12.5c.69 0 1.25.56 1.25 1.25v9.19l-3.22-3.22a1.75 1.75 0 0 0-2.47 0L5.06 19.4A1.25 1.25 0 0 1 4.5 18.25V5.75ZM9 8.5A1.5 1.5 0 1 0 9 11.5 1.5 1.5 0 0 0 9 8.5Z",
        ["archive"] = "M3.5 6.25C3.5 5.01 4.51 4 5.75 4h12.5c1.24 0 2.25 1.01 2.25 2.25 0 .98-.63 1.81-1.5 2.12v7.88A3.75 3.75 0 0 1 15.25 21h-6.5A3.75 3.75 0 0 1 5 17.25V8.37A2.25 2.25 0 0 1 3.5 6.25ZM6.5 8.5v8.75c0 1.24 1.01 2.25 2.25 2.25h6.5c1.24 0 2.25-1.01 2.25-2.25V8.5h-4.75v1.25a.75.75 0 0 1-1.5 0V8.5H6.5ZM5.75 5.5a.75.75 0 0 0 0 1.5h12.5a.75.75 0 0 0 0-1.5H5.75Z",
        ["document"] = "M5.75 2A2.75 2.75 0 0 0 3 4.75v14.5A2.75 2.75 0 0 0 5.75 22h9.5A2.75 2.75 0 0 0 18 19.25V8.66c0-.46-.18-.9-.51-1.23l-4.92-4.92A1.75 1.75 0 0 0 11.34 2H5.75ZM4.5 4.75c0-.69.56-1.25 1.25-1.25H11V7c0 1.1.9 2 2 2h3.5v10.25c0 .69-.56 1.25-1.25 1.25h-9.5c-.69 0-1.25-.56-1.25-1.25V4.75ZM12.5 4.41 15.59 7.5H13a.5.5 0 0 1-.5-.5V4.41Z",
        ["app"] = "M5.75 3A2.75 2.75 0 0 0 3 5.75v12.5A2.75 2.75 0 0 0 5.75 21h12.5A2.75 2.75 0 0 0 21 18.25V5.75A2.75 2.75 0 0 0 18.25 3H5.75ZM4.5 8.5h15v9.75c0 .69-.56 1.25-1.25 1.25H5.75c-.69 0-1.25-.56-1.25-1.25V8.5ZM7 6.25a.75.75 0 1 0 0-1.5.75.75 0 0 0 0 1.5ZM9.5 6.25a.75.75 0 1 0 0-1.5.75.75 0 0 0 0 1.5Z",
        ["disc"] = "M12 2.25c5.38 0 9.75 4.37 9.75 9.75s-4.37 9.75-9.75 9.75S2.25 17.38 2.25 12 6.62 2.25 12 2.25Zm0 1.5A8.25 8.25 0 1 0 20.25 12 8.25 8.25 0 0 0 12 3.75ZM12 9.25a2.75 2.75 0 1 1 0 5.5 2.75 2.75 0 0 1 0-5.5Z",
        ["file"] = "M5.75 2A2.75 2.75 0 0 0 3 4.75v14.5A2.75 2.75 0 0 0 5.75 22h9.5A2.75 2.75 0 0 0 18 19.25V8.66c0-.46-.18-.9-.51-1.23l-4.92-4.92A1.75 1.75 0 0 0 11.34 2H5.75ZM4.5 4.75c0-.69.56-1.25 1.25-1.25H11V7c0 1.1.9 2 2 2h3.5v10.25c0 .69-.56 1.25-1.25 1.25h-9.5c-.69 0-1.25-.56-1.25-1.25V4.75Z"
    };

    private static readonly Dictionary<string, Geometry> Cache = new();

    public static Geometry GetIcon(string kind)
    {
        kind ??= "file";
        if (!Paths.ContainsKey(kind))
            kind = "file";

        if (!Cache.TryGetValue(kind, out var geometry))
        {
            geometry = StreamGeometry.Parse(Paths[kind]);
            Cache[kind] = geometry;
        }

        return geometry;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => GetIcon(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

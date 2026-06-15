using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using Downloader.Desktop.Services;

namespace Downloader.Desktop.Markup;

/// <summary>
/// XAML markup extension for localized text: <c>Text="{i18n:Tr Settings_Title}"</c>.
/// Binds to <see cref="Localizer.Tick"/> (which changes on every language switch) and resolves the
/// key through <see cref="TrConverter"/>, so the text updates live and reliably when the language changes.
/// </summary>
public sealed class TrExtension : MarkupExtension
{
    public TrExtension() { }
    public TrExtension(string key) => Key = key;

    /// <summary>The translation key (positional in XAML).</summary>
    public string Key { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return new Binding
        {
            Source = Localizer.Instance,
            Path = nameof(Localizer.Tick),
            Mode = BindingMode.OneWay,
            Converter = TrConverter.Instance,
            ConverterParameter = Key
        };
    }
}

/// <summary>Returns the translation for the key passed as the converter parameter.</summary>
public sealed class TrConverter : IValueConverter
{
    public static readonly TrConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => Localizer.Instance[parameter as string];

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

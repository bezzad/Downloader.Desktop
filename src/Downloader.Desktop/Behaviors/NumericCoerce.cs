using Avalonia;
using Avalonia.Controls;

namespace Downloader.Desktop.Behaviors;

/// <summary>
/// Attached behavior that keeps a <see cref="NumericUpDown"/> from emitting a <c>null</c> value when the
/// user clears the text box. A cleared box otherwise sets <c>Value = null</c>, which a binding to a
/// non-nullable <c>int</c>/<c>long</c> setting can't convert — surfacing a "value cannot be null"
/// validation error in the view. With this on, an empty box snaps back to the control's Minimum.
/// Enabled globally via a style in App.axaml (<c>NumericUpDown</c>), so every numeric input is covered.
/// </summary>
public static class NumericCoerce
{
    public static readonly AttachedProperty<bool> EmptyToMinimumProperty =
        AvaloniaProperty.RegisterAttached<NumericUpDown, bool>(
            "EmptyToMinimum", typeof(NumericCoerce));

    public static void SetEmptyToMinimum(NumericUpDown element, bool value) =>
        element.SetValue(EmptyToMinimumProperty, value);

    public static bool GetEmptyToMinimum(NumericUpDown element) =>
        element.GetValue(EmptyToMinimumProperty);

    static NumericCoerce()
    {
        EmptyToMinimumProperty.Changed.AddClassHandler<NumericUpDown>((nud, e) =>
        {
            if (e.NewValue is true)
                nud.ValueChanged += OnValueChanged;
            else
                nud.ValueChanged -= OnValueChanged;
        });
    }

    private static void OnValueChanged(object sender, NumericUpDownValueChangedEventArgs e)
    {
        if (e.NewValue is null && sender is NumericUpDown nud)
            nud.Value = nud.Minimum;
    }
}

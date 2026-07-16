using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Downloader.Desktop.Views;

/// <summary>
/// Helpers for intercepting a paste into a URL box. A large list pasted into Avalonia's multi-line
/// <see cref="TextBox"/> lays out every line (no virtualization) and freezes the UI for seconds; the
/// paste is intercepted (tunnel phase) so the text is routed through the view model instead.
/// </summary>
internal static class UrlBoxPaste
{
    public static bool IsPasteGesture(KeyEventArgs e) =>
        (e.Key == Key.V && e.KeyModifiers.HasFlag(KeyModifiers.Control)) ||
        (e.Key == Key.Insert && e.KeyModifiers.HasFlag(KeyModifiers.Shift));

    public static async Task<string> ReadTextAsync(Visual anchor)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(anchor)?.Clipboard;
            if (clipboard == null)
                return null;
            return await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(clipboard);
        }
        catch
        {
            return null; // clipboard unavailable / denied — nothing to paste
        }
    }
}

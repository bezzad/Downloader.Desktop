using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Downloader.Desktop.Views;

/// <summary>
/// Reusable custom window title bar (app icon + title + window buttons) drawn inside the client area.
/// Windows that use it set <c>ExtendClientAreaToDecorationsHint="True"</c> so the OS still handles
/// resize/snap while we draw our own chrome — the cross-platform-reliable way to do custom chrome.
/// </summary>
public partial class TitleBar : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<TitleBar, string>(nameof(Title), "Downloader");

    public static readonly StyledProperty<bool> ShowMinMaxProperty =
        AvaloniaProperty.Register<TitleBar, bool>(nameof(ShowMinMax), true);

    public TitleBar()
    {
        InitializeComponent();
    }

    /// <summary>Text shown next to the app icon.</summary>
    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Show minimize/maximize buttons (true for the main window, false for dialogs).</summary>
    public bool ShowMinMax
    {
        get => GetValue(ShowMinMaxProperty);
        set => SetValue(ShowMinMaxProperty, value);
    }

    private Window Host => TopLevel.GetTopLevel(this) as Window;

    private void OnDrag(object sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            Host?.BeginMoveDrag(e);
    }

    private void OnMinimize(object sender, RoutedEventArgs e)
    {
        if (Host != null)
            Host.WindowState = WindowState.Minimized;
    }

    private void OnToggleMaximize(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void OnToggleMaximize(object sender, TappedEventArgs e) => ToggleMaximize();

    private void ToggleMaximize()
    {
        if (Host == null || !Host.CanResize)
            return;
        Host.WindowState = Host.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Host?.Close();
}

using System;
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

    public static readonly StyledProperty<bool> CenterTitleProperty =
        AvaloniaProperty.Register<TitleBar, bool>(nameof(CenterTitle));

    public static readonly StyledProperty<object> RightContentProperty =
        AvaloniaProperty.Register<TitleBar, object>(nameof(RightContent));

    public TitleBar()
    {
        InitializeComponent();
        LayoutUpdated += (_, _) => UpdateCenteredTitle();
    }

    /// <summary>Center the title across the whole bar (main window) instead of beside the icon.</summary>
    public bool CenterTitle
    {
        get => GetValue(CenterTitleProperty);
        set => SetValue(CenterTitleProperty, value);
    }

    /// <summary>Extra controls shown just left of the window buttons (main window only).</summary>
    public object RightContent
    {
        get => GetValue(RightContentProperty);
        set => SetValue(RightContentProperty, value);
    }

    /// <summary>Raised when a press on empty bar space starts a window drag (test seam).</summary>
    internal event Action DragStarted;

    /// <summary>
    /// A title centered in a bar of <paramref name="barWidth"/> fits when it touches neither the
    /// left block (icon) nor the right block (right content + window buttons).
    /// </summary>
    public static bool TitleFits(double barWidth, double titleWidth, double leftWidth, double rightWidth)
    {
        var start = (barWidth - titleWidth) / 2;
        return start >= leftWidth && start + titleWidth <= barWidth - rightWidth;
    }

    /// <summary>True when the centered title is currently drawn.</summary>
    internal bool IsCenteredTitleShown => CenteredTitle.Opacity > 0;

    private void UpdateCenteredTitle()
    {
        var show = CenterTitle && TitleFits(Bar.Bounds.Width, CenteredTitle.DesiredSize.Width,
            Left.Bounds.Right, Bar.Bounds.Width - Right.Bounds.X);
        // Opacity, not IsVisible: a hidden control is not measured, so its width would read 0.
        var opacity = show ? 1 : 0;
        if (CenteredTitle.Opacity != opacity)
            CenteredTitle.Opacity = opacity;
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
        {
            DragStarted?.Invoke();
            Host?.BeginMoveDrag(e);
        }
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

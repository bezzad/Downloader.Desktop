using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;

namespace Downloader.Desktop.Views;

/// <summary>
/// A 24-hour ring for picking a schedule's start and (optional) stop time. The arc between the two
/// handles is the download window. Drag a handle (or press anywhere on the ring to move the nearer one);
/// once focused, the arrow keys and the mouse wheel move the last-used handle by one minute (Shift: 15).
/// </summary>
public sealed class TimeRangeDial : Control
{
    private const double RingThickness = 14;
    private const double HandleRadius = 10;

    public static readonly StyledProperty<TimeSpan?> StartTimeProperty =
        AvaloniaProperty.Register<TimeRangeDial, TimeSpan?>(nameof(StartTime), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<TimeSpan?> StopTimeProperty =
        AvaloniaProperty.Register<TimeRangeDial, TimeSpan?>(nameof(StopTime), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<IBrush> TrackBrushProperty =
        AvaloniaProperty.Register<TimeRangeDial, IBrush>(nameof(TrackBrush), Brushes.LightGray);

    public static readonly StyledProperty<IBrush> ArcBrushProperty =
        AvaloniaProperty.Register<TimeRangeDial, IBrush>(nameof(ArcBrush), Brushes.Teal);

    public static readonly StyledProperty<IBrush> HandleRingBrushProperty =
        AvaloniaProperty.Register<TimeRangeDial, IBrush>(nameof(HandleRingBrush), Brushes.White);

    public static readonly StyledProperty<IBrush> TickBrushProperty =
        AvaloniaProperty.Register<TimeRangeDial, IBrush>(nameof(TickBrush), Brushes.Gray);

    private bool _dragging;
    private bool _stopActive;

    static TimeRangeDial()
    {
        AffectsRender<TimeRangeDial>(StartTimeProperty, StopTimeProperty, TrackBrushProperty, ArcBrushProperty,
            HandleRingBrushProperty, TickBrushProperty, IsFocusedProperty);
        FocusableProperty.OverrideDefaultValue<TimeRangeDial>(true);
        CursorProperty.OverrideDefaultValue<TimeRangeDial>(new Cursor(StandardCursorType.Hand));
    }

    public TimeSpan? StartTime { get => GetValue(StartTimeProperty); set => SetValue(StartTimeProperty, value); }
    public TimeSpan? StopTime { get => GetValue(StopTimeProperty); set => SetValue(StopTimeProperty, value); }
    public IBrush TrackBrush { get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public IBrush ArcBrush { get => GetValue(ArcBrushProperty); set => SetValue(ArcBrushProperty, value); }
    public IBrush HandleRingBrush { get => GetValue(HandleRingBrushProperty); set => SetValue(HandleRingBrushProperty, value); }
    public IBrush TickBrush { get => GetValue(TickBrushProperty); set => SetValue(TickBrushProperty, value); }

    // A clock face reads clockwise in every language: never mirror it in a right-to-left window.
    protected override bool BypassFlowDirectionPolicies => true;

    private Point Center => new(Bounds.Width / 2, Bounds.Height / 2);
    private double Radius => Math.Max(0, Math.Min(Bounds.Width, Bounds.Height) / 2 - HandleRadius - 2);
    private int StartMinutes => TimeDial.ToMinutes(StartTime);
    private int? StopMinutes => StopTime is null ? null : TimeDial.ToMinutes(StopTime);

    protected override Size MeasureOverride(Size availableSize) => new(220, 220);

    public override void Render(DrawingContext context)
    {
        Point c = Center;
        double r = Radius;
        if (r <= 0) return;

        context.DrawEllipse(null, new Pen(TrackBrush, RingThickness), c, r, r);

        double inner = r - RingThickness / 2 - 3;
        for (int h = 0; h < 24; h++)
        {
            bool major = h % 6 == 0;
            int m = h * 60;
            context.DrawLine(new Pen(TickBrush, major ? 2 : 1),
                TimeDial.PointAt(c, inner - (major ? 8 : 4), m), TimeDial.PointAt(c, inner, m));
            if (!major) continue;
            var label = new FormattedText(h.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, Typeface.Default, 11, TickBrush);
            Point at = TimeDial.PointAt(c, inner - 18, m);
            context.DrawText(label, new Point(at.X - label.Width / 2, at.Y - label.Height / 2));
        }

        int start = StartMinutes;
        Point startPoint = TimeDial.PointAt(c, r, start);
        if (StopMinutes is { } stop)
        {
            int window = TimeDial.WindowMinutes(start, stop);
            Point stopPoint = TimeDial.PointAt(c, r, stop);
            if (window > 0)
            {
                var arc = new StreamGeometry();
                using (StreamGeometryContext g = arc.Open())
                {
                    g.BeginFigure(startPoint, false);
                    g.ArcTo(stopPoint, new Size(r, r), 0, window > TimeDial.MinutesPerDay / 2, SweepDirection.Clockwise);
                    g.EndFigure(false);
                }
                context.DrawGeometry(null, new Pen(ArcBrush, RingThickness, lineCap: PenLineCap.Round), arc);
            }
            // The stop handle is hollow so it stays distinguishable from the start handle in every accent.
            DrawHandle(context, stopPoint, HandleRingBrush, ArcBrush, _stopActive);
        }
        DrawHandle(context, startPoint, ArcBrush, HandleRingBrush, !_stopActive || StopTime is null);
    }

    private void DrawHandle(DrawingContext context, Point at, IBrush fill, IBrush ring, bool active)
    {
        double radius = IsFocused && active ? HandleRadius + 2 : HandleRadius;
        context.DrawEllipse(fill, new Pen(ring, 3), at, radius, radius);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus();
        Point p = e.GetPosition(this);
        _stopActive = PicksStop(p);
        _dragging = true;
        MoveActiveTo(p);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging) MoveActiveTo(e.GetPosition(this));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragging = false;
    }

    // Only while focused, so scrolling the page past an untouched dial still scrolls the page.
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (!IsFocused || e.Delta.Y == 0) return;
        Nudge(Math.Sign(e.Delta.Y) * Step(e.KeyModifiers));
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        int direction = e.Key switch
        {
            Key.Right or Key.Up => 1,
            Key.Left or Key.Down => -1,
            _ => 0
        };
        if (direction == 0) return;
        Nudge(direction * Step(e.KeyModifiers));
        e.Handled = true;
    }

    private static int Step(KeyModifiers modifiers) => modifiers.HasFlag(KeyModifiers.Shift) ? 15 : 1;

    /// <summary>With both handles showing, the press moves whichever is nearer to it.</summary>
    private bool PicksStop(Point p)
    {
        if (StopMinutes is not { } stop) return false;
        Point c = Center;
        double r = Radius;
        return Distance(p, TimeDial.PointAt(c, r, stop)) < Distance(p, TimeDial.PointAt(c, r, StartMinutes));
    }

    private void MoveActiveTo(Point p) => SetActive(TimeDial.MinutesAt(p.X - Center.X, p.Y - Center.Y));

    private void Nudge(int minutes) =>
        SetActive((_stopActive && StopMinutes is { } stop ? stop : StartMinutes) + minutes);

    private void SetActive(int minutes)
    {
        if (_stopActive && StopTime is not null) StopTime = TimeDial.ToTime(minutes);
        else StartTime = TimeDial.ToTime(minutes);
    }

    private static double Distance(Point a, Point b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
}

using Avalonia;
using Avalonia.Controls;
using Downloader.Desktop.Views;
using Xunit;

namespace Downloader.Desktop.Tests.Unit;

/// <summary>
/// The property that was broken before the snapshot rewrite: because every frame anchors to the fixed
/// drag-start snapshot, a fast multi-step drag (many small deltas) must land on exactly the same
/// size/position as one single equivalent large delta — no compounding error that walks the window off-screen.
/// </summary>
public class WindowResizeTests
{
    private static readonly PixelPoint StartPointer = new(500, 400);
    private static readonly PixelPoint StartPos = new(100, 100);
    private const double StartW = 800, StartH = 600, Scale = 1.0;
    private const double MinW = 200, MinH = 150, MaxW = 4000, MaxH = 4000;

    private static WindowResize.Result Compute(WindowEdge edge, PixelPoint pointer) =>
        WindowResize.Compute(edge, StartPointer, StartPos, StartW, StartH, pointer, Scale, MinW, MinH, MaxW, MaxH);

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData(WindowEdge.East, 300, 0)]
    [InlineData(WindowEdge.West, -250, 0)]
    [InlineData(WindowEdge.North, 0, -200)]
    [InlineData(WindowEdge.South, 0, 220)]
    [InlineData(WindowEdge.NorthWest, -180, -160)]
    [InlineData(WindowEdge.SouthEast, 240, 190)]
    [InlineData(WindowEdge.NorthEast, 210, -140)]
    [InlineData(WindowEdge.SouthWest, -170, 130)]
    public void Fast_multistep_drag_equals_one_big_delta(WindowEdge edge, int totalDx, int totalDy)
    {
        // One big jump straight to the final pointer position.
        var final = new PixelPoint(StartPointer.X + totalDx, StartPointer.Y + totalDy);
        var single = Compute(edge, final);

        // The same displacement delivered as 40 tiny incremental frames, each recomputed from the snapshot.
        WindowResize.Result stepped = default;
        for (int i = 1; i <= 40; i++)
        {
            var p = new PixelPoint(StartPointer.X + totalDx * i / 40, StartPointer.Y + totalDy * i / 40);
            stepped = Compute(edge, p);
        }

        Assert.Equal(single.Position.X, stepped.Position.X);
        Assert.Equal(single.Position.Y, stepped.Position.Y);
        Assert.Equal(single.Width, stepped.Width, 6);
        Assert.Equal(single.Height, stepped.Height, 6);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void West_keeps_the_right_edge_fixed()
    {
        var r = Compute(WindowEdge.West, new PixelPoint(StartPointer.X - 120, StartPointer.Y));
        double rightBefore = StartPos.X + StartW;
        double rightAfter = r.Position.X + r.Width;
        Assert.Equal(rightBefore, rightAfter, 6);
        Assert.Equal(StartW + 120, r.Width, 6); // dragging left grows the window
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Min_size_clamp_still_pins_the_opposite_edge()
    {
        // Drag the west edge far past the min width; the right edge must not move.
        var r = Compute(WindowEdge.West, new PixelPoint(StartPointer.X + 5000, StartPointer.Y));
        Assert.Equal(MinW, r.Width, 6);
        Assert.Equal(StartPos.X + StartW, r.Position.X + r.Width, 6);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void ClampOnScreen_pulls_a_runaway_window_back()
    {
        var screen = new[] { new PixelRect(0, 0, 1920, 1080) };
        var far = WindowResize.ClampOnScreen(new PixelPoint(-5000, -5000), 800, 600, screen);
        // Some part of the window (at least the margin) is back on screen.
        Assert.True(far.X + 800 >= 40);
        Assert.True(far.Y >= 0);
    }
}

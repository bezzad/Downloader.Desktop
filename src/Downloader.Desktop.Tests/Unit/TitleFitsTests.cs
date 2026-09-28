using Downloader.Desktop.Views;
using Xunit;

namespace Downloader.Desktop.Tests.Unit;

public class TitleFitsTests
{
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_short_title_in_a_wide_bar_fits() =>
        Assert.True(TitleBar.TitleFits(1000, 100, 50, 300)); // title 450..550, right from 700

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_title_is_hidden_when_the_right_side_reaches_it() =>
        Assert.False(TitleBar.TitleFits(840, 100, 50, 400)); // title 370..470, right from 440

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_title_is_hidden_when_the_left_side_reaches_it() =>
        Assert.False(TitleBar.TitleFits(200, 100, 60, 0)); // title 50..150, icon to 60

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Touching_exactly_at_the_edge_still_fits() =>
        Assert.True(TitleBar.TitleFits(800, 100, 350, 350)); // title 350..450
}

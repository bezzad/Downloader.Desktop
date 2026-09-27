using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Downloader.Desktop.Views;
using Xunit;

namespace Downloader.Desktop.Tests.UI;

/// <summary>
/// The downloads grid drops Time left, then Queue, then Type when it is too narrow for every column
/// (the category sidebar open on a small window), instead of crushing the fixed columns into each other.
/// Every column together (with 2 queues) is 982px; the view has a 5px margin on each side.
/// </summary>
public class DownloadsColumnFitTests
{
    private static (bool type, bool queue, bool timeLeft) VisibleAt(double windowWidth, int queues = 2)
    {
        Localizer.Instance.Load("en");
        var manager = new DownloadManager();
        manager.Initialize(Config.New());
        for (int i = 1; i < queues; i++)
            manager.AddQueue("Media");
        var page = new DownloadsView { DataContext = new DownloadsViewModel(manager) };
        new Window { Content = page, Width = windowWidth, Height = 500 }.Show();
        for (int i = 0; i < 5; i++)
            Dispatcher.UIThread.RunJobs();

        var grid = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(page).OfType<DataGrid>().First();
        bool Visible(string member) => grid.Columns.First(c => c.SortMemberPath == member).IsVisible;
        return (Visible("CategoryOrder"), Visible("QueueName"), Visible("TimeLeftText"));
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_wide_window_shows_every_column() =>
        Assert.Equal((true, true, true), VisibleAt(1100));

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Time_left_goes_first() =>
        Assert.Equal((true, true, false), VisibleAt(985));

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_sidebar_on_a_1000px_window_also_drops_the_queue_but_keeps_the_type() =>
        Assert.Equal((true, false, false), VisibleAt(800));

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Narrower_still_the_type_goes_last() =>
        Assert.Equal((false, false, false), VisibleAt(700));

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void With_one_queue_the_queue_column_stays_hidden_however_wide() =>
        Assert.Equal((true, false, true), VisibleAt(1400, queues: 1));

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Columns_are_dropped_in_order_only_until_the_rest_fits()
    {
        double[] optional = { 90, 110, 70 };
        Assert.Equal(0, ColumnFit.CountToHide(1000, 982, optional));
        Assert.Equal(1, ColumnFit.CountToHide(975, 982, optional));
        Assert.Equal(2, ColumnFit.CountToHide(790, 982, optional));
        Assert.Equal(3, ColumnFit.CountToHide(100, 982, optional));   // never more than there are
    }
}

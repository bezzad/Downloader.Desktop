using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Downloader.Desktop.ViewModels;

namespace Downloader.Desktop.Views;

public partial class QueuesView : UserControl
{
    private QueuesViewModel _vm;

    public QueuesView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => HookViewModel();
        HookViewModel();
    }

    private void HookViewModel()
    {
        if (_vm != null)
            _vm.QueueAdded -= ScrollToNewQueue;
        _vm = DataContext as QueuesViewModel;
        if (_vm != null)
            _vm.QueueAdded += ScrollToNewQueue;
    }

    /// <summary>A new queue is appended at the end of the list, which with many queues is below the fold —
    /// scroll to it so the user sees what their click did. Posted so the card is laid out first.</summary>
    private void ScrollToNewQueue()
    {
        Dispatcher.UIThread.Post(() =>
        {
            PageScroller.UpdateLayout();
            // Offset is clamped to the extent, so "past the end" simply lands at the bottom.
            PageScroller.Offset = new Vector(PageScroller.Offset.X, PageScroller.Extent.Height);
        }, DispatcherPriority.Background);
    }

    /// <summary>Enter in the inline queue-name box creates the queue; Esc cancels it.</summary>
    private void OnQueueNameKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not QueuesViewModel vm)
            return;
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            vm.ConfirmNewQueue();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            vm.CancelNewQueueCommand.Execute(null);
        }
    }
}

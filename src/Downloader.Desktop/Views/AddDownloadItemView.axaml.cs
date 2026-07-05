using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Downloader.Desktop.ViewModels;

namespace Downloader.Desktop.Views;

public partial class AddDownloadItemView : Window
{
    public AddDownloadItemView()
    {
        InitializeComponent();

        // Intercept Enter/Tab in the TUNNEL phase so the clipboard suggestion is accepted BEFORE the
        // multi-line TextBox handles Enter. The TextBox inserts a newline in its own bubble-phase key
        // handler and marks the event handled, so a bubble-phase handler here never fires (that was the
        // "Enter just adds a new line" bug). Tunnel runs root→target, before the TextBox's bubble handler.
        UrlBox.AddHandler(KeyDownEvent, OnUrlBoxKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>Enter in the inline queue-name box confirms the new queue; Esc cancels the row.</summary>
    private void OnQueueNameKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not AddDownloadItemViewModel vm)
            return;
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            vm.ConfirmAddQueue();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            vm.CancelAddQueueCommand.Execute(null);
        }
    }

    /// <summary>While the links box is empty and a clipboard suggestion is showing, Enter or Tab accepts it
    /// (populating the real box). Otherwise keep normal typing behaviour (Enter/Shift+Enter insert newlines
    /// in this multi-line box).</summary>
    private void OnUrlBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not AddDownloadItemViewModel vm || !vm.ShowClipboardSuggestion)
            return;
        if (e.Key != Key.Enter && e.Key != Key.Tab)
            return;

        e.Handled = true;
        vm.AcceptClipboardSuggestion();
        UrlBox.CaretIndex = UrlBox.Text?.Length ?? 0;
    }
}

using Avalonia.Controls;
using Avalonia.Input;
using Downloader.Desktop.ViewModels;

namespace Downloader.Desktop.Views;

public partial class AddDownloadItemView : Window
{
    public AddDownloadItemView()
    {
        InitializeComponent();
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
        if (sender is TextBox box)
            box.CaretIndex = box.Text?.Length ?? 0;
    }
}
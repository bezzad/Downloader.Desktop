using Avalonia.Controls;
using Avalonia.Input;

namespace Downloader.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnTitleBarPointerPressed(object sender, PointerPressedEventArgs e)
    {
        // This will start the drag for moving the window
        BeginMoveDrag(e);
    }

    /// <summary>Enter adds the link(s); Shift+Enter inserts a newline so several URLs can be entered.</summary>
    private void OnUrlBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || (e.KeyModifiers & KeyModifiers.Shift) != 0)
            return;

        e.Handled = true; // don't insert a newline
        if (DataContext is ViewModels.MainViewModel vm && vm.AddDownloadItemCommand.CanExecute(null))
            vm.AddDownloadItemCommand.Execute(null);
    }
}

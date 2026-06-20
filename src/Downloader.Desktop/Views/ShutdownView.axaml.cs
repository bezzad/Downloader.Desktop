using Avalonia.Controls;
using Avalonia.Input;
using Downloader.Desktop.ViewModels;

namespace Downloader.Desktop.Views;

public partial class ShutdownView : Window
{
    public ShutdownView()
    {
        InitializeComponent();
    }

    /// <summary>Esc cancels the shutdown (same as the Cancel button).</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is ShutdownViewModel vm)
        {
            e.Handled = true;
            vm.CancelCommand.Execute(null);
            return;
        }

        base.OnKeyDown(e);
    }
}

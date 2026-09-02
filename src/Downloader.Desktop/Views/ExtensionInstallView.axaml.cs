using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Downloader.Desktop.ViewModels;

namespace Downloader.Desktop.Views;

public partial class ExtensionInstallView : Window
{
    public ExtensionInstallView()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    /// <summary>Esc closes the dialog (there is no native chrome to do it).</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
            return;
        }

        base.OnKeyDown(e);
    }

    /// <summary>Drops the language subscription — a dialog that leaks it keeps its VM alive for the life
    /// of the process.</summary>
    protected override void OnClosed(System.EventArgs e)
    {
        (DataContext as ExtensionInstallViewModel)?.Detach();
        base.OnClosed(e);
    }
}

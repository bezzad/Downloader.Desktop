using Avalonia.Controls;
using Avalonia.Input;

namespace Downloader.Desktop.Views;

public partial class DonateView : Window
{
    public DonateView()
    {
        InitializeComponent();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
        base.OnKeyDown(e);
    }
}

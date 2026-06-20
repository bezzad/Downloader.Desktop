using Avalonia.Controls;
using Avalonia.Input;

namespace Downloader.Desktop.Views;

public partial class AboutView : Window
{
    public AboutView()
    {
        InitializeComponent();
    }

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
}

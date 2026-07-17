using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Downloader.Desktop.Views;

/// <summary>Small reusable Yes/No confirmation modal (returns true/false via ShowDialog).</summary>
public partial class ConfirmView : Window
{
    public ConfirmView()
    {
        InitializeComponent();
    }

    private void OnYes(object sender, RoutedEventArgs e) => Close(true);
    private void OnNo(object sender, RoutedEventArgs e) => Close(false);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close(false);
        }
        base.OnKeyDown(e);
    }
}

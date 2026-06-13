using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Downloader.Desktop.Views;

public partial class DownloadDetailsView : Window
{
    public DownloadDetailsView()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}

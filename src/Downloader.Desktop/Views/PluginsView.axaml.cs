using Avalonia.Controls;
using Downloader.Desktop.ViewModels;

namespace Downloader.Desktop.Views;

public partial class PluginsView : UserControl
{
    public PluginsView()
    {
        InitializeComponent();
        // Fetch the optional-plugin catalog when the section is actually shown (keeps the ctor network-free
        // for tests/design-time). FetchAsync is failure-tolerant, so nothing to guard beyond a null VM.
        Loaded += (_, _) =>
        {
            if (DataContext is PluginsViewModel vm)
                _ = vm.RefreshCatalogAsync();
        };
    }
}

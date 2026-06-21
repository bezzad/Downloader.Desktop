using Avalonia.Controls;
using Avalonia.Input;
using Downloader.Desktop.ViewModels;

namespace Downloader.Desktop.Views;

public partial class UpdatePromptView : Window
{
    public UpdatePromptView()
    {
        InitializeComponent();
    }

    /// <summary>Esc dismisses the prompt (same as "Later").</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is UpdatePromptViewModel vm)
        {
            e.Handled = true;
            vm.LaterCommand.Execute(null);
            return;
        }

        base.OnKeyDown(e);
    }
}

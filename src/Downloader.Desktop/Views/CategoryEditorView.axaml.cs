using Avalonia.Controls;
using Avalonia.Input;

namespace Downloader.Desktop.Views;

public partial class CategoryEditorView : Window
{
    public CategoryEditorView()
    {
        InitializeComponent();
    }

    /// <summary>Esc cancels — there is no native chrome to do it (see DownloadDetailsView).</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close(null);
            return;
        }

        base.OnKeyDown(e);
    }
}

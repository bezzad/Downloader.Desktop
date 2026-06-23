using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Downloader.Desktop.Views;

public partial class PageDialogView : Window
{
    public static readonly StyledProperty<object> PageProperty =
        AvaloniaProperty.Register<PageDialogView, object>(nameof(Page));

    public static readonly StyledProperty<string> PageTitleProperty =
        AvaloniaProperty.Register<PageDialogView, string>(nameof(PageTitle));

    /// <summary>The hosted page view model (rendered via the window's DataTemplates).</summary>
    public object Page
    {
        get => GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    public string PageTitle
    {
        get => GetValue(PageTitleProperty);
        set => SetValue(PageTitleProperty, value);
    }

    public PageDialogView()
    {
        InitializeComponent();
    }

    public PageDialogView(object page, string title) : this()
    {
        Page = page;
        PageTitle = title;
        Title = title;
    }

    /// <summary>Esc closes the dialog (no native chrome to do it).</summary>
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

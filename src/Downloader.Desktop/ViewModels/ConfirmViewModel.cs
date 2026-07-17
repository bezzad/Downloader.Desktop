namespace Downloader.Desktop.ViewModels;

/// <summary>Data for the reusable Yes/No confirmation modal.</summary>
public class ConfirmViewModel : ViewModelBase
{
    public ConfirmViewModel(string title, string message)
    {
        Title = title;
        Message = message;
    }

    public string Title { get; }
    public string Message { get; }
}

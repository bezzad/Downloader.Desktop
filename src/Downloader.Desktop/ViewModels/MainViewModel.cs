namespace Downloader.Desktop.ViewModels;

public class MainViewModel : ViewModelBase
{
    public DownloadsViewModel Downloads { get; } = new DownloadsViewModel();
    public HeadViewModel Head { get; } = new HeadViewModel();

}

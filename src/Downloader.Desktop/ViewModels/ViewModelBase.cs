using Avalonia.Controls;
using ReactiveUI;

namespace Downloader.Desktop.ViewModels;

public class ViewModelBase : ReactiveObject
{
    public Window View { get; set; }
}

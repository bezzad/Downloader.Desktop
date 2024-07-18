using ReactiveUI;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Downloader.Desktop.ViewModels;

public class HeadViewModel : ViewModelBase
{
    public HeadViewModel()
    {
        AddUrlCommand = ReactiveCommand.CreateFromTask(OpenDialogBox);
    }

    public ICommand AddUrlCommand { get; }


    public async Task OpenDialogBox()
    {

    }
}

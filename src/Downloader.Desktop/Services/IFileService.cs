using Downloader.Desktop.Models;
using System.Threading.Tasks;

namespace Downloader.Desktop.Services;

public interface IFileService 
{
    public Task SaveToFileAsync(Config itemToSave);
    public Task<Config> LoadFromFileAsync();
}
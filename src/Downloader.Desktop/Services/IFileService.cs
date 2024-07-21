using Avalonia.Platform.Storage;
using Downloader.Desktop.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Downloader.Desktop.Services;

public interface IFileService
{
    public Task<IStorageFile?> OpenFileAsync();
    public Task<IStorageFile?> SaveFileAsync();
    public Task SaveToFileAsync(IEnumerable<DownloadItem> itemsToSave);
    public Task<IEnumerable<DownloadItem>?> LoadFromFileAsync();
}

using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Downloader.Desktop.Services;

/// <summary>
/// A helper class to manage dialogs via extension methods. Add more on your own
/// </summary>
public static class DialogHelper
{
    /// <summary>
    /// Shows an open file dialog for a registered context, most likely a ViewModel
    /// </summary>
    /// <param name="context">The context</param>
    /// <param name="title">The dialog title or a default is null</param>
    /// <param name="selectMany">Is selecting many files allowed?</param>
    /// <returns>An array of file names</returns>
    /// <exception cref="ArgumentNullException">if context was null</exception>
    public static async Task<IEnumerable<string>?> OpenFileDialogAsync(this object? context, string? title = null, bool selectMany = true)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        // lookup the TopLevel for the context
        var topLevel = DialogManager.GetTopLevelForContext(context);

        if (topLevel != null)
        {
            // Open the file dialog
            var storageFiles = await topLevel.StorageProvider.OpenFilePickerAsync(
                            new FilePickerOpenOptions()
                            {
                                AllowMultiple = selectMany,
                                Title = title ?? "Select any file(s)"
                            });

            // return the result
            return storageFiles.Select(s => s.Name);
        }
        return null;
    }

}

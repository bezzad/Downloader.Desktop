using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using ReactiveUI;

namespace Downloader.Desktop.ViewModels;

/// <summary>
/// One entry of a "which category?" picker: a category, or the automatic option that hands the
/// decision back to the app. Shared by the row's right-click menu, the Add dialog and the Details
/// window so all three offer the same list in the same order.
/// </summary>
public sealed class CategoryChoice
{
    public CategoryChoice(string id, string label, Action<string> pick = null)
    {
        Id = id;
        Label = label;
        Command = pick is null ? null : ReactiveCommand.Create(() => pick(id));
    }

    /// <summary>The category's id, or null for "work it out".</summary>
    public string Id { get; }

    /// <summary>What the entry reads as. For a category this is its name verbatim; only the
    /// automatic entry is interface text.</summary>
    public string Label { get; }

    /// <summary>Applies this choice. Null when the picker binds selection instead (a combo box).</summary>
    public ICommand Command { get; }

    /// <summary>
    /// The full list for a download: the automatic option first — named after what the app would
    /// pick, so choosing it is an informed decision — then every category in the user's order.
    /// </summary>
    public static List<CategoryChoice> For(CategoryService categories, DownloadCategory detected,
        Action<string> pick = null)
    {
        var choices = new List<CategoryChoice>
        {
            new(null, string.Format(Localizer.Instance["Cat_Automatic"], detected?.Name ?? string.Empty), pick)
        };

        if (categories != null)
            choices.AddRange(categories.Categories.Select(c => new CategoryChoice(c.Id, c.Name, pick)));

        return choices;
    }
}

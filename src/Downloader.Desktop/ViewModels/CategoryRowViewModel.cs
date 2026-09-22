using System;
using System.Windows.Input;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using ReactiveUI;

namespace Downloader.Desktop.ViewModels;

/// <summary>
/// One row of the category sidebar: a category, or the "All" entry that clears the category filter
/// without touching the status filter or the search box.
/// </summary>
public class CategoryRowViewModel : ViewModelBase
{
    private int _count;
    private bool _isSelected;

    public CategoryRowViewModel(DownloadCategory category, Action<string> select, Action<CategoryRowViewModel> edit,
        Action<CategoryRowViewModel, int> move, Action<CategoryRowViewModel> delete = null)
    {
        Category = category;
        SelectCommand = ReactiveCommand.Create(() => select?.Invoke(category?.Id));
        EditCommand = ReactiveCommand.Create(() => edit?.Invoke(this));
        MoveUpCommand = ReactiveCommand.Create(() => move?.Invoke(this, -1));
        MoveDownCommand = ReactiveCommand.Create(() => move?.Invoke(this, 1));
        DeleteCommand = ReactiveCommand.Create(() => delete?.Invoke(this));
    }

    /// <summary>The category, or null for the "All" row.</summary>
    public DownloadCategory Category { get; }

    /// <summary>True for the "All" row, which is not a category and cannot be edited or moved.</summary>
    public bool IsAll => Category is null;

    public string Id => Category?.Id;

    /// <summary>The name exactly as stored — a category's name is the user's data, never translated.
    /// Only the "All" row's label is interface text.</summary>
    public string Name => Category?.Name ?? Localizer.Instance["Cat_All"];

    public string Icon => Category?.Icon ?? "all";

    public string Color => Category?.Color;

    /// <summary>How many downloads are in this category under the filters currently in force.</summary>
    public int Count
    {
        get => _count;
        set
        {
            if (_count == value)
                return;

            _count = value;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(IsEmpty));
        }
    }

    /// <summary>An empty category is shown de-emphasized rather than hidden: hiding and re-showing
    /// rows as downloads arrive would make the sidebar twitch under the pointer.</summary>
    public bool IsEmpty => _count == 0;

    public bool IsSelected
    {
        get => _isSelected;
        set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }

    /// <summary>Built-in categories can be edited and reordered, but never deleted.</summary>
    public bool CanEdit => !IsAll;

    /// <summary>Only a category the user created can be deleted — <see cref="CategoryService.Remove"/>
    /// refuses a built-in one, so offering the item for one would be a button that does nothing.</summary>
    public bool CanDelete => !IsAll && Category?.IsBuiltIn == false;

    public ICommand SelectCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand DeleteCommand { get; }

    /// <summary>Re-reads everything that comes off the category itself (after an edit).</summary>
    public void RaiseCategoryChanged()
    {
        this.RaisePropertyChanged(nameof(Name));
        this.RaisePropertyChanged(nameof(Icon));
        this.RaisePropertyChanged(nameof(Color));
    }
}

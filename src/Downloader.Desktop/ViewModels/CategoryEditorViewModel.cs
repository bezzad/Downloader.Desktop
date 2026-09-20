using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Avalonia.Controls;
using Downloader.Desktop.Converters;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using ReactiveUI;

namespace Downloader.Desktop.ViewModels;

/// <summary>
/// The category editor: name, icon, color and the extensions the category claims. Works on a
/// <see cref="DownloadCategory.Clone"/> so cancelling leaves the live list untouched.
/// </summary>
public class CategoryEditorViewModel : ViewModelBase
{
    private readonly CategoryService _categories;
    private readonly string _editingId;
    private string _name;
    private string _icon;
    private string _color;
    private string _extensions;
    private string _error;

    /// <summary>Design-time constructor.</summary>
    public CategoryEditorViewModel() : this(new CategoryService(), null)
    {
    }

    /// <param name="categories">The live list, consulted for the duplicate-name rule.</param>
    /// <param name="editing">The category being edited, or null to create a new one.</param>
    public CategoryEditorViewModel(CategoryService categories, DownloadCategory editing)
    {
        _categories = categories;
        _editingId = editing?.Id;
        IsNew = editing is null;
        IsBuiltIn = editing?.IsBuiltIn == true;
        Original = editing;

        _name = editing?.Name ?? string.Empty;
        _icon = editing?.Icon ?? IconChoices.First();
        _color = editing?.Color ?? ColorChoices.First();
        _extensions = string.Join(", ", editing?.Extensions ?? new List<string>());

        SaveCommand = ReactiveCommand.Create(Save);
        CancelCommand = ReactiveCommand.Create(() => Close(null));
        DeleteCommand = ReactiveCommand.Create(Delete);
    }

    /// <summary>The category being edited, or null when creating one.</summary>
    public DownloadCategory Original { get; }

    public bool IsNew { get; }

    /// <summary>Built-in categories can be renamed, recolored, re-iconed and re-scoped, but never
    /// deleted — the app must always be able to resolve a download to something.</summary>
    public bool IsBuiltIn { get; }

    public bool CanDelete => !IsNew && !IsBuiltIn;

    public string Title => Localizer.Instance[IsNew ? "Cat_Add" : "Cat_Edit"];

    /// <summary>The icons a category can wear. Keys, not paths — that is what survives an export to
    /// another machine, and what an app that does not know one can fall back from.</summary>
    public static IReadOnlyList<string> IconChoices { get; } = new[]
    {
        "video", "audio", "image", "document", "archive", "app", "disc", "file", "all"
    };

    /// <summary>A restrained palette that reads on both the light and the dark theme.</summary>
    public static IReadOnlyList<string> ColorChoices { get; } = new[]
    {
        "#4F8DF5", "#2DBED6", "#35B87F", "#E0A23C", "#B07CE8", "#E2705A", "#7A8CA6", "#8A93A0"
    };

    public ObservableCollection<CategoryIconChoice> Icons { get; } =
        new(IconChoices.Select(k => new CategoryIconChoice(k)));

    public ObservableCollection<CategoryColorChoice> Colors { get; } =
        new(ColorChoices.Select(c => new CategoryColorChoice(c)));

    public string Name
    {
        get => _name;
        set
        {
            this.RaiseAndSetIfChanged(ref _name, value);
            Error = null;
        }
    }

    public string Icon
    {
        get => _icon;
        set => this.RaiseAndSetIfChanged(ref _icon, value);
    }

    public string Color
    {
        get => _color;
        set => this.RaiseAndSetIfChanged(ref _color, value);
    }

    /// <summary>Extensions as typed, e.g. "mp4, mkv, avi".</summary>
    public string Extensions
    {
        get => _extensions;
        set => this.RaiseAndSetIfChanged(ref _extensions, value);
    }

    /// <summary>Why the last save attempt was refused, or null.</summary>
    public string Error
    {
        get => _error;
        set
        {
            this.RaiseAndSetIfChanged(ref _error, value);
            this.RaisePropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(_error);

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand DeleteCommand { get; }

    /// <summary>
    /// Validates and, if the input is usable, closes the dialog returning the edited copy.
    /// Exposed for tests, which drive the rules without a window.
    /// </summary>
    internal DownloadCategory Build()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            Error = Localizer.Instance["Cat_NameRequired"];
            return null;
        }

        if (!_categories.IsNameAvailable(Name, _editingId))
        {
            Error = Localizer.Instance["Cat_NameDuplicate"];
            return null;
        }

        var edited = Original?.Clone() ?? new DownloadCategory { Id = CategoryService.NewId() };
        edited.Name = Name.Trim();
        edited.Icon = Icon;
        edited.Color = Color;
        edited.Extensions = CategoryService.ParseExtensions(Extensions);
        return edited;
    }

    private void Save()
    {
        var edited = Build();
        if (edited != null)
            Close(edited);
    }

    private void Delete()
    {
        if (!CanDelete)
            return;

        _categories.Remove(_editingId);
        Close(null);
    }

    private void Close(DownloadCategory result) => (View as Window)?.Close(result);
}

/// <summary>One entry of the icon picker.</summary>
public sealed class CategoryIconChoice
{
    public CategoryIconChoice(string key) => Key = key;

    public string Key { get; }

    public Avalonia.Media.Geometry Geometry => FileKindToIconConverter.GetIcon(Key);
}

/// <summary>One entry of the color picker.</summary>
public sealed class CategoryColorChoice
{
    public CategoryColorChoice(string hex) => Hex = hex;

    public string Hex { get; }

    public Avalonia.Media.IBrush Brush => HexToBrushConverter.BrushFor(Hex);
}

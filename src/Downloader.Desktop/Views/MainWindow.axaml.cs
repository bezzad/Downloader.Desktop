using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Downloader.Desktop.ViewModels;

namespace Downloader.Desktop.Views;

public partial class MainWindow : Window
{
    private readonly PageViewCache _pages = new();
    private ViewModels.MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();

        // Intercept paste (tunnel) on the top box: a large list would freeze the UI if the multi-line
        // TextBox laid it out. Small pastes insert normally; a large paste opens the Add dialog directly.
        TopUrlBox.AddHandler(KeyDownEvent, OnTopUrlBoxPaste, RoutingStrategies.Tunnel);

        // Page views are cached + reused across navigation (see PageViewCache) — swap the cached
        // control on CurrentPage changes instead of letting a DataTemplate rebuild the page.
        DataContextChanged += (_, _) =>
        {
            if (_vm != null)
                _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm = DataContext as ViewModels.MainViewModel;
            if (_vm != null)
            {
                _vm.PropertyChanged += OnVmPropertyChanged;
                PageHost.Content = _pages.GetView(_vm.CurrentPage);
            }
        };
    }

    private void OnVmPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.MainViewModel.CurrentPage))
            PageHost.Content = _pages.GetView(_vm?.CurrentPage);
    }

    private async void OnTopUrlBoxPaste(object sender, KeyEventArgs e)
    {
        if (!UrlBoxPaste.IsPasteGesture(e) || DataContext is not ViewModels.MainViewModel vm)
            return;

        e.Handled = true;
        var text = await UrlBoxPaste.ReadTextAsync(this);
        if (string.IsNullOrEmpty(text))
            return;

        // Large paste → straight into the Add dialog (bulk mode); leave the top box empty so it never
        // has to lay out thousands of lines.
        if (ViewModels.AddDownloadItemViewModel.CountUrls(text) > ViewModels.AddDownloadItemViewModel.BulkPreviewThreshold)
        {
            await vm.OpenAddWithText(text);
            return;
        }

        var current = TopUrlBox.Text ?? string.Empty;
        var caret = Math.Clamp(TopUrlBox.CaretIndex, 0, current.Length);
        var merged = current.Substring(0, caret) + text + current.Substring(caret);
        vm.DownloadUrl = merged;
        TopUrlBox.CaretIndex = Math.Min(caret + text.Length, merged.Length);
    }

    private void OnTitleBarPointerPressed(object sender, PointerPressedEventArgs e)
    {
        // This will start the drag for moving the window
        BeginMoveDrag(e);
    }

    /// <summary>Enter adds the link(s); Shift+Enter inserts a newline so several URLs can be entered.</summary>
    private void OnUrlBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || (e.KeyModifiers & KeyModifiers.Shift) != 0)
            return;

        e.Handled = true; // don't insert a newline
        if (DataContext is ViewModels.MainViewModel vm && vm.AddDownloadItemCommand.CanExecute(null))
            vm.AddDownloadItemCommand.Execute(null);
    }

    // --- Sidebar: drag a category by its grip to reorder ---
    // Same manual pointer-capture approach as the downloads grid (the OS DragDrop session shows no
    // visual on X11): press captures, move highlights the row under the pointer, release drops.

    private Button _dragCategoryRow;
    private Button _categoryDropRow;

    private void OnCategoryGripPressed(object sender, PointerPressedEventArgs e)
    {
        if (sender is not Control grip || grip.DataContext is not CategoryRowViewModel { IsAll: false } ||
            !e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed)
            return;

        _dragCategoryRow = grip.FindAncestorOfType<Button>();
        _dragCategoryRow?.Classes.Add("dragging");
        e.Pointer.Capture(grip);
        e.Handled = true; // don't let the row's click select it
    }

    private void OnCategoryGripMoved(object sender, PointerEventArgs e)
    {
        if (_dragCategoryRow is null)
            return;

        var row = CategoryRowAt(e.GetPosition(CategoryList));
        if (ReferenceEquals(row, _dragCategoryRow))
            row = null;
        if (ReferenceEquals(row, _categoryDropRow))
            return;

        _categoryDropRow?.Classes.Remove("droptarget");
        _categoryDropRow = row;
        _categoryDropRow?.Classes.Add("droptarget");
        e.Handled = true;
    }

    private void OnCategoryGripReleased(object sender, PointerReleasedEventArgs e)
    {
        if (_dragCategoryRow is null)
            return;

        var target = CategoryRowAt(e.GetPosition(CategoryList))?.DataContext as CategoryRowViewModel;
        var dragged = _dragCategoryRow.DataContext as CategoryRowViewModel;
        EndCategoryDrag();
        e.Pointer.Capture(null);
        e.Handled = true;

        if (target is null || dragged is null || ReferenceEquals(target, dragged) || _vm is null)
            return;

        // CategoryRows starts with "All", which is not a category: row i is category i - 1, and a drop
        // on "All" lands at the top (index 0) — never above it.
        _vm.MoveCategoryTo(dragged.Id, Math.Max(0, _vm.CategoryRows.IndexOf(target) - 1));
    }

    private void OnCategoryGripCaptureLost(object sender, PointerCaptureLostEventArgs e) => EndCategoryDrag();

    private void EndCategoryDrag()
    {
        _dragCategoryRow?.Classes.Remove("dragging");
        _categoryDropRow?.Classes.Remove("droptarget");
        _dragCategoryRow = null;
        _categoryDropRow = null;
    }

    /// <summary>The category row button under a point in <see cref="CategoryList"/>'s coordinates.</summary>
    private Button CategoryRowAt(Point point) =>
        CategoryList.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Classes.Contains("cat"))
            .FirstOrDefault(b => b.TranslatePoint(new Point(0, 0), CategoryList) is { } topLeft &&
                                 new Rect(topLeft, b.Bounds.Size).Contains(point));
}

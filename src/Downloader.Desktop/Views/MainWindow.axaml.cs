using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Downloader.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Intercept paste (tunnel) on the top box: a large list would freeze the UI if the multi-line
        // TextBox laid it out. Small pastes insert normally; a large paste opens the Add dialog directly.
        TopUrlBox.AddHandler(KeyDownEvent, OnTopUrlBoxPaste, RoutingStrategies.Tunnel);
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
}

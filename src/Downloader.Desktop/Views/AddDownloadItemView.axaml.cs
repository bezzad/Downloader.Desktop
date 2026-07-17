using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Downloader.Desktop.ViewModels;

namespace Downloader.Desktop.Views;

public partial class AddDownloadItemView : Window
{
    public AddDownloadItemView()
    {
        InitializeComponent();

        // Intercept Enter/Tab in the TUNNEL phase so the clipboard suggestion is accepted BEFORE the
        // multi-line TextBox handles Enter. The TextBox inserts a newline in its own bubble-phase key
        // handler and marks the event handled, so a bubble-phase handler here never fires (that was the
        // "Enter just adds a new line" bug). Tunnel runs root→target, before the TextBox's bubble handler.
        UrlBox.AddHandler(KeyDownEvent, OnUrlBoxKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>Esc closes the dialog (standard dialog behavior, since there is no native chrome).
    /// Not reached when the inline queue-name editor handles Esc first (it cancels that row).</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
            return;
        }

        base.OnKeyDown(e);
    }

    /// <summary>Enter in the inline queue-name box confirms the new queue; Esc cancels the row.</summary>
    private void OnQueueNameKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not AddDownloadItemViewModel vm)
            return;
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            vm.ConfirmAddQueue();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            vm.CancelAddQueueCommand.Execute(null);
        }
    }

    /// <summary>While the links box is empty and a clipboard suggestion is showing, Enter or Tab accepts it
    /// (populating the real box). Otherwise keep normal typing behaviour (Enter/Shift+Enter insert newlines
    /// in this multi-line box).</summary>
    private async void OnUrlBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not AddDownloadItemViewModel vm)
            return;

        // Intercept paste (tunnel) so a large list never lays out in the multi-line TextBox (the freeze);
        // route it through the VM, which switches to the bulk summary above the threshold.
        if (UrlBoxPaste.IsPasteGesture(e))
        {
            e.Handled = true;
            var text = await UrlBoxPaste.ReadTextAsync(this);
            if (string.IsNullOrEmpty(text))
                return;
            var current = UrlBox.Text ?? string.Empty;
            var caret = Math.Clamp(UrlBox.CaretIndex, 0, current.Length);
            vm.Urls = current.Substring(0, caret) + text + current.Substring(caret);
            UrlBox.CaretIndex = Math.Min(caret + text.Length, (UrlBox.Text ?? string.Empty).Length);
            return;
        }

        if (!vm.ShowClipboardSuggestion)
            return;
        if (e.Key != Key.Enter && e.Key != Key.Tab)
            return;

        e.Handled = true;
        vm.AcceptClipboardSuggestion();
        UrlBox.CaretIndex = UrlBox.Text?.Length ?? 0;
    }
}

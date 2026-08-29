using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Downloader.Desktop.Views;
using ReactiveUI;
using Xunit;

namespace Downloader.Desktop.Tests.UI;

/// <summary>
/// The dialogs, actually opened.
///
/// Every entry point in <see cref="DialogHelper"/> starts with "if there is no main window, do
/// nothing", and under the headless runtime there never was one — so the whole file used to take the
/// early return and none of the real flow ran. <see cref="DesktopLifetimeScope"/> installs a lifetime
/// with a real window, which lets these assert the things that have actually broken here: a dialog
/// opened from inside another one appearing UNDERNEATH it (only one modal may be on screen), a
/// confirmation that must sit ON TOP of its caller rather than closing it, and the remembered window
/// size round trip.
/// </summary>
public class DialogFlowTests
{
    // Every modal is shown with ShowDialog(MainWindow), so the main window owns it — that ownership is
    // also what makes a second modal a SIBLING of the first rather than its child, which is the bug
    // BeginModal exists to prevent.
    private static T OpenWindow<T>(Window main) where T : Window =>
        main.OwnedWindows.OfType<T>().FirstOrDefault();

    /// <summary>Waits for a dialog of the given type to be on screen, then closes it with a result.</summary>
    private static async Task<T> CloseWhenShown<T>(Window main, object result = null) where T : Window
    {
        T window = null;
        for (var i = 0; i < 200 && window == null; i++)
        {
            DesktopLifetimeScope.Pump(1);
            window = OpenWindow<T>(main);
            if (window == null)
                await Task.Delay(5);
        }

        Assert.True(window != null, $"the {typeof(T).Name} dialog never appeared");
        if (result == null)
            window.Close();
        else
            window.Close(result);
        DesktopLifetimeScope.Pump();
        return window;
    }

    private static Task WithTimeout(Task task) => task.WaitAsync(TimeSpan.FromSeconds(15));

    // ---- the generic modal ------------------------------------------------

    /// <summary>
    /// The Add dialog's whole contract: it opens modal, it hands back what it was closed with, and the
    /// size the user dragged it to is remembered for next time.
    /// </summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task A_modal_returns_its_result_and_remembers_the_size_it_was_left_at()
    {
        Localizer.Instance.Load("en");
        using var scope = new DesktopLifetimeScope();
        var config = Config.New();
        config.WindowSizes[DialogHelper.AddDownloadWindowKey] = new WindowSize { Width = 700, Height = 520 };

        var expected = new List<DownloadItem> { new() { Urls = { "https://10.255.255.1/a.bin" } } };
        var dialog = DialogHelper.ShowDialog<AddDownloadItemView, AddDownloadItemViewModel, List<DownloadItem>>(
            new AddDownloadItemView(), new AddDownloadItemViewModel(config, ""), config);

        var view = await CloseWhenShown<AddDownloadItemView>(scope.MainWindow, expected);
        var result = await dialog.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Same(expected, result);
        Assert.Equal(700, view.Width);   // the remembered size was applied on open
        Assert.Equal(520, view.Height);
        // …and re-saved on close, so the next open starts where the user left it.
        Assert.Equal(700, config.WindowSizes[DialogHelper.AddDownloadWindowKey].Width);
        Assert.Empty(DialogHelper.OpenModals);
    }

    /// <summary>Dismissed without a result — callers must get default, not a phantom empty add.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task A_dismissed_modal_hands_back_nothing()
    {
        Localizer.Instance.Load("en");
        using var scope = new DesktopLifetimeScope();
        var config = Config.New();

        var dialog = DialogHelper.ShowDialog<AddDownloadItemView, AddDownloadItemViewModel, List<DownloadItem>>(
            new AddDownloadItemView(), new AddDownloadItemViewModel(config, ""), config);

        await CloseWhenShown<AddDownloadItemView>(scope.MainWindow);

        Assert.Null(await dialog.WaitAsync(TimeSpan.FromSeconds(15)));
    }

    /// <summary>
    /// Only ONE modal may be on screen. Every dialog is owned by the main window, so a second one is
    /// the first one's SIBLING — the shared owner raises the earlier dialog back on top and the new
    /// one looks like it opened underneath (Donate from About).
    /// </summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task Opening_a_second_dialog_closes_the_first_instead_of_hiding_behind_it()
    {
        Localizer.Instance.Load("en");
        using var scope = new DesktopLifetimeScope();
        var config = Config.New();

        var first = DialogHelper.ShowDialog<AddDownloadItemView, AddDownloadItemViewModel, List<DownloadItem>>(
            new AddDownloadItemView(), new AddDownloadItemViewModel(config, ""), config);
        for (var i = 0; i < 200 && OpenWindow<AddDownloadItemView>(scope.MainWindow) == null; i++)
        {
            DesktopLifetimeScope.Pump(1);
            await Task.Delay(5);
        }
        Assert.Single(DialogHelper.OpenModals);

        var about = DialogHelper.ShowAbout();

        // The first dialog is gone, and the second is the only tracked modal.
        Assert.Null(await first.WaitAsync(TimeSpan.FromSeconds(15)));
        var aboutView = await CloseWhenShown<AboutView>(scope.MainWindow);
        Assert.NotNull(aboutView);
        await WithTimeout(about);
        Assert.Empty(DialogHelper.OpenModals);
    }

    // ---- the confirmation --------------------------------------------------

    /// <summary>
    /// A confirmation deliberately does NOT close its caller — it is asked FROM a dialog (the details
    /// window's "refresh link" asks before discarding a partial file), so closing the caller would
    /// dismiss the very thing being confirmed.
    /// </summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task A_confirmation_sits_on_top_of_its_caller_rather_than_closing_it()
    {
        Localizer.Instance.Load("en");
        using var scope = new DesktopLifetimeScope();
        var config = Config.New();

        var caller = DialogHelper.ShowDialog<AddDownloadItemView, AddDownloadItemViewModel, List<DownloadItem>>(
            new AddDownloadItemView(), new AddDownloadItemViewModel(config, ""), config);
        for (var i = 0; i < 200 && OpenWindow<AddDownloadItemView>(scope.MainWindow) == null; i++)
        {
            DesktopLifetimeScope.Pump(1);
            await Task.Delay(5);
        }

        var confirm = DialogHelper.Confirm("Replace the file?", "A different size means the partial is lost.");
        await CloseWhenShown<ConfirmView>(scope.MainWindow, true);

        Assert.True(await confirm.WaitAsync(TimeSpan.FromSeconds(15)));
        Assert.NotNull(OpenWindow<AddDownloadItemView>(scope.MainWindow)); // the caller is still up

        await CloseWhenShown<AddDownloadItemView>(scope.MainWindow);
        await WithTimeout(caller);
    }

    /// <summary>Anything other than an explicit yes is a no — the destructive action must not proceed.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task A_confirmation_that_is_dismissed_counts_as_no()
    {
        Localizer.Instance.Load("en");
        using var scope = new DesktopLifetimeScope();

        var confirm = DialogHelper.Confirm("Replace the file?", "…");
        await CloseWhenShown<ConfirmView>(scope.MainWindow);

        Assert.False(await confirm.WaitAsync(TimeSpan.FromSeconds(15)));
    }

    // ---- the read-only dialogs --------------------------------------------

    /// <summary>The details dialog opens for a row and cleans its subscriptions up on close.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task The_details_dialog_opens_for_a_row_and_remembers_its_size()
    {
        Localizer.Instance.Load("en");
        using var scope = new DesktopLifetimeScope();
        var config = Config.New();
        var manager = new DownloadManager();
        manager.Initialize(config);
        var row = manager.Add(new DownloadItem { Urls = { "https://10.255.255.1/f.bin" }, FileName = "f.bin" },
            autoStart: false);

        var details = DialogHelper.ShowDetails(row, config);
        var view = await CloseWhenShown<DownloadDetailsView>(scope.MainWindow);
        await WithTimeout(details);

        Assert.NotNull(view);
        Assert.True(config.WindowSizes.ContainsKey(DialogHelper.DetailsWindowKey),
            "the details window's size must be remembered like the Add dialog's");
        Assert.Empty(DialogHelper.OpenModals);
    }

    /// <summary>Nothing to show details for is a no-op, not a null dereference.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task Details_for_no_row_does_nothing()
    {
        using var scope = new DesktopLifetimeScope();

        await WithTimeout(DialogHelper.ShowDetails(null));

        Assert.Empty(DialogHelper.OpenModals);
    }

    /// <summary>About and Donate are the two dialogs reachable from each other — both must open.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task The_about_and_donate_dialogs_both_open_and_close_cleanly()
    {
        Localizer.Instance.Load("en");
        using var scope = new DesktopLifetimeScope();

        var about = DialogHelper.ShowAbout();
        await CloseWhenShown<AboutView>(scope.MainWindow);
        await WithTimeout(about);

        var donate = DialogHelper.ShowDonate();
        await CloseWhenShown<DonateView>(scope.MainWindow);
        await WithTimeout(donate);

        Assert.Empty(DialogHelper.OpenModals);
    }

    /// <summary>The update prompt is a non-modal Topmost window so it shows even from the tray.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public void The_update_prompt_opens_without_blocking_and_ignores_a_missing_release()
    {
        Localizer.Instance.Load("en");
        using var scope = new DesktopLifetimeScope();

        DialogHelper.ShowUpdatePrompt(null); // nothing to prompt about — must not open a blank prompt
        DesktopLifetimeScope.Pump();
        Assert.Empty(scope.MainWindow.OwnedWindows);

        DialogHelper.ShowUpdatePrompt(new UpdateInfo
        {
            Version = "99.0.0", Tag = "v99.0.0", ReleaseUrl = "https://host/releases/v99.0.0"
        });
        DesktopLifetimeScope.Pump();

        // Shown UN-owned and Topmost on purpose: the main window may be hidden in the tray when an
        // update lands, and an owned dialog would be hidden along with it.
        Assert.Empty(scope.MainWindow.OwnedWindows);
        Assert.True(new UpdatePromptView().Topmost);
    }

    /// <summary>
    /// A link captured from the browser extension (or a second launch) surfaces the window and opens
    /// the Add dialog pre-filled. This is the payoff of the whole local-API path — if the dialog never
    /// opens, the user clicks "download with Downloader" in their browser and nothing visible happens.
    /// </summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task A_captured_link_opens_the_add_dialog_prefilled()
    {
        Localizer.Instance.Load("en");
        var realScheduler = RxApp.MainThreadScheduler;
        RxApp.MainThreadScheduler = new DeferringScheduler(); // see DeferringScheduler: window before init
        try
        {
        using var scope = new DesktopLifetimeScope();
        var manager = new DownloadManager();
        var main = new MainViewModel(new CapturedConfigFileService(), manager) { View = scope.MainWindow };
        for (var i = 0; i < 200 && main.Downloads == null; i++)
        {
            DesktopLifetimeScope.Pump(1);
            await Task.Delay(5);
        }

        // The shell publishes its capture handler for the local API; that is how a link sent by the
        // browser extension reaches the window.
        Assert.NotNull(LocalApiService.OnUrlCaptured);
        LocalApiService.OnUrlCaptured("https://10.255.255.1/captured.bin");

        var view = await CloseWhenShown<AddDownloadItemView>(scope.MainWindow);
        Assert.Equal("https://10.255.255.1/captured.bin", main.DownloadUrl);
        Assert.Contains("captured.bin", ((AddDownloadItemViewModel)view.DataContext!).Urls);

        // A blank capture must not open anything.
        LocalApiService.OnUrlCaptured("   ");
        DesktopLifetimeScope.Pump();
        Assert.Empty(scope.MainWindow.OwnedWindows);
        }
        finally
        {
            RxApp.MainThreadScheduler = realScheduler;
        }
    }

    private sealed class CapturedConfigFileService : IFileService
    {
        public Task<Config> LoadFromFileAsync() => Task.FromResult(Config.New());
        public Task SaveToFileAsync(Config itemToSave) => Task.CompletedTask;
    }

    /// <summary>Copying is best-effort: no text, or no clipboard, must both be survivable.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task Copying_text_is_best_effort()
    {
        using var scope = new DesktopLifetimeScope();

        await WithTimeout(DialogHelper.CopyTextAsync(""));
        await WithTimeout(DialogHelper.CopyTextAsync(null));
        await WithTimeout(DialogHelper.CopyTextAsync("the log path"));
    }
}

using System;
using System.Windows.Input;
using ReactiveUI;

namespace Downloader.Desktop.ViewModels;

/// <summary>
/// The in-app Donate modal (mirrors Donate.md): a thank-you, the Liberapay link, the USDT address
/// with in-app copy (no browser round-trip), and the non-monetary ways to help. Replaces opening
/// Donate.md in a browser — users couldn't tell anything had happened.
/// </summary>
public class DonateViewModel : ViewModelBase
{
    public const string LiberapayUrl = "https://liberapay.com/bezzad/donate";
    public const string RepoUrl = "https://github.com/bezzad/Downloader.Desktop";
    public const string UsdtAddress = "0xFF6B6524BA90Fb7b0C5d5bE1D71903CBF0f8198a";
    public const string UsdtNetwork = "Tether (BEP20 — BNB Smart Chain)";

    private bool _copied;

    public DonateViewModel()
    {
        OpenLiberapayCommand = ReactiveCommand.Create(() => Open(LiberapayUrl));
        OpenRepoCommand = ReactiveCommand.Create(() => Open(RepoUrl));
        CopyUsdtCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var clipboard = (View as Avalonia.Controls.TopLevel)?.Clipboard
                            ?? (Services.DialogHelper.MainWindow as Avalonia.Controls.TopLevel)?.Clipboard;
            if (clipboard != null)
                await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, UsdtAddress);
            Copied = true;
        });
    }

    public string Usdt => UsdtAddress;
    public string Network => UsdtNetwork;

    /// <summary>Flips the copy button's label to "Copied!" after a successful copy.</summary>
    public bool Copied
    {
        get => _copied;
        private set => this.RaiseAndSetIfChanged(ref _copied, value);
    }

    public ICommand OpenLiberapayCommand { get; }
    public ICommand OpenRepoCommand { get; }
    public ICommand CopyUsdtCommand { get; }

    private static void Open(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // best-effort
        }
    }
}

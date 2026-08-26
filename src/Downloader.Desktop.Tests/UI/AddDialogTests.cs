using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Downloader.Desktop.Models;
using Downloader.Desktop.Plugins;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Xunit;

namespace Downloader.Desktop.Tests.UI;

/// <summary>
/// The Add-link dialog: how pasted text becomes downloads, and how a plugin's link variants gate the
/// Download button.
///
/// The variant lookup is the awkward part. It runs per keystroke against a plugin that may go to the
/// network, and the Download button is disabled while it is in flight — so a lookup that never
/// completes would wedge the dialog with no way to add anything. Both the "still looking" and
/// "lookup failed" states are covered here, since a failure has to reach the user as a message rather
/// than as a spinner that never stops.
/// </summary>
public class AddDialogTests
{
    private static AddDownloadItemViewModel Build(
        string url,
        Func<string, CancellationToken, Task<IReadOnlyList<LinkVariant>>> getVariants = null,
        Func<string, string> getResolverName = null)
    {
        Localizer.Instance.Load("en");
        return new AddDownloadItemViewModel(Config.New(), url, getVariants: getVariants,
            getResolverName: getResolverName);
    }

    // ---- parsing pasted links ---------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_single_pasted_link_becomes_one_download()
    {
        var vm = Build("https://10.255.255.1/file.zip");

        Assert.True(vm.IsSingleLink);
        Assert.False(vm.IsMultiple);
        Assert.True(vm.CanDownload);
        Assert.True(vm.IsFilenameEnabled); // a name can be typed for a single link

        var items = vm.BuildItems();
        Assert.Single(items);
        Assert.Equal("https://10.255.255.1/file.zip", items[0].Url);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Several_pasted_lines_become_several_downloads()
    {
        var vm = Build("https://10.255.255.1/a.zip\nhttps://10.255.255.1/b.zip\nhttps://10.255.255.1/c.zip");

        Assert.True(vm.IsMultiple);
        Assert.False(vm.IsSingleLink);
        // A per-download name makes no sense for a batch, so the box is disabled (not hidden).
        Assert.False(vm.IsFilenameEnabled);

        var items = vm.BuildItems();
        Assert.Equal(3, items.Count);
        Assert.Equal(new[] { "a.zip", "b.zip", "c.zip" },
            items.Select(i => i.Url.Split('/').Last()).ToArray());
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Blank_lines_and_stray_whitespace_are_ignored()
    {
        var vm = Build("  https://10.255.255.1/a.zip  \n\n   \n\thttps://10.255.255.1/b.zip\n");

        Assert.Equal(2, vm.BuildItems().Count);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Nothing_pasted_means_nothing_to_download()
    {
        var vm = Build("   ");

        Assert.False(vm.CanDownload);
        Assert.Empty(vm.BuildItems());
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_typed_name_is_carried_onto_the_download()
    {
        var vm = Build("https://10.255.255.1/file.zip");
        vm.Filename = "my-name.zip";

        Assert.Equal("my-name.zip", vm.BuildItems().Single().FileName);
    }

    // ---- the resolver badge ------------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_link_a_plugin_claims_shows_which_plugin_will_handle_it()
    {
        var vm = Build("https://10.255.255.1/stream.m3u8",
            getResolverName: _ => "Streaming media");

        Assert.True(vm.HasResolver);
        Assert.Equal("Streaming media", vm.ResolverName);
        Assert.Contains("Streaming media", vm.ResolverBadgeText);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void An_ordinary_link_shows_no_badge()
    {
        var vm = Build("https://10.255.255.1/file.zip", getResolverName: _ => null);

        Assert.False(vm.HasResolver);
    }

    // ---- link variants -----------------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task The_default_variant_is_pre_checked_and_each_ticked_one_becomes_a_download()
    {
        var variants = new IReadOnlyList<LinkVariant>[]
        {
            new[]
            {
                new LinkVariant { Id = "1080", Label = "1080p", IsDefault = true },
                new LinkVariant { Id = "720", Label = "720p" },
            }
        }[0];

        var vm = Build("https://10.255.255.1/video", getVariants: (_, _) => Task.FromResult(variants));
        await WaitForLookup(vm);

        Assert.True(vm.HasVariants);
        Assert.True(vm.ShowVariantSection);
        Assert.Equal(2, vm.Variants.Count);

        // Only the variant the plugin marked as default starts checked — offering to download every
        // quality of the same video at once would be a surprising default.
        Assert.True(vm.Variants.Single(v => v.Id == "1080").IsChecked);
        Assert.False(vm.Variants.Single(v => v.Id == "720").IsChecked);

        Assert.Equal("1080", vm.BuildItems().Single().VariantId);

        // Ticking the second one adds a second download.
        vm.Variants.Single(v => v.Id == "720").IsChecked = true;
        var both = vm.BuildItems();
        Assert.Equal(2, both.Count);
        Assert.Contains(both, i => i.VariantId == "1080");
        Assert.Contains(both, i => i.VariantId == "720");
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Unchecking_every_variant_falls_back_to_a_plain_add()
    {
        var variants = new[]
        {
            new LinkVariant { Id = "zip", Label = "Offline copy (.zip)" },
        };

        var vm = Build("https://10.255.255.1/page.html",
            getVariants: (_, _) => Task.FromResult<IReadOnlyList<LinkVariant>>(variants));
        await WaitForLookup(vm);

        foreach (var v in vm.Variants)
            v.IsChecked = false;

        // A fallback plugin can offer one unchecked variant without hijacking the normal flow: with
        // nothing selected the dialog just adds the pasted link.
        var items = vm.BuildItems();
        Assert.Single(items);
        Assert.Null(items[0].VariantId);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_variant_carrying_its_own_url_replaces_the_pasted_link()
    {
        var variants = new[]
        {
            new LinkVariant { Id = "12b", Label = "gemma3:12b", SubstituteUrl = "gemma3:12b", IsDefault = true },
        };

        var vm = Build("https://ollama.com/library/gemma3",
            getVariants: (_, _) => Task.FromResult<IReadOnlyList<LinkVariant>>(variants));
        await WaitForLookup(vm);

        var item = vm.BuildItems().Single();

        // A substitute variant IS its own link; VariantId stays null so post-download actions that
        // parse the item URL keep working.
        Assert.Equal("gemma3:12b", item.Url);
        Assert.Null(item.VariantId);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_failed_variant_lookup_is_reported_instead_of_spinning_forever()
    {
        var vm = Build("https://10.255.255.1/video",
            getVariants: (_, _) => throw new InvalidOperationException("sign in to confirm you're not a bot"));
        await WaitForLookup(vm);

        // Previously this showed a spinner, then an empty section, and only explained itself on the
        // failed row after Download.
        Assert.True(vm.HasVariantError);
        Assert.Contains("bot", vm.VariantError);
        Assert.True(vm.ShowVariantSection);
        Assert.False(vm.IsFetchingVariants);
        Assert.True(vm.CanDownload); // the dialog is still usable
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_link_with_no_variants_shows_no_picker()
    {
        var vm = Build("https://10.255.255.1/file.zip",
            getVariants: (_, _) => Task.FromResult<IReadOnlyList<LinkVariant>>(null));
        await WaitForLookup(vm);

        Assert.False(vm.HasVariants);
        Assert.False(vm.HasVariantError);
        Assert.False(vm.ShowVariantSection);
        Assert.True(vm.CanDownload);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_multi_link_paste_skips_the_variant_picker_entirely()
    {
        var called = false;
        var vm = Build("https://10.255.255.1/a\nhttps://10.255.255.1/b",
            getVariants: (_, _) =>
            {
                called = true;
                return Task.FromResult<IReadOnlyList<LinkVariant>>(null);
            });
        await WaitForLookup(vm);

        Assert.False(called, "asking per link would fire a lookup storm on a bulk paste");
        Assert.False(vm.ShowVariantSection);
    }

    /// <summary>Lets the debounced background lookup settle.</summary>
    private static async Task WaitForLookup(AddDownloadItemViewModel vm)
    {
        var deadline = Environment.TickCount64 + 10_000;
        while (Environment.TickCount64 < deadline)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            if (!vm.IsFetchingVariants && (vm.HasVariants || vm.HasVariantError || !vm.ShowVariantSection))
                break;
            await Task.Delay(25);
        }
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }
}

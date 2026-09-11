using Avalonia.Headless.XUnit;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace Downloader.Desktop.Tests.Integration;

/// <summary>
/// What Settings says, and what a user can DO, when the local API could not bind.
///
/// <para>Reported after a run where every port in the range was busy: the row read
/// "127.0.0.1:15151 … not running", which says the app only ever tried 15151 (it tries all five), and
/// offered nothing to do about it — the background retry gives up after about a minute, and the only
/// way back was to toggle the whole feature off and on.</para>
/// </summary>
public class LocalApiRetryTests
{
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_bound_listener_is_described_by_the_port_it_actually_bound()
    {
        Assert.Equal("127.0.0.1:15153", LocalApiService.DescribeAddress(15153));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void With_nothing_bound_the_address_names_the_RANGE_that_was_tried()
    {
        // Never a single port: naming the preferred one while the listener is down is what made a
        // fallback that HAD run look like it had not.
        var text = LocalApiService.DescribeAddress(0);

        Assert.Contains(LocalApiService.PortRange[0].ToString(), text);
        Assert.Contains(LocalApiService.PortRange[^1].ToString(), text);
    }

    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)] // binds every port via HttpListener — slow on macOS CI
    public void The_retry_button_appears_only_while_the_api_is_enabled_and_down_and_it_binds_when_a_port_frees()
    {
        LocalApiService.Stop(); // a listener left bound by another test would answer as "already running"

        var blockers = LocalApiService.PortRange.Select(p =>
        {
            var l = new HttpListener();
            l.Prefixes.Add($"http://127.0.0.1:{p}/");
            try { l.Start(); return l; } catch { return null; } // a port already busy externally blocks too
        }).ToList();

        // Same guard as Start_retries_in_background_until_a_port_frees_up: the scenario IS "every port
        // is taken", and on macOS CI a prefix can be refused while the port stays free. Nothing to
        // exercise then — leave rather than report a bug that is not there.
        if (LocalApiService.PortRange.Where((p, i) => blockers[i] == null && PortIsFree(p)).Any())
        {
            Release(blockers);
            return;
        }

        var config = Config.New();
        config.Settings.EnableBrowserIntegration = true;
        LocalApiService.Config = config;
        try
        {
            var settings = new SettingViewModel(config, new DownloadManager());
            LocalApiService.Start();
            Assert.False(LocalApiService.IsRunning); // everything blocked right now

            // Down, so the button is offered — and the row does not claim an address it does not have.
            Assert.True(settings.CanRetryLocalApi);
            Assert.Equal(LocalApiService.DescribeAddress(0), settings.LocalApiAddress);

            // Pressing it while everything is still busy changes nothing and must not throw.
            settings.RetryLocalApiCommand.Execute(null);
            Assert.False(LocalApiService.IsRunning);
            Assert.True(settings.CanRetryLocalApi);

            // Free one port: the SAME button now brings the API up, with no toggling and no restart.
            var freed = blockers.First(b => b != null);
            freed.Stop();
            freed.Close();
            blockers[blockers.IndexOf(freed)] = null;

            settings.RetryLocalApiCommand.Execute(null);

            Assert.True(LocalApiService.IsRunning);
            Assert.False(settings.CanRetryLocalApi);           // nothing left to retry
            Assert.Contains(LocalApiService.EffectivePort.ToString(), settings.LocalApiAddress);
        }
        finally
        {
            LocalApiService.Stop();
            LocalApiService.Config = null;
            Release(blockers);
        }
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Nothing_is_offered_to_retry_while_the_feature_is_switched_off()
    {
        LocalApiService.Stop();
        var config = Config.New();
        config.Settings.EnableBrowserIntegration = false;
        try
        {
            var settings = new SettingViewModel(config, new DownloadManager());

            Assert.False(settings.CanRetryLocalApi);
        }
        finally
        {
            LocalApiService.Stop();
        }
    }

    /// <summary>True when nothing at all is listening on the loopback port.</summary>
    private static bool PortIsFree(int port)
    {
        var probe = new TcpListener(IPAddress.Loopback, port);
        try { probe.Start(); return true; }
        catch (SocketException) { return false; }
        finally { try { probe.Stop(); } catch { /* cleanup */ } }
    }

    private static void Release(IEnumerable<HttpListener> blockers)
    {
        foreach (var b in blockers)
        {
            if (b is null) continue;
            try { b.Stop(); b.Close(); } catch { /* cleanup */ }
        }
    }
}

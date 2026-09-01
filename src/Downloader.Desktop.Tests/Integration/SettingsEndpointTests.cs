using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Integration;

/// <summary>
/// <c>/api/settings</c> — what a local client needs to pre-fill its own UI. The browser extension's
/// download-folder box starts from the app's own default rather than an empty field, which is the only
/// way a typed absolute path is right on the user's machine without them looking it up.
/// </summary>
public class SettingsEndpointTests
{
    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public void The_app_reports_its_default_save_folder_and_version()
    {
        LocalApiService.Stop(); // a leak from another test would answer on the wrong port

        var manager = new DownloadManager();
        var config = Config.New();
        config.DefaultQueue.IsRunning = false; // nothing may hit the network here
        config.Settings.DefaultSavePath = "/tmp/dldesktop-settings-endpoint";
        manager.Initialize(config);

        LocalApiService.Manager = manager;
        LocalApiService.Config = config;
        try
        {
            LocalApiService.Start();
            Assert.True(LocalApiService.IsRunning);

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var answer = Json(client, "/api/settings");

            Assert.Equal("/tmp/dldesktop-settings-endpoint", answer.GetProperty("defaultSavePath").GetString());
            Assert.Equal(UpdateService.CurrentVersion.ToString(), answer.GetProperty("version").GetString());

            // Read-only: no download appeared, and the setting it reported is untouched.
            Assert.Empty(manager.Items);
            Assert.Equal("/tmp/dldesktop-settings-endpoint", config.Settings.DefaultSavePath);

            // Nothing secret may travel back, however the app is configured — the same API ACCEPTS
            // cookies and headers, so an echo here is how one would escape.
            var raw = Body(client, "/api/settings");
            Assert.DoesNotContain("cookie", raw, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("header", raw, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("proxy", raw, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            LocalApiService.Stop();
            LocalApiService.Manager = null;
            LocalApiService.Config = null;
        }
    }

    // The /api handlers marshal onto the UI thread, which IS this thread under the headless runtime —
    // so the request runs off-thread while the dispatcher is pumped (see LocalApiEndToEndTests.Pump).
    private static HttpResponseMessage Send(HttpClient client, string pathAndQuery)
    {
        var task = Task.Run(() => client.GetAsync($"http://127.0.0.1:{LocalApiService.EffectivePort}{pathAndQuery}"));
        var deadline = DateTime.UtcNow.AddSeconds(40);
        while (!task.IsCompleted)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
            if (DateTime.UtcNow > deadline) throw new TimeoutException("local API request did not finish");
        }
        return task.GetAwaiter().GetResult();
    }

    private static string Body(HttpClient client, string pathAndQuery)
    {
        var response = Send(client, pathAndQuery);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Task.Run(() => response.Content.ReadAsStringAsync()).GetAwaiter().GetResult();
    }

    private static JsonElement Json(HttpClient client, string pathAndQuery) =>
        JsonDocument.Parse(Body(client, pathAndQuery)).RootElement.Clone();
}

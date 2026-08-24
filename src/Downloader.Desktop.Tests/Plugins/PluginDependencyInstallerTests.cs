using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Downloader.Desktop.Plugins;
using Downloader.Desktop.Plugins.Hls;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Plugins;

/// <summary>
/// The "check → fetch on Add" flow (add-plugin-dependency-fetch): a plugin declares external binaries it
/// needs via <see cref="PluginBinaryDependency"/>/<see cref="IHasRuntimeDependencies"/>, the host fetches
/// whatever isn't already available through <see cref="PluginDependencyInstaller"/> (a real
/// <c>DownloadService</c> against a loopback server — no external network), and <see cref="PluginManager"/>
/// exposes a loaded plugin's declared dependencies for the catalog Add flow to consume.
/// </summary>
public class PluginDependencyInstallerTests
{
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void FfmpegBinary_dependency_is_unavailable_until_the_cached_exe_exists()
    {
        var dataDir = Directory.CreateTempSubdirectory("ffmpeg-dep-").FullName;
        try
        {
            var ffmpeg = new FfmpegBinary(dataDir);
            var dep = ffmpeg.GetDependency();

            Assert.Equal("ffmpeg", dep.Id);

            var exeName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

            // A system-wide ffmpeg on PATH legitimately makes the dependency available (that's by
            // design), so the "not available yet" asserts only hold on machines without one.
            var onPath = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator)
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .Any(d => File.Exists(Path.Combine(d, exeName)));

            if (!onPath)
                Assert.False(dep.IsAvailable()); // nothing cached yet

            var exeDir = Path.GetDirectoryName(dep.DownloadDestination)!;
            Directory.CreateDirectory(exeDir);
            var exe = Path.Combine(dataDir, "ffmpeg-bin", exeName);

            // A fragment left by an interrupted download is NOT an installed tool — treating it as one
            // is what left a truncated yt-dlp in place for a month, unrunnable and never refetched.
            File.WriteAllText(exe, "stub");
            if (!onPath)
                Assert.False(dep.IsAvailable());

            File.WriteAllBytes(exe, new byte[(int)Downloader.Desktop.Plugins.Hls.BinaryFile.MinUsableBytes + 1]);
            Downloader.Desktop.Plugins.Hls.BinaryFile.MakeExecutable(exe);
            Assert.True(dep.IsAvailable());
        }
        finally { try { Directory.Delete(dataDir, recursive: true); } catch { /* best-effort */ } }
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void PluginManager_exposes_dependencies_declared_by_a_loaded_plugin()
    {
        var pm = new PluginManager();
        pm.RegisterPlugin(new FakeDependentPlugin());

        var deps = pm.GetRuntimeDependencies(FakeDependentPlugin.PluginId);

        Assert.Single(deps);
        Assert.Equal("fake-dep", deps[0].Id);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void PluginManager_reports_no_dependencies_for_a_plugin_without_the_interface()
    {
        var pm = new PluginManager();
        pm.RegisterPlugin(new FakePlainPlugin());

        Assert.Empty(pm.GetRuntimeDependencies(FakePlainPlugin.PluginId));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void PluginManager_reports_no_dependencies_for_an_unknown_plugin_id()
    {
        var pm = new PluginManager();
        Assert.Empty(pm.GetRuntimeDependencies("nope"));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task EnsureAllAsync_downloads_a_missing_dependency_and_finishes_it()
    {
        var payload = new byte[64 * 1024];
        new Random(42).NextBytes(payload);
        using var server = new RangeLoopbackServer(payload);

        var dir = Directory.CreateTempSubdirectory("plugin-dep-dl-").FullName;
        try
        {
            var dest = Path.Combine(dir, "download", "dep.bin");
            var finished = false;
            var dep = new PluginBinaryDependency
            {
                Id = "d1",
                DisplayName = "Dep One",
                DownloadUrl = new Uri(server.Url + "dep.bin"),
                DownloadDestination = dest,
                IsAvailable = () => false,
                FinishInstallAsync = _ => { finished = true; return Task.CompletedTask; },
            };

            var progressReports = 0;
            var progress = new Progress<PluginDependencyProgress>(_ => Interlocked.Increment(ref progressReports));

            await PluginDependencyInstaller.EnsureAllAsync(new[] { dep }, progress, CancellationToken.None);

            Assert.True(File.Exists(dest));
            Assert.Equal(payload, await File.ReadAllBytesAsync(dest, TestContext.Current.CancellationToken));
            Assert.True(finished);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ } }
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task EnsureAllAsync_finishes_an_existing_complete_archive_without_redownloading()
    {
        // An install-time fetch that downloaded fully but was killed before extraction: the archive sits
        // at the destination. EnsureAll must just extract it, not download again.
        using var server = new RangeLoopbackServer(new byte[16]);
        var dir = Directory.CreateTempSubdirectory("plugin-dep-heal-").FullName;
        try
        {
            var dest = Path.Combine(dir, "dep.bin");
            await File.WriteAllBytesAsync(dest, new byte[] { 1, 2, 3 }, TestContext.Current.CancellationToken);
            var available = false;
            var dep = new PluginBinaryDependency
            {
                Id = "d1",
                DisplayName = "Dep One",
                DownloadUrl = new Uri(server.Url + "does-not-exist.bin"), // a download attempt would 404 → fail loudly
                DownloadDestination = dest,
                IsAvailable = () => available,
                FinishInstallAsync = _ => { available = true; return Task.CompletedTask; },
            };

            await PluginDependencyInstaller.EnsureAllAsync(new[] { dep }, null, CancellationToken.None);

            Assert.True(available);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ } }
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task EnsureAllAsync_replaces_a_corrupt_archive_and_downloads_fresh()
    {
        // A truncated archive from an interrupted download: FinishInstall throws on it, so EnsureAll must
        // delete it and download the real payload instead of failing on the same bad file forever.
        var payload = new byte[8 * 1024];
        new Random(7).NextBytes(payload);
        using var server = new RangeLoopbackServer(payload);

        var dir = Directory.CreateTempSubdirectory("plugin-dep-corrupt-").FullName;
        try
        {
            var dest = Path.Combine(dir, "dep.bin");
            await File.WriteAllBytesAsync(dest, new byte[] { 9, 9 }, TestContext.Current.CancellationToken); // truncated garbage
            var available = false;
            var dep = new PluginBinaryDependency
            {
                Id = "d1",
                DisplayName = "Dep One",
                DownloadUrl = new Uri(server.Url + "dep.bin"),
                DownloadDestination = dest,
                IsAvailable = () => available,
                FinishInstallAsync = async _ =>
                {
                    var bytes = await File.ReadAllBytesAsync(dest);
                    if (bytes.Length != payload.Length)
                        throw new InvalidOperationException("corrupt archive");
                    available = true;
                },
            };

            await PluginDependencyInstaller.EnsureAllAsync(new[] { dep }, null, CancellationToken.None);

            Assert.True(available);
            Assert.Equal(payload, await File.ReadAllBytesAsync(dest, TestContext.Current.CancellationToken));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ } }
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Ffmpeg_finish_install_deletes_a_corrupt_archive_so_the_next_attempt_redownloads()
    {
        var dataDir = Directory.CreateTempSubdirectory("ffmpeg-corrupt-").FullName;
        try
        {
            var ffmpeg = new FfmpegBinary(dataDir);
            var dep = ffmpeg.GetDependency();
            Directory.CreateDirectory(Path.GetDirectoryName(dep.DownloadDestination)!);
            await File.WriteAllBytesAsync(dep.DownloadDestination, new byte[] { 0x50, 0x4B, 1, 2 }, TestContext.Current.CancellationToken); // truncated "zip"

            await Assert.ThrowsAsync<InvalidOperationException>(() => dep.FinishInstallAsync(CancellationToken.None));

            Assert.False(File.Exists(dep.DownloadDestination)); // corrupt archive was removed
        }
        finally { try { Directory.Delete(dataDir, recursive: true); } catch { /* best-effort */ } }
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task EnsureAllAsync_skips_dependencies_that_are_already_available()
    {
        using var server = new RangeLoopbackServer(new byte[16]);
        var attempted = false;
        var dep = new PluginBinaryDependency
        {
            Id = "d1",
            DisplayName = "Dep One",
            DownloadUrl = new Uri(server.Url + "unused.bin"),
            DownloadDestination = Path.Combine(Path.GetTempPath(), "should-not-be-created.bin"),
            IsAvailable = () => true, // already there
            FinishInstallAsync = _ => { attempted = true; return Task.CompletedTask; },
        };

        await PluginDependencyInstaller.EnsureAllAsync(new[] { dep }, null, CancellationToken.None);

        Assert.False(attempted); // never downloaded or "finished" — IsAvailable short-circuited it
        Assert.False(File.Exists(dep.DownloadDestination));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task EnsureAllAsync_throws_and_does_not_download_when_already_cancelled()
    {
        using var server = new RangeLoopbackServer(new byte[16]);
        var dest = Path.Combine(Path.GetTempPath(), "cancelled-" + Guid.NewGuid().ToString("N") + ".bin");
        var dep = new PluginBinaryDependency
        {
            Id = "d1",
            DisplayName = "Dep One",
            DownloadUrl = new Uri(server.Url + "x.bin"),
            DownloadDestination = dest,
            IsAvailable = () => false,
            FinishInstallAsync = _ => Task.CompletedTask,
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => PluginDependencyInstaller.EnsureAllAsync(new[] { dep }, null, cts.Token));
        Assert.False(File.Exists(dest));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task EnsureAllAsync_surfaces_a_404_as_a_friendly_failure()
    {
        using var server = new RangeLoopbackServer(new byte[16]);
        var dep = new PluginBinaryDependency
        {
            Id = "d1",
            DisplayName = "Dep One",
            DownloadUrl = new Uri(server.Url + "does-not-exist.bin"),
            DownloadDestination = Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid().ToString("N") + ".bin"),
            IsAvailable = () => false,
            FinishInstallAsync = _ => Task.CompletedTask,
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => PluginDependencyInstaller.EnsureAllAsync(new[] { dep }, null, CancellationToken.None));
        Assert.Contains("Dep One", ex.Message);
    }

    private sealed class FakeDependentPlugin : IDownloaderPlugin, IHasRuntimeDependencies
    {
        public const string PluginId = "com.test.fake-dependent";
        public string Id => PluginId;
        public string Name => "Fake dependent plugin";
        public string Version => "1.0.0";
        public string Author => "test";
        public string Description => "test";
        public void Initialize(IPluginContext context) { }

        public IReadOnlyList<PluginBinaryDependency> GetRequiredDependencies(string dataDirectory) => new[]
        {
            new PluginBinaryDependency
            {
                Id = "fake-dep",
                DisplayName = "Fake Dep",
                DownloadUrl = new Uri("https://example.invalid/fake-dep.bin"),
                DownloadDestination = Path.Combine(dataDirectory, "fake-dep.bin"),
                IsAvailable = () => false,
                FinishInstallAsync = _ => Task.CompletedTask,
            },
        };
    }

    private sealed class FakePlainPlugin : IDownloaderPlugin
    {
        public const string PluginId = "com.test.fake-plain";
        public string Id => PluginId;
        public string Name => "Fake plain plugin";
        public string Version => "1.0.0";
        public string Author => "test";
        public string Description => "test";
        public void Initialize(IPluginContext context) { }
    }

    /// <summary>Minimal loopback HTTP server with Range support, mirroring IntegrationTests.LoopbackFileServer.</summary>
    private sealed class RangeLoopbackServer : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly byte[] _data;

        public string Url { get; }

        public RangeLoopbackServer(byte[] data)
        {
            _data = data;
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            var port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            Url = $"http://127.0.0.1:{port}/";
            _listener = new HttpListener();
            _listener.Prefixes.Add(Url);
            _listener.Start();
            _ = Task.Run(LoopAsync);
        }

        private async Task LoopAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); }
                catch { break; }
                _ = Task.Run(() => Handle(ctx));
            }
        }

        private void Handle(HttpListenerContext ctx)
        {
            try
            {
                var resp = ctx.Response;
                if (!ctx.Request.Url!.AbsolutePath.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) ||
                    ctx.Request.Url.AbsolutePath.Contains("does-not-exist"))
                {
                    resp.StatusCode = 404;
                    resp.OutputStream.Close();
                    return;
                }

                resp.Headers["Accept-Ranges"] = "bytes";
                if (ctx.Request.HttpMethod == "HEAD")
                {
                    resp.ContentLength64 = _data.Length;
                    resp.OutputStream.Close();
                    return;
                }

                var range = ctx.Request.Headers["Range"];
                int start = 0, end = _data.Length - 1;
                if (!string.IsNullOrEmpty(range) && range.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = range.Substring(6).Split('-');
                    if (parts.Length == 2)
                    {
                        if (int.TryParse(parts[0], out var s)) start = s;
                        if (int.TryParse(parts[1], out var e)) end = e;
                    }
                    end = Math.Min(end, _data.Length - 1);
                    start = Math.Max(0, Math.Min(start, end));
                    resp.StatusCode = 206;
                    resp.AddHeader("Content-Range", $"bytes {start}-{end}/{_data.Length}");
                }

                var len = end - start + 1;
                resp.ContentLength64 = len;
                resp.OutputStream.Write(_data, start, len);
                resp.OutputStream.Close();
            }
            catch
            {
                try { ctx.Response.Abort(); } catch { /* ignore */ }
            }
        }

        public void Dispose()
        {
            try { _listener.Stop(); } catch { /* ignore */ }
            try { _listener.Close(); } catch { /* ignore */ }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Downloader.Desktop.Models;
using Downloader.Desktop.Plugins;
using Downloader.Desktop.Plugins.Ollama;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests;

/// <summary>Pure tests for the Ollama plugin's claim/manifest logic (compile-referenced project).</summary>
public class OllamaLogicTests
{
    // ---- 4.1 claim matrix ----

    [Fact]
    public void Claims_bare_names_and_library_urls()
    {
        foreach (var input in new[]
        {
            "gemma3", "gemma3:12b", "llama3.2", "llama3.2:latest", "qwen2.5-coder:7b",
            "user/model", "user/model:v1.5-q4_K_M",
            "https://ollama.com/library/gemma3", "https://ollama.com/library/gemma3:12b",
            "https://www.ollama.com/library/llama3.2:1b"
        })
        {
            Assert.True(OllamaModelRef.TryParse(input, out var r), $"should claim: {input}");
            Assert.NotNull(r.Tag);
        }
    }

    [Fact]
    public void Defaults_namespace_and_tag()
    {
        Assert.True(OllamaModelRef.TryParse("gemma3:1b", out var r));
        Assert.Equal("library", r.Namespace);
        Assert.Equal("gemma3", r.Model);
        Assert.Equal("1b", r.Tag);

        Assert.True(OllamaModelRef.TryParse("gemma3", out var r2));
        Assert.Equal("latest", r2.Tag);

        Assert.True(OllamaModelRef.TryParse("https://ollama.com/library/gemma3:12b", out var r3));
        Assert.Equal("library/gemma3", r3.PathNamespaceModel);
        Assert.Equal("12b", r3.Tag);
    }

    [Fact]
    public void Rejects_urls_paths_and_file_names()
    {
        foreach (var input in new[]
        {
            null, "", "  ", "https://example.com/file.zip", "https://ollama.com/download",
            "http://github.com/owner/repo", "video.mp4", "archive.tar.gz", "movie.mkv", "page.html",
            "model.gguf", "/usr/local/bin", "~/Downloads", "C:\\temp\\x", "two words", "a/b/c"
        })
        {
            Assert.False(OllamaModelRef.TryParse(input, out _), $"should reject: {input ?? "<null>"}");
        }
    }

    // ---- 4.2 manifest parsing ----

    private const string ManifestJson = """
    {
      "schemaVersion": 2,
      "mediaType": "application/vnd.docker.distribution.manifest.v2+json",
      "config": { "mediaType": "application/vnd.docker.container.image.v1+json", "digest": "sha256:cfg", "size": 484 },
      "layers": [
        { "mediaType": "application/vnd.ollama.image.model", "digest": "sha256:modelhash", "size": 815319791 },
        { "mediaType": "application/vnd.ollama.image.template", "digest": "sha256:tmpl", "size": 358 },
        { "mediaType": "application/vnd.ollama.image.params", "digest": "sha256:params", "size": 98 }
      ]
    }
    """;

    [Fact]
    public void Parses_manifest_and_picks_the_model_layer()
    {
        var m = OllamaManifest.Parse(ManifestJson);
        Assert.Equal("sha256:modelhash", m.ModelLayer.Digest);
        Assert.Equal(815319791, m.ModelLayer.Size);
        // Metadata = config + the non-model layers.
        var metadata = m.MetadataLayers.Select(l => l.Digest).ToList();
        Assert.Contains("sha256:cfg", metadata);
        Assert.Contains("sha256:tmpl", metadata);
        Assert.Contains("sha256:params", metadata);
        Assert.DoesNotContain("sha256:modelhash", metadata);
    }

    [Fact]
    public void Manifest_without_model_layer_is_a_clear_error()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            OllamaManifest.Parse("""{ "layers": [ { "mediaType": "other", "digest": "sha256:x", "size": 1 } ] }"""));
        Assert.Contains("model layer", ex.Message);
    }
}

/// <summary>Registry + installer against a loopback fake registry / temp store dirs.</summary>
public class OllamaIntegrationTests
{
    [Fact]
    public async Task Resolves_a_model_to_its_blob_url_with_size_and_gguf_name()
    {
        var model = Bytes("MODEL", 4096);
        using var server = new FakeRegistry(model);
        using var registry = new HttpOllamaRegistry(server.Url);
        var resolver = new OllamaResolver(registry);

        Assert.True(resolver.CanResolve("gemma3:1b"));
        var plan = await resolver.ResolveAsync("gemma3:1b", CancellationToken.None);

        Assert.Single(plan.Parts);
        Assert.Equal($"{server.Url}v2/library/gemma3/blobs/{server.ModelDigest}", plan.Parts[0].Url);
        Assert.Equal(model.Length, plan.Parts[0].ExpectedSize);
        Assert.Equal("gemma3-1b.gguf", plan.SuggestedFileName);
    }

    [Fact]
    public async Task Unknown_model_is_a_clear_not_found_error()
    {
        using var server = new FakeRegistry(Bytes("X", 10), notFound: true);
        using var registry = new HttpOllamaRegistry(server.Url);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            registry.GetManifestAsync(Parse("nope:latest"), CancellationToken.None));
        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public async Task Installer_happy_path_builds_the_store_and_keeps_the_original_file()
    {
        var model = Bytes("WEIGHTS", 8192);
        using var server = new FakeRegistry(model);
        using var registry = new HttpOllamaRegistry(server.Url);

        var work = TempDir();
        try
        {
            var file = Path.Combine(work, "gemma3-1b.gguf");
            await File.WriteAllBytesAsync(file, model);
            var store = Path.Combine(work, "store");

            await new OllamaInstaller(registry).InstallAsync(Parse("gemma3:1b"), file, store, null, CancellationToken.None);

            // Blobs: the model blob + every metadata layer, named sha256-<hex>.
            var blobs = Directory.GetFiles(Path.Combine(store, "blobs")).Select(Path.GetFileName).ToList();
            Assert.Contains(OllamaInstaller.BlobFileName(server.ModelDigest), blobs);
            Assert.Contains("sha256-cfg", blobs);
            // Manifest written at manifests/registry.ollama.ai/library/gemma3/1b with the raw JSON.
            var manifestPath = Path.Combine(store, "manifests", "registry.ollama.ai", "library", "gemma3", "1b");
            Assert.True(File.Exists(manifestPath));
            Assert.Contains(server.ModelDigest, await File.ReadAllTextAsync(manifestPath));
            // The user's downloaded file is untouched.
            Assert.Equal(model, await File.ReadAllBytesAsync(file));
        }
        finally { TryDelete(work); }
    }

    [Fact]
    public async Task Installer_digest_mismatch_writes_no_manifest()
    {
        using var server = new FakeRegistry(Bytes("REAL", 4096));
        using var registry = new HttpOllamaRegistry(server.Url);

        var work = TempDir();
        try
        {
            var file = Path.Combine(work, "wrong.gguf");
            await File.WriteAllBytesAsync(file, Bytes("TAMPERED", 4096)); // different bytes → wrong sha256
            var store = Path.Combine(work, "store");

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new OllamaInstaller(registry).InstallAsync(Parse("gemma3:1b"), file, store, null, CancellationToken.None));

            Assert.Contains("Checksum mismatch", ex.Message);
            Assert.False(Directory.Exists(Path.Combine(store, "manifests"))); // nothing half-installed
        }
        finally { TryDelete(work); }
    }

    // ---- helpers ----

    private static OllamaModelRef Parse(string s)
    {
        Assert.True(OllamaModelRef.TryParse(s, out var r));
        return r;
    }

    private static byte[] Bytes(string tag, int len)
    {
        var b = new byte[len];
        var t = Encoding.ASCII.GetBytes(tag);
        for (var i = 0; i < len; i++) b[i] = t[i % t.Length];
        return b;
    }

    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "dldesktop_ollama_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
    }

    /// <summary>Loopback registry: /v2/*/manifests/* returns a manifest whose model digest matches the
    /// served bytes; /v2/*/blobs/<digest> serves the blob (tiny fixed content for metadata digests).</summary>
    private sealed class FakeRegistry : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly byte[] _model;
        private readonly bool _notFound;
        public string Url { get; }
        public string ModelDigest { get; }

        public FakeRegistry(byte[] model, bool notFound = false)
        {
            _model = model;
            _notFound = notFound;
            using (var sha = System.Security.Cryptography.SHA256.Create())
                ModelDigest = "sha256:" + Convert.ToHexString(sha.ComputeHash(model)).ToLowerInvariant();

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
                var path = ctx.Request.Url!.AbsolutePath;
                if (_notFound) { ctx.Response.StatusCode = 404; ctx.Response.Close(); return; }

                byte[] body;
                if (path.Contains("/manifests/"))
                {
                    body = Encoding.UTF8.GetBytes($$"""
                    {
                      "schemaVersion": 2,
                      "config": { "mediaType": "application/vnd.docker.container.image.v1+json", "digest": "sha256:cfg", "size": 4 },
                      "layers": [
                        { "mediaType": "application/vnd.ollama.image.model", "digest": "{{ModelDigest}}", "size": {{_model.Length}} },
                        { "mediaType": "application/vnd.ollama.image.template", "digest": "sha256:tmpl", "size": 4 }
                      ]
                    }
                    """);
                }
                else if (path.Contains("/blobs/"))
                {
                    var digest = path[(path.LastIndexOf('/') + 1)..];
                    body = digest == ModelDigest ? _model : Encoding.UTF8.GetBytes("meta");
                }
                else
                {
                    ctx.Response.StatusCode = 404; ctx.Response.Close(); return;
                }

                ctx.Response.ContentLength64 = body.Length;
                ctx.Response.OutputStream.Write(body);
                ctx.Response.OutputStream.Close();
            }
            catch { try { ctx.Response.Abort(); } catch { /* ignore */ } }
        }

        public void Dispose()
        {
            try { _listener.Stop(); } catch { /* ignore */ }
            try { _listener.Close(); } catch { /* ignore */ }
        }
    }
}

/// <summary>Built-in plugin loading + post-download-action host behavior (in-process fakes).</summary>
public class BuiltInAndPostActionTests
{
    private sealed class FakeAction : IPostDownloadAction
    {
        public bool Executed;
        public bool ShouldFail;
        public string Label => "Add to Fake";
        public bool CanOffer(string sourceUrl, string filePath) => sourceUrl?.StartsWith("fake:") == true;
        public Task ExecuteAsync(string sourceUrl, string filePath, IProgress<double> progress, CancellationToken ct)
        {
            if (ShouldFail) throw new InvalidOperationException("fake action failed");
            Executed = true;
            return Task.CompletedTask;
        }
    }

    private sealed class FakePlugin : IDownloaderPlugin
    {
        private readonly FakeAction _action;
        public FakePlugin(FakeAction action) => _action = action;
        public string Id => "com.test.fake";
        public string Name => "Fake";
        public string Version => "1.0";
        public string Author => "t";
        public string Description => "";
        public void Initialize(IPluginContext context) => context.RegisterPostDownloadAction(_action);
    }

    [Fact]
    public void BuiltIn_plugins_are_flagged_and_not_removable()
    {
        var pm = new PluginManager();
        pm.RegisterPlugin(new FakePlugin(new FakeAction()), isBuiltIn: true);

        var d = pm.Plugins.Single();
        Assert.True(d.IsBuiltIn);
        Assert.False(pm.RemovePlugin(d.Id));       // refused
        Assert.Single(pm.Plugins);                  // still there
        pm.SetEnabled(d.Id, false);                 // …but can be disabled
        Assert.False(pm.Plugins.Single().IsEnabled);
    }

    [Fact]
    public void User_plugins_remain_removable()
    {
        var pm = new PluginManager();
        pm.RegisterPlugin(new FakePlugin(new FakeAction()));
        Assert.True(pm.RemovePlugin("com.test.fake"));
        Assert.Empty(pm.Plugins);
    }

    [Fact]
    public void Action_is_offered_only_by_the_resolving_plugin_for_matching_input()
    {
        var pm = new PluginManager();
        pm.RegisterPlugin(new FakePlugin(new FakeAction()));

        var tmp = Path.GetTempFileName();
        try
        {
            Assert.NotNull(pm.FindPostDownloadAction("com.test.fake", "fake:model", tmp));
            Assert.Null(pm.FindPostDownloadAction("com.other.plugin", "fake:model", tmp)); // wrong plugin
            Assert.Null(pm.FindPostDownloadAction("com.test.fake", "https://other", tmp)); // CanOffer false
            Assert.Null(pm.FindPostDownloadAction(null, "fake:model", tmp));               // no resolver id
            pm.SetEnabled("com.test.fake", false);
            Assert.Null(pm.FindPostDownloadAction("com.test.fake", "fake:model", tmp));    // disabled
        }
        finally { File.Delete(tmp); }
    }

    [AvaloniaFact]
    public async Task Manager_offers_label_on_completed_item_and_runs_only_on_click()
    {
        var action = new FakeAction();
        var pm = new PluginManager();
        pm.RegisterPlugin(new FakePlugin(action));

        var manager = new DownloadManager(pm);
        var config = Config.New();
        config.DefaultQueue.IsRunning = false;
        manager.Initialize(config);

        var file = Path.GetTempFileName();
        try
        {
            var item = new DownloadItem
            {
                Urls = { "fake:model" },
                SaveFolder = Path.GetDirectoryName(file),
                FileName = Path.GetFileName(file),
                ResolverPluginId = "com.test.fake",
                Status = DownloadStatus.Completed
            };
            var vm = manager.Add(item, autoStart: false);
            vm.Status = DownloadStatus.Completed;

            Assert.Equal("Add to Fake", manager.PostDownloadActionLabel(vm));
            Assert.True(vm.HasPostAction);
            Assert.False(action.Executed); // offered ≠ run

            await manager.RunPostDownloadAction(vm);
            Assert.True(action.Executed);
        }
        finally { File.Delete(file); }
    }

    [AvaloniaFact]
    public async Task Failing_action_surfaces_a_friendly_item_error()
    {
        var action = new FakeAction { ShouldFail = true };
        var pm = new PluginManager();
        pm.RegisterPlugin(new FakePlugin(action));
        var manager = new DownloadManager(pm);
        var config = Config.New();
        config.DefaultQueue.IsRunning = false;
        manager.Initialize(config);

        var file = Path.GetTempFileName();
        try
        {
            var item = new DownloadItem
            {
                Urls = { "fake:model" },
                SaveFolder = Path.GetDirectoryName(file),
                FileName = Path.GetFileName(file),
                ResolverPluginId = "com.test.fake",
                Status = DownloadStatus.Completed
            };
            var vm = manager.Add(item, autoStart: false);
            vm.Status = DownloadStatus.Completed;

            await manager.RunPostDownloadAction(vm);
            // Pump the dispatcher so the OnUi error assignment lands.
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.Contains("fake action failed", vm.ErrorMessage);
            Assert.Equal(DownloadStatus.Completed, vm.Status); // the download itself stays completed
        }
        finally { File.Delete(file); }
    }

    [AvaloniaFact]
    public async Task Bare_model_name_is_claimed_by_an_enabled_resolver_and_rejected_when_disabled()
    {
        var pm = new PluginManager();
        pm.RegisterPlugin(new ClaimingPlugin());
        var manager = new DownloadManager(pm);

        var plan = await manager.ResolvePlanAsync("gemma3:1b", CancellationToken.None);
        Assert.NotNull(plan);
        Assert.Equal("https://resolved/blob", plan.Parts[0].Url);

        pm.SetEnabled("com.test.claimer", false);
        Assert.Null(await manager.ResolvePlanAsync("gemma3:1b", CancellationToken.None)); // unclaimed again
    }

    private sealed class ClaimingPlugin : IDownloaderPlugin
    {
        public string Id => "com.test.claimer";
        public string Name => "Claimer";
        public string Version => "1.0";
        public string Author => "t";
        public string Description => "";
        public void Initialize(IPluginContext context) => context.RegisterResolver(new Resolver());

        private sealed class Resolver : ILinkResolver
        {
            public bool CanResolve(string url) => url == "gemma3:1b";
            public Task<DownloadPlan> ResolveAsync(string url, CancellationToken ct) =>
                Task.FromResult(new DownloadPlan { Parts = new[] { new DownloadPart { Url = "https://resolved/blob" } } });
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Downloader.Desktop.Plugins;
using Downloader.Desktop.Plugins.Ollama;
using Xunit;

namespace Downloader.Desktop.Tests.Plugins;

/// <summary>
/// Downloading a model from a HuggingFace repository and adding it to the local Ollama store: which links
/// are claimed, which of a repository's files are offered, and what happens to the file afterwards.
/// Everything here is network-free — the repository API is stubbed — because every decision worth testing
/// is made from the file list, not from the transfer.
/// </summary>
public class HuggingFaceTests
{
    // ── Claiming (no network, ever) ──────────────────────────────────────────────────────────────────

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    // The link from the report, and the shapes people paste around it.
    [InlineData("https://huggingface.co/empero-ai/Qwen3.8-2B-Distill-GGUF", true)]
    [InlineData("https://huggingface.co/unsloth/Qwen3-8B-GGUF/tree/main", true)]
    [InlineData("https://huggingface.co/unsloth/Qwen3-8B-GGUF/resolve/main/Qwen3-8B-Q4_K_M.gguf", true)]
    [InlineData("https://huggingface.co/unsloth/Qwen3-8B-GGUF/blob/main/Qwen3-8B-Q4_K_M.gguf", true)]
    [InlineData("https://hf.co/unsloth/Qwen3-8B-GGUF", true)]
    [InlineData("https://www.huggingface.co/unsloth/Qwen3-8B-GGUF", true)]
    // Everything on the site that is NOT a model repository.
    [InlineData("https://huggingface.co/datasets/squad", false)]
    [InlineData("https://huggingface.co/spaces/someone/demo", false)]
    [InlineData("https://huggingface.co/unsloth", false)]
    [InlineData("https://huggingface.co", false)]
    [InlineData("https://huggingface.co/docs/transformers/index", false)]
    [InlineData("https://huggingface.co/unsloth/Qwen3-8B-GGUF/discussions", false)]
    // And links that are not HuggingFace at all.
    [InlineData("https://huggingface.co.evil.example/a/b", false)]
    [InlineData("https://example.com/unsloth/Qwen3-8B-GGUF", false)]
    [InlineData("gemma3:12b", false)]
    [InlineData("", false)]
    public void Only_model_repositories_are_claimed(string url, bool expected)
    {
        var resolver = new HuggingFaceResolver(new StubApi());
        Assert.Equal(expected, resolver.CanResolve(url));
        Assert.Equal(expected, HuggingFaceModelRef.TryParse(url, out _));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Claiming_never_calls_the_api()
    {
        var api = new StubApi();
        var resolver = new HuggingFaceResolver(api);

        for (var i = 0; i < 50; i++)
            resolver.CanResolve("https://huggingface.co/empero-ai/Qwen3.8-2B-Distill-GGUF");

        Assert.Equal(0, api.Calls);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_link_is_parsed_into_owner_repo_revision_and_file()
    {
        Assert.True(HuggingFaceModelRef.TryParse(
            "https://huggingface.co/unsloth/Qwen3-8B-GGUF/resolve/v1.2/sub/dir/Qwen3-8B-Q4_K_M.gguf", out var withFile));
        Assert.Equal("unsloth", withFile!.Owner);
        Assert.Equal("Qwen3-8B-GGUF", withFile.Repo);
        Assert.Equal("v1.2", withFile.Revision);
        Assert.Equal("sub/dir/Qwen3-8B-Q4_K_M.gguf", withFile.FilePath);
        Assert.Contains("resolve/v1.2/sub/dir/Qwen3-8B-Q4_K_M.gguf", withFile.DownloadUrl);

        Assert.True(HuggingFaceModelRef.TryParse("https://huggingface.co/unsloth/Qwen3-8B-GGUF", out var repo));
        Assert.False(repo!.HasFile);
        Assert.Equal("main", repo.Revision); // the default branch, when none is named
    }

    // ── Choosing a file ──────────────────────────────────────────────────────────────────────────────

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Several_quantisations_are_offered_with_their_size()
    {
        var api = new StubApi(
            new HuggingFaceFile("Qwen3-8B-Q4_K_M.gguf", 4_800_000_000, Hex('a')),
            new HuggingFaceFile("Qwen3-8B-Q8_0.gguf", 8_500_000_000, Hex('b')),
            new HuggingFaceFile("README.md", 1200, null),
            new HuggingFaceFile("config.json", 900, null));
        var resolver = new HuggingFaceResolver(api);

        var variants = await resolver.GetVariantsAsync("https://huggingface.co/unsloth/Qwen3-8B-GGUF", null,
            TestContext.Current.CancellationToken);

        Assert.NotNull(variants);
        Assert.Equal(2, variants!.Count); // only the model files, never the README
        Assert.Equal("Qwen3-8B-Q4_K_M.gguf", variants[0].Id);
        Assert.Contains("Q4_K_M", variants[0].Label);
        Assert.Contains("GB", variants[0].Label); // the size is what makes the choice meaningful
        Assert.True(variants[0].IsDefault);       // smallest first: fastest, runs on the most machines
        Assert.Equal(4_800_000_000, variants[0].ExpectedSize);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task The_chosen_quantisation_is_what_gets_downloaded()
    {
        var api = new StubApi(
            new HuggingFaceFile("Qwen3-8B-Q4_K_M.gguf", 4_800_000_000, Hex('a')),
            new HuggingFaceFile("Qwen3-8B-Q8_0.gguf", 8_500_000_000, Hex('b')));
        var resolver = new HuggingFaceResolver(api);

        var plan = await resolver.ResolveAsync("https://huggingface.co/unsloth/Qwen3-8B-GGUF",
            new ResolveOptions { VariantId = "Qwen3-8B-Q8_0.gguf" }, TestContext.Current.CancellationToken);

        Assert.Single(plan.Parts);
        Assert.Contains("resolve/main/Qwen3-8B-Q8_0.gguf", plan.Parts[0].Url);
        Assert.Equal("Qwen3-8B-Q8_0.gguf", plan.SuggestedFileName);
        Assert.Equal(8_500_000_000, plan.Parts[0].ExpectedSize);
        Assert.Equal(PostProcessKind.None, plan.PostProcess.Kind);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_repository_with_one_model_file_asks_nothing_and_downloads_it()
    {
        var api = new StubApi(
            new HuggingFaceFile("model.gguf", 1000, Hex('c')),
            new HuggingFaceFile("README.md", 10, null));
        var resolver = new HuggingFaceResolver(api);
        var ct = TestContext.Current.CancellationToken;

        Assert.Null(await resolver.GetVariantsAsync("https://huggingface.co/o/r", null, ct));

        var plan = await resolver.ResolveAsync("https://huggingface.co/o/r", null, ct);
        Assert.Contains("resolve/main/model.gguf", plan.Parts[0].Url);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_link_to_one_file_resolves_to_that_file_without_listing_the_repository()
    {
        var api = new StubApi();
        var resolver = new HuggingFaceResolver(api);

        var plan = await resolver.ResolveAsync(
            "https://huggingface.co/o/r/resolve/main/model-Q5_K_M.gguf", null, TestContext.Current.CancellationToken);

        Assert.Equal(0, api.Calls);
        Assert.Equal("model-Q5_K_M.gguf", plan.SuggestedFileName);
    }

    // ── Failing clearly ──────────────────────────────────────────────────────────────────────────────

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_repository_with_no_model_file_says_so()
    {
        var api = new StubApi(
            new HuggingFaceFile("model.safetensors", 5_000_000_000, Hex('d')),
            new HuggingFaceFile("README.md", 10, null));
        var resolver = new HuggingFaceResolver(api);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => resolver.ResolveAsync("https://huggingface.co/o/r", null, TestContext.Current.CancellationToken));

        Assert.Contains("o/r", ex.Message);
        Assert.Contains("no GGUF model file", ex.Message);
    }

    /// <summary>A split model is a limitation, not a mystery: say which it is rather than downloading one
    /// shard that Ollama can never load.</summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_sharded_model_is_refused_with_the_reason()
    {
        var api = new StubApi(
            new HuggingFaceFile("Qwen3-235B-Q4_K_M-00001-of-00005.gguf", 40_000_000_000, Hex('e')),
            new HuggingFaceFile("Qwen3-235B-Q4_K_M-00002-of-00005.gguf", 40_000_000_000, Hex('f')));
        var resolver = new HuggingFaceResolver(api);
        var ct = TestContext.Current.CancellationToken;

        Assert.Null(await resolver.GetVariantsAsync("https://huggingface.co/o/r", null, ct));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => resolver.ResolveAsync("https://huggingface.co/o/r", null, ct));

        Assert.Contains("split", ex.Message);
        Assert.Contains("single-file GGUF", ex.Message);
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData(System.Net.HttpStatusCode.NotFound, "doesn't exist")]
    [InlineData(System.Net.HttpStatusCode.Unauthorized, "private or gated")]
    [InlineData(System.Net.HttpStatusCode.Forbidden, "private or gated")]
    public async Task A_missing_repository_and_a_private_one_are_told_apart(
        System.Net.HttpStatusCode status, string expected)
    {
        using var http = new System.Net.Http.HttpClient(new StatusHandler(status));
        var api = new HttpHuggingFaceApi(http);
        HuggingFaceModelRef.TryParse("https://huggingface.co/o/r", out var model);

        var ex = await Assert.ThrowsAsync<HuggingFaceRepoException>(
            () => api.ListFilesAsync(model!, TestContext.Current.CancellationToken));

        Assert.Contains(expected, ex.Message);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_repository_listing_reads_sizes_and_digests_from_the_lfs_block()
    {
        // The shape HuggingFace's tree API really returns: for an LFS object the top-level "size" is the
        // pointer file's, and the real size and digest live under "lfs".
        var json = """
        [
          { "type": "directory", "path": "sub" },
          { "type": "file", "path": "README.md", "size": 1200 },
          { "type": "file", "path": "model-Q4_K_M.gguf", "size": 135,
            "lfs": { "oid": "AAAABBBBCCCCDDDDEEEEFFFF00001111222233334444555566667777888899AA", "size": 4800000000 } }
        ]
        """;

        var files = HttpHuggingFaceApi.Parse(json);

        Assert.Equal(2, files.Count); // the directory entry is not a file
        var model = files.Single(f => f.IsGguf);
        Assert.Equal(4_800_000_000, model.Size);
        Assert.Equal("aaaabbbbccccddddeeeeffff00001111222233334444555566667777888899aa", model.Sha256);
        Assert.Equal("Q4_K_M", model.Quantisation);
        Assert.Null(files.Single(f => f.Path == "README.md").Sha256);
    }

    // ── Installing into the local store ──────────────────────────────────────────────────────────────

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_downloaded_model_is_installed_under_its_huggingface_name()
    {
        var (store, file, digest) = await AModelOnDisk();
        HuggingFaceModelRef.TryParse("https://huggingface.co/unsloth/Qwen3-8B-GGUF", out var model);

        await new HuggingFaceInstaller().InstallAsync(model!, file, digest, store, null,
            TestContext.Current.CancellationToken);

        // Named the way Ollama itself names a model pulled from HuggingFace.
        Assert.Equal("hf.co/unsloth/Qwen3-8B-GGUF:q4_k_m", HuggingFaceInstaller.LocalModelName(model!, file));

        var manifestPath = HuggingFaceInstaller.ManifestPath(store, model!, "q4_k_m");
        Assert.True(File.Exists(manifestPath), "the manifest is what makes the model appear in `ollama list`");

        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath,
            TestContext.Current.CancellationToken));
        var layer = manifest.RootElement.GetProperty("layers")[0];
        Assert.Equal(OllamaManifest.ModelMediaType, layer.GetProperty("mediaType").GetString());
        Assert.Equal($"sha256:{digest}", layer.GetProperty("digest").GetString());

        // Both blobs the manifest points at really exist…
        var blobs = Directory.GetFiles(Path.Combine(store, "blobs")).Select(Path.GetFileName).ToList();
        Assert.Contains($"sha256-{digest}", blobs);
        var configDigest = manifest.RootElement.GetProperty("config").GetProperty("digest").GetString();
        Assert.Contains(configDigest!.Replace(':', '-'), blobs);

        // …and the user's own download is untouched where they saved it.
        Assert.True(File.Exists(file));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_file_that_is_not_what_the_repository_published_installs_nothing()
    {
        var (store, file, _) = await AModelOnDisk();
        HuggingFaceModelRef.TryParse("https://huggingface.co/unsloth/Qwen3-8B-GGUF", out var model);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new HuggingFaceInstaller().InstallAsync(model!, file, Hex('9'), store, null,
                TestContext.Current.CancellationToken));

        Assert.Contains("Checksum mismatch", ex.Message);
        Assert.Contains("Nothing was added", ex.Message);
        Assert.False(Directory.Exists(Path.Combine(store, "manifests")));
        Assert.False(Directory.Exists(Path.Combine(store, "blobs")));
        Assert.True(File.Exists(file)); // and the download itself is left alone
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_repository_that_publishes_no_digest_still_installs()
    {
        // Small non-LFS files carry no oid. Refusing a download that is fine would be worse than the
        // check being unavailable, so the install proceeds.
        var (store, file, digest) = await AModelOnDisk();
        HuggingFaceModelRef.TryParse("https://huggingface.co/o/r", out var model);

        await new HuggingFaceInstaller().InstallAsync(model!, file, null, store, null,
            TestContext.Current.CancellationToken);

        Assert.True(File.Exists(HuggingFaceInstaller.ManifestPath(store, model!, "q4_k_m")));
        Assert.Contains($"sha256-{digest}",
            Directory.GetFiles(Path.Combine(store, "blobs")).Select(Path.GetFileName));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Without_an_ollama_store_the_failure_says_where_it_looked()
    {
        var (_, file, _) = await AModelOnDisk();
        HuggingFaceModelRef.TryParse("https://huggingface.co/o/r", out var model);
        var missing = Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid().ToString("N")[..8], "models");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new HuggingFaceInstaller().InstallAsync(model!, file, null, missing, null,
                TestContext.Current.CancellationToken));

        Assert.Contains("Ollama doesn't appear to be installed", ex.Message);
        Assert.Contains(missing, ex.Message);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task The_offer_is_made_for_a_completed_huggingface_download_only()
    {
        var (_, file, _) = await AModelOnDisk();
        var action = new AddHuggingFaceToOllamaAction(new StubApi());

        Assert.Equal("Add to Ollama", action.Label);
        Assert.True(action.CanOffer("https://huggingface.co/unsloth/Qwen3-8B-GGUF", file));
        // Not a model repository, not a GGUF, not on disk.
        Assert.False(action.CanOffer("https://example.com/a.zip", file));
        Assert.False(action.CanOffer("https://huggingface.co/unsloth/Qwen3-8B-GGUF", file + ".zip"));
        Assert.False(action.CanOffer("https://huggingface.co/unsloth/Qwen3-8B-GGUF", "/nowhere/model.gguf"));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Running_the_offer_verifies_against_what_the_repository_publishes()
    {
        var (store, file, digest) = await AModelOnDisk();
        var api = new StubApi(new HuggingFaceFile("Qwen3-8B-Q4_K_M.gguf", 16, digest));
        var action = new AddHuggingFaceToOllamaAction(api) { StoreRootOverride = store };

        await action.ExecuteAsync("https://huggingface.co/unsloth/Qwen3-8B-GGUF", file, null,
            TestContext.Current.CancellationToken);

        HuggingFaceModelRef.TryParse("https://huggingface.co/unsloth/Qwen3-8B-GGUF", out var model);
        Assert.True(File.Exists(HuggingFaceInstaller.ManifestPath(store, model!, "q4_k_m")));
        Assert.Equal(1, api.Calls); // it asked the repository what it published
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Running_the_offer_on_a_tampered_file_writes_nothing()
    {
        var (store, file, _) = await AModelOnDisk();
        var api = new StubApi(new HuggingFaceFile("Qwen3-8B-Q4_K_M.gguf", 16, Hex('9')));
        var action = new AddHuggingFaceToOllamaAction(api) { StoreRootOverride = store };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            action.ExecuteAsync("https://huggingface.co/unsloth/Qwen3-8B-GGUF", file, null,
                TestContext.Current.CancellationToken));

        Assert.Contains("Checksum mismatch", ex.Message);
        Assert.False(Directory.Exists(Path.Combine(store, "manifests")));
    }

    /// <summary>The Ollama and HuggingFace halves of the plugin must never both claim the same input —
    /// the host offers whichever action answers first, and two claimants would make that a coin toss.</summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_two_resolvers_and_actions_claim_disjoint_inputs()
    {
        var ollama = new OllamaResolver(new NoRegistry());
        var hf = new HuggingFaceResolver(new StubApi());

        foreach (var url in new[]
                 {
                     "gemma3:12b", "https://ollama.com/library/gemma3:12b",
                     "https://huggingface.co/unsloth/Qwen3-8B-GGUF",
                     "https://huggingface.co/unsloth/Qwen3-8B-GGUF/resolve/main/m.gguf",
                 })
        {
            Assert.False(ollama.CanResolve(url) && hf.CanResolve(url),
                $"both resolvers claim {url}");
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A downloaded model file plus an empty store to install it into, and the file's real
    /// digest.</summary>
    private static async Task<(string Store, string File, string Digest)> AModelOnDisk()
    {
        var root = Path.Combine(Path.GetTempPath(), "dldesktop-hf-" + Guid.NewGuid().ToString("N")[..8]);
        var store = Path.Combine(root, ".ollama", "models");
        Directory.CreateDirectory(store);
        var downloads = Path.Combine(root, "Downloads");
        Directory.CreateDirectory(downloads);

        var file = Path.Combine(downloads, "Qwen3-8B-Q4_K_M.gguf");
        var bytes = Encoding.UTF8.GetBytes("GGUF pretend weights");
        await File.WriteAllBytesAsync(file, bytes, TestContext.Current.CancellationToken);
        var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return (store, file, digest);
    }

    private static string Hex(char c) => new(c, 64);

    private sealed class StubApi(params HuggingFaceFile[] files) : IHuggingFaceApi
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<HuggingFaceFile>> ListFilesAsync(HuggingFaceModelRef model, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<HuggingFaceFile>>(files);
        }
    }

    private sealed class StatusHandler(System.Net.HttpStatusCode status) : System.Net.Http.HttpMessageHandler
    {
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(
            System.Net.Http.HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new System.Net.Http.HttpResponseMessage(status));
    }

    /// <summary>An Ollama registry that must never be called — these tests only exercise claim checks.</summary>
    private sealed class NoRegistry : IOllamaRegistry
    {
        public Task<OllamaManifest> GetManifestAsync(OllamaModelRef model, CancellationToken ct) =>
            throw new InvalidOperationException("not expected");
        public string BlobUrl(OllamaModelRef model, string digest) => throw new InvalidOperationException("not expected");
        public Task DownloadBlobAsync(OllamaModelRef model, string digest, string destinationPath, CancellationToken ct) =>
            throw new InvalidOperationException("not expected");
    }
}

/// <summary>
/// The plugin's own version, as Settings → Plugins shows it. The catalog and the update check compare this
/// string, so a code change that forgets to bump it means nobody ever receives the fix — which is exactly
/// what happened the last time HuggingFace-sized work shipped under an unchanged version.
/// </summary>
public class OllamaPluginVersionTests
{
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_plugin_reports_the_version_that_carries_huggingface_support()
    {
        var plugin = new OllamaPlugin();
        Assert.Equal("1.2.0", plugin.Version);

        // What the Settings row actually renders.
        var descriptor = new Downloader.Desktop.Services.PluginDescriptor
        {
            Id = plugin.Id,
            Name = plugin.Name,
            Version = plugin.Version,
            Author = plugin.Author,
            Description = plugin.Description,
        };
        var row = new Downloader.Desktop.ViewModels.PluginRowViewModel(
            descriptor, new Downloader.Desktop.Services.PluginManager(),
            Downloader.Desktop.Models.Config.New());

        Assert.Equal("v1.2.0", row.VersionText);
        Assert.Contains("HuggingFace", row.Description);
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Downloader.Desktop.Plugins;
using Downloader.Desktop.Plugins.Ollama;
using Downloader.Desktop.Tests.Plugins.Hls;
using Xunit;

namespace Downloader.Desktop.Tests.Plugins;

/// <summary>
/// The Ollama plugin's two outward-facing halves: what it asks the registry for, and what it writes
/// into the user's local model store.
///
/// Both were only covered along their happy path. The interesting cases are the unhappy ones — a
/// registry that is up but unhappy, a tag list that isn't there, a store that isn't there, a blob
/// already present — because each of them either misleads the user ("model not found" when the
/// registry actually returned a 503) or risks touching files that are not ours.
/// </summary>
public class OllamaRegistryAndInstallTests
{
    // ---- the registry over HTTP -------------------------------------------

    private static HttpOllamaRegistry RegistryAt(LoopbackServer server) =>
        new(baseUrl: server.BaseUrl.TrimEnd('/'), tagsBaseUrl: server.BaseUrl.TrimEnd('/'));

    private static OllamaModelRef Model(string text)
    {
        Assert.True(OllamaModelRef.TryParse(text, out var model), $"'{text}' should parse as a model ref");
        return model!;
    }

    /// <summary>
    /// A 404 means "no such model" and says so. Anything else means the registry is reachable but
    /// unhappy, and reporting that as "check the name and tag" sends the user chasing a typo that
    /// isn't there — so the status code has to survive into the message.
    /// </summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_registry_error_that_is_not_a_missing_model_reports_its_status()
    {
        using var server = new LoopbackServer();
        var model = Model("gemma3:12b");
        server.MapStatus($"/v2/{model.PathNamespaceModel}/manifests/12b", 503);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RegistryAt(server).GetManifestAsync(model, CancellationToken.None));

        Assert.Contains("503", ex.Message);
        Assert.DoesNotContain("not found", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The tag list drives the Add dialog's variant picker for a tag-less paste.</summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task The_tag_list_comes_from_the_website_endpoint()
    {
        using var server = new LoopbackServer();
        var model = Model("gemma3");
        server.MapText($"/{model.PathNamespaceModel}/tags",
            """{"tags":["latest","4b","12b",""," "]}""", "application/json");

        var tags = await RegistryAt(server).GetTagsAsync(model, CancellationToken.None);

        Assert.Equal(new[] { "latest", "4b", "12b" }, tags);
    }

    /// <summary>
    /// No tag list is not an error: a direct "model:tag" resolve still works without it, so the picker
    /// simply offers nothing rather than failing the whole add.
    /// </summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_missing_tag_list_leaves_the_model_resolvable()
    {
        using var server = new LoopbackServer();

        var tags = await RegistryAt(server).GetTagsAsync(Model("gemma3"), CancellationToken.None);

        Assert.Empty(tags);
    }

    /// <summary>A payload that parses but carries no tag array must not be read as a list of one.</summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_tag_payload_with_no_tag_array_yields_nothing()
    {
        using var server = new LoopbackServer();
        var model = Model("gemma3");
        server.MapText($"/{model.PathNamespaceModel}/tags", """{"error":"nope"}""", "application/json");

        Assert.Empty(await RegistryAt(server).GetTagsAsync(model, CancellationToken.None));
    }

    /// <summary>
    /// The interface default exists so a registry written before variants keeps compiling. It has to
    /// answer "no tags" rather than throw, or a stub host breaks the Add dialog.
    /// </summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_registry_that_predates_tag_listing_reports_no_tags()
    {
        IOllamaRegistry registry = new LegacyRegistry();

        Assert.Empty(await registry.GetTagsAsync(Model("gemma3"), CancellationToken.None));
    }

    /// <summary>Implements only what the interface required before tag listing was added.</summary>
    private sealed class LegacyRegistry : IOllamaRegistry
    {
        public Task<OllamaManifest> GetManifestAsync(OllamaModelRef model, CancellationToken ct) =>
            throw new NotSupportedException();
        public string BlobUrl(OllamaModelRef model, string digest) => "";
        public Task DownloadBlobAsync(OllamaModelRef model, string digest, string destinationPath, CancellationToken ct) =>
            Task.CompletedTask;
    }

    // ---- the local model store --------------------------------------------

    /// <summary>An explicit OLLAMA_MODELS wins; otherwise the store lives under the home directory.</summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_store_root_follows_the_environment_before_the_home_directory()
    {
        var previous = Environment.GetEnvironmentVariable("OLLAMA_MODELS");
        try
        {
            Environment.SetEnvironmentVariable("OLLAMA_MODELS", "/custom/models");
            Assert.Equal("/custom/models", OllamaInstaller.DefaultStoreRoot());

            Environment.SetEnvironmentVariable("OLLAMA_MODELS", "   ");
            Assert.EndsWith(Path.Combine(".ollama", "models"), OllamaInstaller.DefaultStoreRoot());

            Environment.SetEnvironmentVariable("OLLAMA_MODELS", null);
            Assert.EndsWith(Path.Combine(".ollama", "models"), OllamaInstaller.DefaultStoreRoot());
        }
        finally
        {
            Environment.SetEnvironmentVariable("OLLAMA_MODELS", previous);
        }
    }

    /// <summary>The download was removed between finishing and pressing the button — say so plainly.</summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Installing_a_file_that_no_longer_exists_reports_it()
    {
        var installer = new OllamaInstaller(new LegacyRegistry());

        await Assert.ThrowsAsync<FileNotFoundException>(() => installer.InstallAsync(
            Model("gemma3:12b"), Path.Combine(Path.GetTempPath(), "gone-" + Guid.NewGuid().ToString("N")),
            storeRoot: null, progress: null, ct: CancellationToken.None));
    }

    /// <summary>
    /// Writing a model store into a machine that has no Ollama would silently create a folder tree the
    /// user never asked for and never sees used. Refuse instead, and say what's missing.
    /// </summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Installing_where_ollama_is_not_set_up_refuses_before_writing_anything()
    {
        var previous = Environment.GetEnvironmentVariable("OLLAMA_MODELS");
        var file = Path.Combine(Path.GetTempPath(), "model-" + Guid.NewGuid().ToString("N") + ".gguf");
        var absentRoot = Path.Combine(Path.GetTempPath(), "no-ollama-" + Guid.NewGuid().ToString("N"), "models");
        File.WriteAllBytes(file, new byte[] { 1, 2, 3 });
        try
        {
            Environment.SetEnvironmentVariable("OLLAMA_MODELS", null);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new OllamaInstaller(new LegacyRegistry())
                    .InstallAsync(Model("gemma3:12b"), file, absentRoot, null, CancellationToken.None));

            Assert.Contains("Ollama", ex.Message);
            Assert.False(Directory.Exists(absentRoot), "nothing may be created when Ollama is absent");
        }
        finally
        {
            Environment.SetEnvironmentVariable("OLLAMA_MODELS", previous);
            File.Delete(file);
        }
    }

    /// <summary>
    /// The blob store is content-addressed, so a digest already present is by definition the same
    /// bytes. Re-linking it is pointless and overwriting it risks a file another model depends on.
    /// </summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_blob_already_in_the_store_is_left_exactly_as_it_was()
    {
        var dir = Directory.CreateTempSubdirectory("ollama-blobs-").FullName;
        try
        {
            var source = Path.Combine(dir, "downloaded.gguf");
            var destination = Path.Combine(dir, "blobs", "sha256-abc");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllText(source, "new bytes");
            File.WriteAllText(destination, "the blob already in the store");

            OllamaInstaller.HardLinkOrCopy(source, destination);

            Assert.Equal("the blob already in the store", File.ReadAllText(destination));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>Whether it links or copies, the store ends up with the bytes and the source is untouched.</summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Placing_a_blob_never_modifies_the_downloaded_file()
    {
        var dir = Directory.CreateTempSubdirectory("ollama-blobs-").FullName;
        try
        {
            var source = Path.Combine(dir, "downloaded.gguf");
            var destination = Path.Combine(dir, "blobs", "sha256-def");
            File.WriteAllText(source, "model bytes");

            OllamaInstaller.HardLinkOrCopy(source, destination);

            Assert.Equal("model bytes", File.ReadAllText(destination));
            Assert.Equal("model bytes", File.ReadAllText(source));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ---- the "Add to Ollama" action ---------------------------------------

    /// <summary>The button only appears for a completed download that really is an Ollama model.</summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_action_offers_itself_only_for_a_model_file_that_exists()
    {
        var action = new AddToOllamaAction(new LegacyRegistry());
        var file = Path.Combine(Path.GetTempPath(), "m-" + Guid.NewGuid().ToString("N") + ".gguf");
        File.WriteAllBytes(file, new byte[] { 7 });
        try
        {
            Assert.Equal("Add to Ollama", action.Label);
            Assert.True(action.CanOffer("gemma3:12b", file));
            Assert.False(action.CanOffer("https://host/file.zip", file), "not a model reference");
            Assert.False(action.CanOffer("gemma3:12b", file + ".missing"), "the file is gone");
            Assert.False(action.CanOffer("gemma3:12b", ""), "no file at all");
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>Asked to install something that is not a model reference, it refuses rather than guessing.</summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task The_action_refuses_a_source_that_is_not_a_model_reference()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new AddToOllamaAction(new LegacyRegistry())
                .ExecuteAsync("https://host/file.zip", "/tmp/whatever", null, CancellationToken.None));

        Assert.Contains("not an Ollama model reference", ex.Message);
    }

    /// <summary>
    /// The whole install, end to end, into a temp store: the verified blob is linked in, the metadata
    /// layers are fetched, and the manifest is written LAST — its presence is what makes the model show
    /// up in `ollama list`, so writing it early would advertise a half-installed model.
    /// </summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task The_action_installs_the_model_into_the_store_it_is_given()
    {
        var store = Directory.CreateTempSubdirectory("ollama-store-").FullName;
        var file = Path.Combine(store, "gemma3-12b.gguf");
        var bytes = Encoding.UTF8.GetBytes("pretend model weights");
        File.WriteAllBytes(file, bytes);
        try
        {
            var registry = new RecordingRegistry(bytes);
            var action = new AddToOllamaAction(registry) { StoreRootOverride = store };
            var progress = new List<double>();

            await action.ExecuteAsync("gemma3:12b", file, new Progress<double>(p => progress.Add(p)),
                CancellationToken.None);

            var blob = Path.Combine(store, "blobs", OllamaInstaller.BlobFileName(registry.ModelDigest));
            Assert.True(File.Exists(blob), "the verified model blob belongs in the store");
            Assert.True(File.Exists(Path.Combine(store, "manifests", "registry.ollama.ai", "library", "gemma3", "12b")),
                "the manifest is what makes the model visible to Ollama");
            Assert.Contains("sha256:meta", registry.Downloaded);
            Assert.Equal("pretend model weights", File.ReadAllText(file));
        }
        finally
        {
            Directory.Delete(store, recursive: true);
        }
    }

    /// <summary>A downloaded file that is not the manifest's model layer must never be installed.</summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_file_whose_checksum_does_not_match_is_refused()
    {
        var store = Directory.CreateTempSubdirectory("ollama-store-").FullName;
        var file = Path.Combine(store, "gemma3-12b.gguf");
        File.WriteAllText(file, "these are not the bytes the manifest describes");
        try
        {
            var registry = new RecordingRegistry(Encoding.UTF8.GetBytes("the real weights"));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AddToOllamaAction(registry) { StoreRootOverride = store }
                    .ExecuteAsync("gemma3:12b", file, null, CancellationToken.None));

            Assert.Contains("Checksum mismatch", ex.Message);
            Assert.False(Directory.Exists(Path.Combine(store, "manifests")),
                "a mismatched model must never become visible to Ollama");
        }
        finally
        {
            Directory.Delete(store, recursive: true);
        }
    }

    // ---- the resolver's edges ---------------------------------------------

    /// <summary>
    /// A paste that already names a tag has nothing to choose between, so the picker must stay away —
    /// offering a single "12b" checkbox for "gemma3:12b" is noise, and it is what the variant lookup
    /// blocks the Add button on while it runs.
    /// </summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_fully_tagged_model_offers_no_variants()
    {
        var resolver = new OllamaResolver(new RecordingRegistry(new byte[] { 1 }));

        Assert.Null(await resolver.GetVariantsAsync("gemma3:12b", null, CancellationToken.None));
        Assert.Null(await resolver.GetVariantsAsync("https://host/file.zip", null, CancellationToken.None));
    }

    /// <summary>Asked to resolve a link it never claimed, the resolver says so instead of guessing.</summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Resolving_a_link_that_is_not_a_model_reference_is_refused()
    {
        var resolver = new OllamaResolver(new RecordingRegistry(new byte[] { 1 }));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => resolver.ResolveAsync("https://host/file.zip", CancellationToken.None));

        Assert.Contains("not an Ollama model reference", ex.Message);
    }

    /// <summary>Serves a manifest for the given bytes and records which metadata blobs were fetched.</summary>
    private sealed class RecordingRegistry : IOllamaRegistry
    {
        private readonly byte[] _model;
        public string ModelDigest { get; }
        public List<string> Downloaded { get; } = new();

        public RecordingRegistry(byte[] model)
        {
            _model = model;
            ModelDigest = "sha256:" + Convert.ToHexString(SHA256.HashData(model)).ToLowerInvariant();
        }

        public Task<OllamaManifest> GetManifestAsync(OllamaModelRef model, CancellationToken ct) =>
            Task.FromResult(OllamaManifest.Parse($$"""
            {
              "schemaVersion": 2,
              "layers": [
                { "mediaType": "application/vnd.ollama.image.model", "digest": "{{ModelDigest}}", "size": {{_model.Length}} },
                { "mediaType": "application/vnd.ollama.image.template", "digest": "sha256:meta", "size": 4 }
              ]
            }
            """));

        public string BlobUrl(OllamaModelRef model, string digest) => $"https://registry/{digest}";

        public Task DownloadBlobAsync(OllamaModelRef model, string digest, string destinationPath, CancellationToken ct)
        {
            Downloaded.Add(digest);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.WriteAllText(destinationPath, "meta");
            return Task.CompletedTask;
        }
    }
}

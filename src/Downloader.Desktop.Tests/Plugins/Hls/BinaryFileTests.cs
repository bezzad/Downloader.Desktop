using System.Net;
using System.Text;
using Downloader.Desktop.Plugins.Hls;
using Xunit;

namespace Downloader.Desktop.Tests.Plugins.Hls;

/// <summary>
/// The rule that keeps a half-downloaded tool from becoming a permanent failure: a download lands on a
/// temporary path and is moved into place only once complete, and "installed" means present, plausibly
/// sized AND runnable — never merely present.
///
/// This was written after a 23 MB fragment of a 40 MB binary sat under the real name for months, still
/// without its executable bit, failing every extraction with no way back. The assertions are therefore
/// mostly about what is left on disk after something goes wrong.
/// </summary>
public class BinaryFileTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("binfile-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private string WriteFile(string name, long size, bool executable)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, new byte[size]);
        if (executable && !OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_tool_counts_as_installed_only_when_it_could_actually_run()
    {
        Assert.False(BinaryFile.IsUsable(Path.Combine(_dir, "never-downloaded")), "absent");
        Assert.False(BinaryFile.IsUsable(WriteFile("fragment", 4096, executable: true)),
            "a fragment is not a binary, however executable it is");

        var complete = WriteFile("tool", BinaryFile.MinUsableBytes + 1, executable: true);
        Assert.True(BinaryFile.IsUsable(complete));

        if (OperatingSystem.IsWindows())
            return;
        // The executable bit is set only once a download finishes, so its absence is a reliable
        // "this was never completed" marker for installs that are already broken.
        var notExecutable = WriteFile("tool-no-x", BinaryFile.MinUsableBytes + 1, executable: false);
        Assert.False(BinaryFile.IsUsable(notExecutable));
        BinaryFile.MakeExecutable(notExecutable);
        Assert.True(BinaryFile.IsUsable(notExecutable));
    }

    /// <summary>A path the filesystem refuses to answer about is "not usable", not an exception —
    /// this check runs on every extraction, so it must never be the thing that fails.</summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void An_unanswerable_path_is_simply_not_usable()
    {
        var tooLong = Path.Combine(_dir, new string('x', 8000));

        Assert.False(BinaryFile.IsUsable(tooLong));
    }

    /// <summary>Clearing a leftover is best-effort: absent, or locked, must both be survivable.</summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Clearing_a_leftover_tolerates_it_being_absent_or_undeletable()
    {
        BinaryFile.DeleteIfPresent(Path.Combine(_dir, "not-there"));

        var subdir = Path.Combine(_dir, "a-directory");
        Directory.CreateDirectory(subdir);
        BinaryFile.DeleteIfPresent(subdir); // a directory is not a file — must not throw

        Assert.True(Directory.Exists(subdir));
    }

    /// <summary>The happy path still has to leave no temporary file behind.</summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_completed_download_is_moved_into_place_with_nothing_left_over()
    {
        var body = Encoding.UTF8.GetBytes("a downloaded tool");
        using var http = new HttpClient(new StubHandler(HttpStatusCode.OK, body));
        var path = Path.Combine(_dir, "nested", "tool");

        await BinaryFile.DownloadToAsync(http, "https://example.invalid/tool", path, CancellationToken.None);

        Assert.Equal(body, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(path + ".partial"), "the temporary file must be gone");
    }

    /// <summary>
    /// The whole point: a download that dies partway must leave NOTHING at the real path, so the next
    /// attempt re-downloads instead of treating a corpse as installed.
    /// </summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_failed_download_leaves_neither_a_real_file_nor_a_temporary_one()
    {
        using var http = new HttpClient(new StubHandler(HttpStatusCode.InternalServerError, Array.Empty<byte>()));
        var path = Path.Combine(_dir, "tool");

        await Assert.ThrowsAnyAsync<Exception>(() =>
            BinaryFile.DownloadToAsync(http, "https://example.invalid/tool", path, CancellationToken.None));

        Assert.False(File.Exists(path), "a failed download must never appear under the real name");
        Assert.False(File.Exists(path + ".partial"));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _code;
        private readonly byte[] _body;

        public StubHandler(HttpStatusCode code, byte[] body)
        {
            _code = code;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(_code) { Content = new ByteArrayContent(_body) });
    }
}

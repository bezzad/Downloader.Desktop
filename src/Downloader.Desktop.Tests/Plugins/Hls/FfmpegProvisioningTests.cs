using Xunit;
using System.Diagnostics;
using System.Net;
using System.Text;
using Downloader.Desktop.Plugins.Hls;

namespace Downloader.Desktop.Tests.Plugins.Hls;

/// <summary>
/// How ffmpeg gets onto the machine, and what happens when that goes wrong.
///
/// This is the plugin's one external binary, and every failure here used to be permanent: a download
/// interrupted at 23 MB of 40 MB stayed under the real name, without its executable bit, and the
/// existence-only "installed?" check treated that corpse as installed forever — every extraction then
/// failed with no way back. So the interesting assertions are about what is left BEHIND after a
/// failure, not just the happy path.
///
/// No real ffmpeg is downloaded: the HTTP client is handed a stub that serves an archive built here,
/// containing a stand-in "ffmpeg" that the tests can make succeed or fail on demand.
/// </summary>
public class FfmpegProvisioningTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ffmpeg-prov-").FullName;
    private readonly string _emptyPathDir = Directory.CreateTempSubdirectory("empty-path-").FullName;
    private readonly string? _realPath = Environment.GetEnvironmentVariable("PATH");

    public FfmpegProvisioningTests()
    {
        // A box that happens to have ffmpeg installed would short-circuit the download path entirely,
        // which is the path these tests exist to exercise. The stub PATH still has to carry the
        // archive tools: the install shells out to `tar` by bare name, and tar in turn execs `xz`.
        if (TarAvailable)
            foreach (var tool in new[] { "tar", "xz" })
            {
                var real = "/usr/bin/" + tool;
                if (File.Exists(real))
                    File.CreateSymbolicLink(Path.Combine(_emptyPathDir, tool), real);
            }
        Environment.SetEnvironmentVariable("PATH", _emptyPathDir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PATH", _realPath);
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        try { Directory.Delete(_emptyPathDir, recursive: true); } catch { /* best effort */ }
    }

    private static bool TarAvailable =>
        !OperatingSystem.IsWindows() && File.Exists("/usr/bin/tar");

    /// <summary>A stand-in ffmpeg: a script big enough to pass the "plausibly a real binary" size gate.</summary>
    private static string WriteFakeFfmpeg(string path, int exitCode, string stderr = "")
    {
        var padding = new string('#', 1024 * 1024 + 16); // over MinUsableBytes
        var script = $"#!/bin/sh\n>&2 printf '%s' \"{stderr}\"\nexit {exitCode}\n{padding}\n";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, script);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    /// <summary>Packs a directory into the .tar.xz shape the Linux install path expects.</summary>
    private static byte[] TarXz(string sourceDir)
    {
        var archive = Path.Combine(Path.GetTempPath(), "ffmpeg-" + Guid.NewGuid().ToString("N") + ".tar.xz");
        var psi = new ProcessStartInfo("/usr/bin/tar", $"-cJf \"{archive}\" -C \"{sourceDir}\" .")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using (var p = Process.Start(psi)!)
            p.WaitForExit();
        var bytes = File.ReadAllBytes(archive);
        File.Delete(archive);
        return bytes;
    }

    /// <summary>Answers every request with the same body, so the hard-coded static-build URL is never hit.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly byte[] _body;
        public int Requests { get; private set; }

        public StubHandler(byte[] body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_body)
            });
        }
    }

    private FfmpegBinary BinaryServing(byte[] archive, out StubHandler handler)
    {
        handler = new StubHandler(archive);
        return new FfmpegBinary(_dir, new HttpClient(handler));
    }

    /// <summary>The whole first-use path: nothing cached, nothing on PATH, so it downloads and installs.</summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_missing_ffmpeg_is_downloaded_and_installed_on_first_use()
    {
        if (!TarAvailable) return; // the .tar.xz install path needs the system tar

        var staged = Directory.CreateTempSubdirectory("ffmpeg-src-").FullName;
        WriteFakeFfmpeg(Path.Combine(staged, "ffmpeg-7.0-static", "ffmpeg"), exitCode: 0);
        var ffmpeg = BinaryServing(TarXz(staged), out var handler);
        Directory.Delete(staged, recursive: true);

        var exe = await ffmpeg.EnsureFfmpegAsync(CancellationToken.None);

        Assert.True(File.Exists(exe), "the installed ffmpeg must be where it says it is");
        Assert.StartsWith(_dir, exe);
        if (!OperatingSystem.IsWindows())
            Assert.True((File.GetUnixFileMode(exe) & UnixFileMode.UserExecute) != 0,
                "an ffmpeg without its executable bit can never be started");
        Assert.Equal(1, handler.Requests);

        // Nothing may be left behind for the next run to trip over.
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_dir, "ffmpeg-bin"), "*.tar.xz"));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_dir, "ffmpeg-bin"), "*.partial"));

        // Second call is answered from the cache — no second download.
        Assert.Equal(exe, await ffmpeg.EnsureFfmpegAsync(CancellationToken.None));
        Assert.Equal(1, handler.Requests);
    }

    /// <summary>
    /// A truncated archive is the failure that used to become permanent. It must be deleted, and the
    /// message must say the next attempt will re-download rather than blaming the user.
    /// </summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_corrupt_archive_is_deleted_so_the_next_attempt_starts_clean()
    {
        if (!TarAvailable) return;

        var ffmpeg = BinaryServing(Encoding.UTF8.GetBytes("this is not an xz archive"), out _);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ffmpeg.EnsureFfmpegAsync(CancellationToken.None));

        Assert.Contains("corrupt", ex.Message, StringComparison.OrdinalIgnoreCase);
        var binDir = Path.Combine(_dir, "ffmpeg-bin");
        if (Directory.Exists(binDir))
        {
            Assert.Empty(Directory.EnumerateFiles(binDir, "ffmpeg-download*"));
            Assert.Empty(Directory.EnumerateFiles(binDir, "*.partial"));
        }
    }

    /// <summary>An archive that unpacks fine but holds no ffmpeg is just as useless — say which it is.</summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task An_archive_with_no_ffmpeg_inside_is_reported_as_such()
    {
        if (!TarAvailable) return;

        var staged = Directory.CreateTempSubdirectory("ffmpeg-src-").FullName;
        File.WriteAllText(Path.Combine(staged, "README.txt"), "wrong archive");
        var ffmpeg = BinaryServing(TarXz(staged), out _);
        Directory.Delete(staged, recursive: true);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ffmpeg.EnsureFfmpegAsync(CancellationToken.None));

        Assert.Contains("not found in the downloaded archive", ex.Message);
    }

    /// <summary>
    /// A half-written ffmpeg from an interrupted install must not be mistaken for a working one — that
    /// is the bug this whole "usable, not merely present" rule exists for.
    /// </summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_truncated_cached_ffmpeg_is_replaced_rather_than_used()
    {
        if (!TarAvailable) return;

        var fragment = Path.Combine(_dir, "ffmpeg-bin", "ffmpeg");
        Directory.CreateDirectory(Path.GetDirectoryName(fragment)!);
        File.WriteAllText(fragment, "a 23MB-of-40MB fragment, in spirit"); // too small to be real

        var staged = Directory.CreateTempSubdirectory("ffmpeg-src-").FullName;
        WriteFakeFfmpeg(Path.Combine(staged, "ffmpeg"), exitCode: 0);
        var ffmpeg = BinaryServing(TarXz(staged), out var handler);
        Directory.Delete(staged, recursive: true);

        var exe = await ffmpeg.EnsureFfmpegAsync(CancellationToken.None);

        Assert.Equal(1, handler.Requests);
        Assert.True(new FileInfo(exe).Length > 1024 * 1024, "the fragment must have been replaced");
    }

    /// <summary>
    /// ffmpeg failing is the difference between "your download is a playable file" and a silent dud, so
    /// its exit code and the tail of its output have to reach the caller.
    /// </summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_failing_ffmpeg_surfaces_its_exit_code_and_the_tail_of_its_output()
    {
        if (OperatingSystem.IsWindows()) return; // the stand-in is a shell script

        WriteFakeFfmpeg(Path.Combine(_dir, "ffmpeg-bin", "ffmpeg"), exitCode: 3, stderr: "boom");
        var ffmpeg = new FfmpegBinary(_dir);
        var input = Path.Combine(_dir, "in.ts");
        File.WriteAllText(input, "x");

        var remux = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ffmpeg.RemuxAsync(input, Path.Combine(_dir, "out.mp4"), CancellationToken.None));
        Assert.Contains("exited with code 3", remux.Message);
        Assert.Contains("boom", remux.Message);

        var mux = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ffmpeg.MuxAsync(input, input, Path.Combine(_dir, "out2.mp4"), CancellationToken.None));
        Assert.Contains("exited with code 3", mux.Message);
    }

    /// <summary>
    /// The host asks this before offering the plugin as ready. With nothing installed and nothing on
    /// PATH the honest answer is "no" — claiming otherwise means the first real download fails.
    /// </summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_declared_dependency_knows_when_ffmpeg_is_absent_and_when_it_is_not()
    {
        var ffmpeg = new FfmpegBinary(_dir);
        var dependency = ffmpeg.GetDependency();

        Assert.Equal("ffmpeg", dependency.Id);
        Assert.False(dependency.IsAvailable!(), "nothing is installed and PATH is empty");

        if (OperatingSystem.IsWindows()) return;
        WriteFakeFfmpeg(Path.Combine(_dir, "ffmpeg-bin", "ffmpeg"), exitCode: 0);

        Assert.True(dependency.IsAvailable!(), "an installed, runnable ffmpeg counts as available");
    }
}

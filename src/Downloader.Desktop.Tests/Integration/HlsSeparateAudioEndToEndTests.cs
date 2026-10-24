using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Downloader.Desktop.Models;
using Downloader.Desktop.Plugins;
using Downloader.Desktop.Plugins.Hls;
using Downloader.Desktop.Services;
using Xunit;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Downloader.Desktop.ViewModels;
using static Downloader.Desktop.Tests.Integration.DashEndToEndTests;

namespace Downloader.Desktop.Tests.Integration;

public class HlsSeparateAudioEndToEndTests
{
    [Fact(Timeout = TestTimeouts.SlowMs)]
    public async Task A_twitter_like_hls_stream_assembles_on_the_first_run()
    {
        var ffmpeg = FindOnPath("ffmpeg");
        if (ffmpeg is null)
            return; // gated: no ffmpeg on this machine

        using var work = new TempDir();
        var streamDir = Directory.CreateDirectory(Path.Combine(work.Path, "stream")).FullName;
        await RunAsync(ffmpeg,
            "-y -loglevel error -f lavfi -i testsrc=duration=40:size=320x180:rate=15 " +
            "-f lavfi -i sine=frequency=440:duration=40 -map 0:v -map 1:a " +
            "-c:v libx264 -preset ultrafast -g 15 -keyint_min 15 -sc_threshold 0 -b:v 200k -c:a aac -b:a 64k " +
            "-f hls -hls_time 1 -hls_playlist_type vod -hls_segment_type fmp4 " +
            "-hls_fmp4_init_filename \"init_%v.mp4\" -hls_segment_filename \"seg_%v_%03d.m4s\" " +
            "-master_pl_name master.m3u8 -var_stream_map \"v:0,agroup:aud a:0,agroup:aud,default:yes\" \"p_%v.m3u8\"",
            workingDirectory: streamDir);

        using var server = new DirectoryServer(streamDir);
        var plan = await new HlsResolver(new HttpClient())
            .ResolveAsync(server.Url + "master.m3u8", new ResolveOptions(), CancellationToken.None);
        Assert.NotNull(plan);

        var downloadDir = Directory.CreateDirectory(Path.Combine(work.Path, "out")).FullName;
        var persisted = PersistedPlan.From(plan);
        var processor = new HlsPostProcessor(new FfmpegBinary(work.Path));
        var stages = new List<string>();

        var finalPath = await new DownloadManager().ExecutePlanAsync(
            persisted, downloadDir, "clip.mp4", processor,
            onPartService: _ => { }, onStage: stages.Add,
            onProgress: _ => { }, isCancelled: () => false, CancellationToken.None);

        Assert.NotNull(finalPath);
        Assert.True(new FileInfo(finalPath!).Length > 0);
    }

    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public void A_twitter_like_hls_row_completes_on_the_first_run()
    {
        var ffmpeg = FindOnPath("ffmpeg");
        if (ffmpeg is null)
            return;
        Localizer.Instance.Load("en");
        using var work = new TempDir();
        var streamDir = Directory.CreateDirectory(Path.Combine(work.Path, "stream")).FullName;
        Task.Run(() => RunAsync(ffmpeg,
            "-y -loglevel error -f lavfi -i testsrc=duration=40:size=320x180:rate=15 " +
            "-f lavfi -i sine=frequency=440:duration=40 -map 0:v -map 1:a " +
            "-c:v libx264 -preset ultrafast -g 15 -keyint_min 15 -sc_threshold 0 -b:v 200k -c:a aac -b:a 64k " +
            "-f hls -hls_time 1 -hls_playlist_type vod -hls_segment_type fmp4 " +
            "-hls_fmp4_init_filename \"init_%v.mp4\" -hls_segment_filename \"seg_%v_%03d.m4s\" " +
            "-master_pl_name master.m3u8 -var_stream_map \"v:0,agroup:aud a:0,agroup:aud,default:yes\" \"p_%v.m3u8\"",
            workingDirectory: streamDir)).GetAwaiter().GetResult();
        using var server = new DirectoryServer(streamDir);

        var pm = new PluginManager();
        pm.RegisterPlugin(new HlsPlugin());
        var manager = new DownloadManager(pm);
        var config = Config.New();
        var folder = Directory.CreateDirectory(Path.Combine(work.Path, "out")).FullName;
        config.Settings.DefaultSavePath = folder;
        manager.Initialize(config);
        NotificationService.Enabled = false;

        var vm = manager.Add(new DownloadItem { Urls = { server.Url + "master.m3u8" }, SaveFolder = folder, FileName = "master.m3u8" }, autoStart: true);
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (vm.Status is not (DownloadStatus.Completed or DownloadStatus.Failed) && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.Status == DownloadStatus.Completed, $"status {vm.Status}: {vm.ErrorMessage}");
    }
}

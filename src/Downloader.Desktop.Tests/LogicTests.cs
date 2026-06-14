using System.Text.Json;
using Downloader;
using Downloader.Desktop.Models;
using Downloader.Desktop.ViewModels;
using Xunit;

namespace Downloader.Desktop.Tests;

/// <summary>Pure-logic unit tests (no Avalonia runtime needed).</summary>
public class LogicTests
{
    [Theory]
    [InlineData("movie.mp4", "video")]
    [InlineData("clip.MKV", "video")]
    [InlineData("song.mp3", "audio")]
    [InlineData("photo.jpeg", "image")]
    [InlineData("archive.zip", "archive")]
    [InlineData("report.pdf", "document")]
    [InlineData("setup.exe", "app")]
    [InlineData("ubuntu.iso", "disc")]
    [InlineData("noextension", "file")]
    [InlineData("", "file")]
    public void GetFileKind_classifies_by_extension(string name, string expected)
    {
        Assert.Equal(expected, DownloadItemViewModel.GetFileKind(name));
    }

    [Fact]
    public void ToConfiguration_maps_core_options()
    {
        var settings = new DownloadSettings
        {
            ChunkCount = 8,
            ParallelDownload = true,
            MaximumBytesPerSecond = 0, // unlimited
            FileExistPolicy = FileExistPolicy.IgnoreDownload
        };

        var cfg = settings.ToConfiguration();

        Assert.Equal(8, cfg.ChunkCount);
        Assert.True(cfg.ParallelDownload);
        Assert.Equal(FileExistPolicy.IgnoreDownload, cfg.FileExistPolicy);
        // The engine treats <= 0 as unlimited (long.MaxValue).
        Assert.Equal(long.MaxValue, cfg.MaximumBytesPerSecond);
    }

    [Fact]
    public void ToConfiguration_clamps_out_of_range_values()
    {
        var settings = new DownloadSettings { ChunkCount = 0, BlockTimeout = 1, HttpClientTimeout = 1 };
        var cfg = settings.ToConfiguration();

        Assert.True(cfg.ChunkCount >= 1);
        Assert.True(cfg.BlockTimeout >= 100);
        Assert.True(cfg.HttpClientTimeout >= 1000);
    }

    [Fact]
    public void DefaultFileExistPolicy_is_IgnoreDownload()
    {
        Assert.Equal(FileExistPolicy.IgnoreDownload, new DownloadSettings().FileExistPolicy);
    }

    [Fact]
    public void Config_New_has_settings_and_default_queue()
    {
        var cfg = Config.New();
        Assert.NotNull(cfg.Settings);
        Assert.Single(cfg.Queues);
        Assert.Equal(DownloadQueue.DefaultName, cfg.DefaultQueue.Name);
        Assert.NotNull(cfg.Downloads);
    }

    [Fact]
    public void Config_round_trips_through_json()
    {
        var cfg = Config.New();
        cfg.Downloads.Add(new DownloadItem { Url = "https://host/file.zip", SaveFolder = "/tmp", FileName = "file.zip" });

        var json = JsonSerializer.Serialize(cfg);
        var back = JsonSerializer.Deserialize<Config>(json).EnsureValid();

        Assert.NotNull(back.Settings);
        Assert.Single(back.Queues);
        Assert.Single(back.Downloads);
        Assert.Equal("file.zip", back.Downloads[0].FileName);
    }

    [Fact]
    public void DownloadItem_FilePath_combines_folder_and_name()
    {
        var item = new DownloadItem { SaveFolder = "/tmp/dl", FileName = "a.bin" };
        Assert.Equal("/tmp/dl", item.FolderPath);
        Assert.Contains("a.bin", item.FilePath);

        var noName = new DownloadItem { SaveFolder = "/tmp/dl" };
        Assert.Equal("/tmp/dl", noName.FilePath);
    }
}

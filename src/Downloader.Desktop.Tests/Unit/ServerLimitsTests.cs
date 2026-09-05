using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Downloader.Desktop.Tests.Unit;

/// <summary>
/// The app's memory of what each host accepts (issue #14). Discovering a server's limit costs a refused
/// request and the partial file that attempt had gathered, so it is worth remembering — but a remembered
/// number is a hint, and these are the rules that keep it one: the user's ceiling always wins, a stale
/// entry is re-tested rather than obeyed, and anything unreadable simply means "no memory".
/// </summary>
public class ServerLimitsTests
{
    private static readonly DateTime Now = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_host_with_no_history_gets_the_full_ceiling()
    {
        var memory = new Dictionary<string, ServerConnectionLimit>();

        Assert.Equal(8, ServerLimits.ChooseStartingCount(memory, "example.com", 8, Now));
        // …and so does a download whose address cannot be keyed at all.
        Assert.Equal(8, ServerLimits.ChooseStartingCount(memory, null, 8, Now));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_remembered_limit_is_where_the_next_download_starts()
    {
        var memory = new Dictionary<string, ServerConnectionLimit>
        {
            ["mirror.example"] = new() { Connections = 4, LearnedUtc = Now.AddDays(-1) }
        };

        Assert.Equal(4, ServerLimits.ChooseStartingCount(memory, "mirror.example", 8, Now));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_configured_count_is_a_ceiling_the_memory_can_never_raise()
    {
        // The user has since chosen two. A remembered eight must not talk them out of it.
        var memory = new Dictionary<string, ServerConnectionLimit>
        {
            ["mirror.example"] = new() { Connections = 8, LearnedUtc = Now }
        };

        Assert.Equal(2, ServerLimits.ChooseStartingCount(memory, "mirror.example", 2, Now));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_stale_limit_is_re_tested_at_the_ceiling()
    {
        var memory = new Dictionary<string, ServerConnectionLimit>
        {
            ["mirror.example"] = new() { Connections = 2, LearnedUtc = Now - ServerLimits.RetestAfter }
        };

        // Exactly at the interval the entry has expired: a host is not held to one bad minute for ever.
        Assert.Equal(8, ServerLimits.ChooseStartingCount(memory, "mirror.example", 8, Now));
        // A moment before it, it is still trusted — the boundary, either side of it.
        memory["mirror.example"].LearnedUtc = Now - ServerLimits.RetestAfter + TimeSpan.FromMinutes(1);
        Assert.Equal(2, ServerLimits.ChooseStartingCount(memory, "mirror.example", 8, Now));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void An_unreadable_entry_means_no_memory_rather_than_a_broken_download()
    {
        var memory = new Dictionary<string, ServerConnectionLimit>
        {
            ["zero.example"] = new() { Connections = 0, LearnedUtc = Now },
            ["negative.example"] = new() { Connections = -4, LearnedUtc = Now },
            ["null.example"] = null,
        };

        Assert.Equal(8, ServerLimits.ChooseStartingCount(memory, "zero.example", 8, Now));
        Assert.Equal(8, ServerLimits.ChooseStartingCount(memory, "negative.example", 8, Now));
        Assert.Equal(8, ServerLimits.ChooseStartingCount(memory, "null.example", 8, Now));
        Assert.Equal(8, ServerLimits.ChooseStartingCount(null, "any.example", 8, Now));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Settling_below_the_ceiling_records_the_limit_and_reaching_it_releases_the_host()
    {
        var memory = new Dictionary<string, ServerConnectionLimit>();

        ServerLimits.Record(memory, "mirror.example", accepted: 4, ceiling: 8, Now);
        Assert.Equal(4, memory["mirror.example"].Connections);
        Assert.Equal(Now, memory["mirror.example"].LearnedUtc);

        // The host no longer refuses: the lesson is dropped rather than left to slow later downloads.
        ServerLimits.Record(memory, "mirror.example", accepted: 8, ceiling: 8, Now);
        Assert.DoesNotContain("mirror.example", memory);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_download_that_measured_nothing_records_nothing()
    {
        var memory = new Dictionary<string, ServerConnectionLimit>();

        ServerLimits.Record(memory, "mirror.example", accepted: 0, ceiling: 8, Now);
        ServerLimits.Record(memory, host: null, accepted: 4, ceiling: 8, Now);

        Assert.Empty(memory);
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("https://Mirror.Example.COM/file.zip", "mirror.example.com")]
    [InlineData("http://cdn.example:8080/a/b?x=1", "cdn.example")]
    [InlineData("not a url", null)]
    [InlineData("", null)]
    public void A_limit_is_keyed_by_host(string url, string? expected)
        => Assert.Equal(expected, ServerLimits.HostOf(url));

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_recorded_limit_survives_a_save_and_load()
    {
        // It lives in the app's own config, so it comes back through machinery that already exists — and
        // a limit that did not survive a restart would be rediscovered, at the price of a partial file.
        var config = Config.New();
        ServerLimits.Record(config.ServerConnectionLimits, "mirror.example", accepted: 2, ceiling: 8, Now);

        var path = Path.Combine(Path.GetTempPath(), "dldesktop-limits-" + Guid.NewGuid().ToString("N")[..8] + ".json");
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(config));
            var loaded = JsonSerializer.Deserialize<Config>(File.ReadAllText(path)).EnsureValid();

            Assert.Equal(2, ServerLimits.ChooseStartingCount(loaded.ServerConnectionLimits, "mirror.example", 8, Now));
        }
        finally
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_config_written_before_the_store_existed_loads_with_no_memory()
    {
        var loaded = JsonSerializer.Deserialize<Config>("{}").EnsureValid();

        Assert.NotNull(loaded.ServerConnectionLimits);
        Assert.Equal(8, ServerLimits.ChooseStartingCount(loaded.ServerConnectionLimits, "mirror.example", 8, Now));
    }
}

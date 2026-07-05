using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Xunit;


namespace Downloader.Desktop.Tests;

/// <summary>Live repro harness for the author's failing x.com flow (network + real plugin + ffmpeg).
/// Gated: DLDESKTOP_XCOM_REPRO must carry the page URL. Not part of the normal suite.</summary>
public class XcomRepro
{
    [Fact]
    public async Task Repro()
    {
        var url = Environment.GetEnvironmentVariable("DLDESKTOP_XCOM_REPRO");
        if (string.IsNullOrWhiteSpace(url))
            return;
        var log = new System.Text.StringBuilder();
        void W(string line) { log.AppendLine(line); }
        using var _flush = new FlushOnDispose(() =>
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "xcom_repro.log"), log.ToString()));

        var pm = new PluginManager();
        pm.LoadFromDirectory(PluginManager.PluginsRoot);
        W($"plugins: {string.Join(", ", pm.Plugins.Select(p => p.Id))}");

        var manager = new DownloadManager(pm);
        var plan = await manager.ResolvePlanAsync(url, CancellationToken.None);
        Assert.NotNull(plan);
        W($"PLAN: name={plan.SuggestedFileName} post={plan.PostProcess.Kind} recipe={plan.PostProcess.Recipe} parts={plan.Parts.Count}");
        foreach (var p in plan.Parts.Take(4))
            W($"  part kind={p.Kind} size={p.ExpectedSize} url={p.Url[..Math.Min(100, p.Url.Length)]}");
        if (plan.Parts.Count > 4) W($"  ... +{plan.Parts.Count - 4} more");

        var persisted = PersistedPlan.From(plan);
        var dir = Path.Combine(Path.GetTempPath(), "xcom_repro_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var processor = persisted.PostProcessKind != Plugins.PostProcessKind.None
            ? pm.FindPostProcessor(persisted.ToPostProcess())
            : null;
        W($"processor: {(processor == null ? "NONE" : processor.GetType().Name)}");

        try
        {
            var final = await manager.ExecutePlanAsync(persisted, dir,
                persisted.SuggestedFileName ?? "out.mp4", processor,
                _ => { }, s => W($"stage: {s}"), _ => { }, () => false, CancellationToken.None);
            W($"FINAL: {final} exists={File.Exists(final)} size={new FileInfo(final).Length}");
        }
        catch (Exception ex)
        {
            W($"FAILED: {ex.GetType().Name}: {ex.Message}");
            W(ex.ToString());
            throw;
        }
    }
}


internal sealed class FlushOnDispose : IDisposable
{
    private readonly Action _flush;
    public FlushOnDispose(Action flush) => _flush = flush;
    public void Dispose() => _flush();
}

using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Integration;

/// <summary>The MCP server driving a real local API: add, list and stop a download through MCP tools.</summary>
public class McpEndToEndTests
{
    /// <summary>Runs the MCP call off-thread while pumping the dispatcher (the API handlers run on the UI thread).</summary>
    private static string Pump(Func<string> work)
    {
        var task = Task.Run(work);
        var deadline = DateTime.UtcNow.AddSeconds(40);
        while (!task.IsCompleted)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("MCP call did not finish");
        }
        return task.GetAwaiter().GetResult();
    }

    private static JsonElement ToolResult(McpServer mcp, string tool, string args) =>
        JsonDocument.Parse(Pump(() => mcp.Handle(
                $$$"""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"{{{tool}}}","arguments":{{{args}}}}}""")))
            .RootElement.GetProperty("result");

    private static string Text(JsonElement result) => result.GetProperty("content")[0].GetProperty("text").GetString();

    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public void Mcp_tools_add_list_and_cancel_through_the_local_api()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        config.DefaultQueue.IsRunning = false; // nothing must hit the network in this test
        manager.Initialize(config);
        LocalApiService.Manager = manager;
        LocalApiService.Config = config;
        LocalApiService.Start();
        Assert.True(LocalApiService.IsRunning);

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var mcp = new McpServer((method, route, body) => McpServer.SendToApp(client, method, route, body));

            var added = ToolResult(mcp, "add_download", """{"url":"https://host/file.zip","filename":"file.zip","start":false}""");
            Assert.False(added.GetProperty("isError").GetBoolean());
            var id = JsonDocument.Parse(Text(added)).RootElement.GetProperty("id").GetString();
            Assert.Equal("file.zip", Assert.Single(manager.Items).GetItem().FileName);

            var list = ToolResult(mcp, "list_downloads", "{}");
            Assert.Equal(id, JsonDocument.Parse(Text(list)).RootElement[0].GetProperty("id").GetString());

            Assert.False(ToolResult(mcp, "cancel_download", $$"""{"id":"{{id}}"}""").GetProperty("isError").GetBoolean());
            Assert.Equal(global::Downloader.DownloadStatus.Stopped, manager.Items[0].Status);

            // A bad request comes back as a tool error carrying the API's own message.
            var bad = ToolResult(mcp, "pause_download", $$"""{"id":"{{Guid.NewGuid()}}"}""");
            Assert.True(bad.GetProperty("isError").GetBoolean());
            Assert.Contains("no download with id", Text(bad));
        }
        finally
        {
            LocalApiService.Stop();
            LocalApiService.Manager = null;
            LocalApiService.Config = null;
        }
    }
}

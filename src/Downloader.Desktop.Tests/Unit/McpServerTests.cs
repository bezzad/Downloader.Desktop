using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.Unit;

/// <summary>The MCP server's JSON-RPC handling, with the local API replaced by a fake.</summary>
public class McpServerTests
{
    private readonly List<(string Method, string Route, string Body)> _calls = new();
    private (int, string) _answer = (200, """{"ok":true}""");

    private McpServer NewServer() => new((method, route, body) =>
    {
        _calls.Add((method, route, body));
        return _answer;
    });

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private JsonElement Call(string tool, string args) =>
        Parse(NewServer().Handle($$$"""{"jsonrpc":"2.0","id":7,"method":"tools/call","params":{"name":"{{{tool}}}","arguments":{{{args}}}}}"""))
            .GetProperty("result");

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Initialize_reports_tools_capability_and_server_name()
    {
        var res = Parse(NewServer().Handle("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18"}}"""));

        Assert.Equal(1, res.GetProperty("id").GetInt32());
        var result = res.GetProperty("result");
        Assert.Equal("2025-06-18", result.GetProperty("protocolVersion").GetString());
        Assert.True(result.GetProperty("capabilities").TryGetProperty("tools", out _));
        Assert.Equal("downloader", result.GetProperty("serverInfo").GetProperty("name").GetString());
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("2024-11-05", "2024-11-05")] // an older version we know is echoed back
    [InlineData("1999-01-01", McpServer.ProtocolVersion)] // an unknown one gets ours
    public void Initialize_picks_the_protocol_version(string asked, string expected)
    {
        var res = Parse(NewServer().Handle($$$"""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"{{{asked}}}"}}"""));
        Assert.Equal(expected, res.GetProperty("result").GetProperty("protocolVersion").GetString());
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_notification_gets_no_answer()
    {
        Assert.Null(NewServer().Handle("""{"jsonrpc":"2.0","method":"notifications/initialized"}"""));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Tools_list_has_every_download_action()
    {
        var tools = Parse(NewServer().Handle("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}"""))
            .GetProperty("result").GetProperty("tools").EnumerateArray().ToList();

        Assert.Equal(
            new[] { "add_download", "list_downloads", "pause_download", "resume_download", "cancel_download", "retry_download", "remove_download" },
            tools.Select(t => t.GetProperty("name").GetString()));
        var add = tools[0].GetProperty("inputSchema");
        Assert.Equal("url", add.GetProperty("required")[0].GetString());
        Assert.Equal("id", tools[2].GetProperty("inputSchema").GetProperty("required")[0].GetString());
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Add_download_posts_the_arguments_to_api_add()
    {
        _answer = (201, """{"id":"abc","name":"f.zip","status":"Running"}""");

        var result = Call("add_download", """{"url":"https://host/f.zip","filename":"f.zip","start":false}""");

        var (method, route, body) = Assert.Single(_calls);
        Assert.Equal(("POST", "add"), (method, route));
        var sent = Parse(body);
        Assert.Equal("https://host/f.zip", sent.GetProperty("url").GetString());
        Assert.False(sent.GetProperty("start").GetBoolean());
        Assert.False(result.GetProperty("isError").GetBoolean());
        Assert.Contains("\"id\":\"abc\"", result.GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void List_downloads_gets_api_list()
    {
        _answer = (200, "[]");
        var result = Call("list_downloads", "{}");

        Assert.Equal(("GET", "list", (string)null), Assert.Single(_calls));
        Assert.Equal("[]", result.GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("pause")]
    [InlineData("resume")]
    [InlineData("cancel")]
    [InlineData("retry")]
    [InlineData("remove")]
    public void Control_tools_post_the_id_to_their_route(string verb)
    {
        Call($"{verb}_download", """{"id":"9f8b4c1e-0000-0000-0000-000000000000"}""");

        var (method, route, body) = Assert.Single(_calls);
        Assert.Equal(("POST", verb), (method, route));
        Assert.Equal("9f8b4c1e-0000-0000-0000-000000000000", Parse(body).GetProperty("id").GetString());
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void An_api_error_is_a_tool_error_with_the_api_message()
    {
        _answer = (404, """{"error":"no download with id x"}""");
        var result = Call("pause_download", """{"id":"x"}""");

        Assert.True(result.GetProperty("isError").GetBoolean());
        Assert.Contains("no download with id", result.GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void An_unreachable_app_is_a_tool_error_that_says_how_to_fix_it()
    {
        _answer = (0, null);
        var result = Call("list_downloads", "{}");

        Assert.True(result.GetProperty("isError").GetBoolean());
        Assert.Contains("not reachable", result.GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void An_unknown_tool_is_a_tool_error_and_calls_nothing()
    {
        var result = Call("format_disk", "{}");

        Assert.True(result.GetProperty("isError").GetBoolean());
        Assert.Empty(_calls);
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("""{"jsonrpc":"2.0","id":3,"method":"nope"}""", -32601)]
    [InlineData("not json", -32700)]
    public void Bad_requests_get_json_rpc_errors(string line, int code)
    {
        var res = Parse(NewServer().Handle(line));
        Assert.Equal(code, res.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Run_answers_each_request_line_and_skips_notifications()
    {
        var input = new StringReader("""
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}
            {"jsonrpc":"2.0","method":"notifications/initialized"}

            {"jsonrpc":"2.0","id":2,"method":"ping"}
            """);
        var output = new StringWriter();

        NewServer().Run(input, output);

        var lines = output.ToString().Split('\n', System.StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Equal(2, Parse(lines[1]).GetProperty("id").GetInt32());
    }
}

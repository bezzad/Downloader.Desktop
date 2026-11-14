using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Downloader.Desktop.Services;

/// <summary>
/// A small MCP (Model Context Protocol) server over stdio, started with <c>Downloader mcp</c>.
/// AI agents (and any other MCP client) launch it and call its tools; each tool is one request to the
/// running app's local API (<see cref="LocalApiService"/>), so the MCP server adds no new behaviour of
/// its own. Messages are JSON-RPC 2.0, one per line.
/// </summary>
public sealed class McpServer
{
    /// <summary>Newest protocol version we speak; an older one the client asks for is echoed back.</summary>
    public const string ProtocolVersion = "2025-06-18";
    private static readonly string[] KnownVersions = { "2024-11-05", "2025-03-26", ProtocolVersion };

    private static readonly string[] ControlVerbs = { "pause", "resume", "cancel", "retry", "remove" };

    private const string NotReachable =
        "Downloader is not reachable. Start the Downloader app and make sure the Browser extension and local API option is on in Settings.";

    /// <summary>Sends one request to the local API: (HTTP method, route after <c>/api/</c>, JSON body or null).
    /// Returns the status code and body; status 0 = the app could not be reached.</summary>
    private readonly Func<string, string, string, (int Status, string Body)> _api;

    public McpServer(Func<string, string, string, (int Status, string Body)> api) => _api = api;

    /// <summary>Reads requests from <paramref name="input"/> until it ends and writes the answers.</summary>
    public void Run(TextReader input, TextWriter output)
    {
        while (input.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            var answer = Handle(line);
            if (answer == null)
                continue;
            output.WriteLine(answer);
            output.Flush();
        }
    }

    /// <summary>Handles one JSON-RPC message. Returns the answer, or null for a notification.</summary>
    public string Handle(string line)
    {
        JsonObject msg;
        try { msg = JsonNode.Parse(line) as JsonObject; }
        catch (JsonException) { msg = null; }
        if (msg == null)
            return Error(null, -32700, "Parse error");

        var id = msg["id"]?.DeepClone();
        var method = (string)msg["method"];
        if (id == null)
            return null; // a notification (e.g. notifications/initialized) gets no answer

        var p = msg["params"] as JsonObject;
        return method switch
        {
            "initialize" => Result(id, Initialize(p)),
            "ping" => Result(id, new JsonObject()),
            "tools/list" => Result(id, new JsonObject { ["tools"] = Tools() }),
            "tools/call" => Result(id, CallTool((string)p?["name"], p?["arguments"] as JsonObject)),
            _ => Error(id, -32601, $"Method not found: {method}")
        };
    }

    private static JsonObject Initialize(JsonObject p)
    {
        var asked = (string)p?["protocolVersion"];
        return new JsonObject
        {
            ["protocolVersion"] = Array.IndexOf(KnownVersions, asked) >= 0 ? asked : ProtocolVersion,
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = "downloader",
                ["version"] = UpdateService.CurrentVersion.ToString()
            }
        };
    }

    private static JsonArray Tools()
    {
        var tools = new JsonArray
        {
            Tool("add_download",
                "Add a download to the Downloader app. It starts right away unless start is false. Returns the new download's id.",
                new JsonObject
                {
                    ["url"] = Prop("string", "Absolute http or https link to download."),
                    ["filename"] = Prop("string", "File name to save as. Optional; found from the link when empty."),
                    ["path"] = Prop("string", "Absolute folder to save into. Optional; the app's default folder when empty."),
                    ["queue"] = Prop("string", "Queue name or id. Optional; the default queue when empty."),
                    ["start"] = Prop("boolean", "false = add it but do not start it yet. Default true.")
                },
                "url"),
            Tool("list_downloads",
                "List all downloads with id, name, url, status, progress (0-100), size, downloaded bytes, speed (bytes/s), folder, file path and queue.",
                new JsonObject())
        };
        foreach (var verb in ControlVerbs)
            tools.Add(Tool($"{verb}_download", ControlDescription(verb),
                new JsonObject { ["id"] = Prop("string", "The download id (from list_downloads or add_download).") },
                "id"));
        return tools;
    }

    private static string ControlDescription(string verb) => verb switch
    {
        "pause" => "Pause a running download.",
        "resume" => "Resume a paused or stopped download.",
        "cancel" => "Stop a download. The partial file is kept, so it can be resumed later.",
        "retry" => "Start a failed or stopped download again.",
        _ => "Remove a download from the list. A finished file on disk is not deleted."
    };

    private static JsonObject Tool(string name, string description, JsonObject properties, string required = null)
    {
        var schema = new JsonObject { ["type"] = "object", ["properties"] = properties };
        if (required != null)
            schema["required"] = new JsonArray(required);
        return new JsonObject { ["name"] = name, ["description"] = description, ["inputSchema"] = schema };
    }

    private static JsonObject Prop(string type, string description) =>
        new() { ["type"] = type, ["description"] = description };

    private JsonObject CallTool(string name, JsonObject args)
    {
        args ??= new JsonObject();
        (int Status, string Body) answer;
        if (name == "add_download")
            answer = _api("POST", "add", args.ToJsonString());
        else if (name == "list_downloads")
            answer = _api("GET", "list", null);
        else if (name?.EndsWith("_download") == true && Array.IndexOf(ControlVerbs, name[..^9]) >= 0)
            answer = _api("POST", name[..^9], new JsonObject { ["id"] = args["id"]?.DeepClone() }.ToJsonString());
        else
            return ToolResult($"Unknown tool: {name}", isError: true);

        return answer.Status == 0
            ? ToolResult(NotReachable, isError: true)
            : ToolResult(answer.Body, isError: answer.Status >= 400);
    }

    private static JsonObject ToolResult(string text, bool isError) => new()
    {
        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
        ["isError"] = isError
    };

    private static string Result(JsonNode id, JsonNode result) =>
        new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result }.ToJsonString();

    private static string Error(JsonNode id, int code, string message) =>
        new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message }
        }.ToJsonString();

    // ---------------- stdio entry point ----------------

    /// <summary>Runs the server on this process's stdin/stdout until the client closes stdin.</summary>
    public static int RunStdio()
    {
        // UTF-8 without a BOM: a BOM in front of the first message breaks JSON parsing in the client.
        var utf8 = new UTF8Encoding(false);
        using var input = new StreamReader(Console.OpenStandardInput(), utf8);
        using var output = new StreamWriter(Console.OpenStandardOutput(), utf8);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        new McpServer((method, route, body) => SendToApp(client, method, route, body)).Run(input, output);
        return 0;
    }

    private static int _lastPort;

    /// <summary>Sends one request to the running app, trying the port that worked last time first.</summary>
    internal static (int, string) SendToApp(HttpClient client, string method, string route, string body)
    {
        foreach (var port in CandidatePorts())
        {
            try
            {
                using var request = new HttpRequestMessage(new HttpMethod(method), $"http://127.0.0.1:{port}/api/{route}");
                if (body != null)
                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                using var response = client.Send(request);
                _lastPort = port;
                using var reader = new StreamReader(response.Content.ReadAsStream());
                return ((int)response.StatusCode, reader.ReadToEnd());
            }
            catch (Exception)
            {
                // not reachable on this port — try the next one
            }
        }
        return (0, null);
    }

    private static IEnumerable<int> CandidatePorts()
    {
        var last = _lastPort;
        if (last != 0)
            yield return last;
        foreach (var port in CliRunner.ResolveCandidatePorts())
            if (port != last)
                yield return port;
    }
}

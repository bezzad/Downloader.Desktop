using System;
using System.Linq;

namespace Downloader.Desktop.Services;

/// <summary>A parsed CLI invocation. <see cref="Error"/> non-null = usage problem (exit code 2).</summary>
public sealed class CliCommand
{
    public string Verb { get; set; }
    /// <summary>The add request for the <c>add</c> verb.</summary>
    public ApiAddRequest Add { get; set; }
    /// <summary>The download id for control verbs (pause/resume/cancel/retry/remove).</summary>
    public string Id { get; set; }
    public string Error { get; set; }
}

/// <summary>
/// Pure command-line parser for the app's CLI verbs (issue #2):
/// <c>add --url … [--filename …] [--path …] [--queue …] [--no-start]</c>, <c>list</c>, and
/// <c>pause|resume|cancel|retry|remove &lt;id&gt;</c>. Anything else — a bare URL, --minimized,
/// --cli-add, no args — is NOT a CLI invocation and falls through to the normal GUI launch.
/// </summary>
public static class CliParser
{
    /// <summary>Internal switch a spawned GUI instance uses to carry a CLI add payload.</summary>
    public const string CliAddSwitch = "--cli-add";

    private static readonly string[] ControlVerbs = { "pause", "resume", "cancel", "retry", "remove" };

    public const string Usage = """
        Usage:
          Downloader add --url <url> [--filename <name>] [--path <folder>] [--queue <name>] [--no-start]
          Downloader list
          Downloader pause|resume|cancel|retry|remove <id>

        Exit codes: 0 success, 1 error (app not running / API disabled / unknown id), 2 usage error.
        list/pause/resume/cancel/retry/remove need the app running with integration enabled in Settings.
        """;

    /// <summary>Returns false when the args are not a CLI invocation (normal GUI launch).</summary>
    public static bool TryParse(string[] args, out CliCommand cmd)
    {
        cmd = null;
        if (args == null || args.Length == 0)
            return false;

        var verb = args[0].Trim().ToLowerInvariant();
        if (verb == "add")
        {
            cmd = ParseAdd(args);
            return true;
        }
        if (verb == "list")
        {
            cmd = args.Length == 1
                ? new CliCommand { Verb = "list" }
                : new CliCommand { Verb = "list", Error = "'list' takes no arguments" };
            return true;
        }
        if (ControlVerbs.Contains(verb))
        {
            if (args.Length != 2 || !Guid.TryParse(args[1], out _))
                cmd = new CliCommand { Verb = verb, Error = $"'{verb}' needs exactly one download id (see 'list')" };
            else
                cmd = new CliCommand { Verb = verb, Id = args[1] };
            return true;
        }

        return false; // not a CLI verb — GUI launch (bare URL, --minimized, --cli-add, …)
    }

    private static CliCommand ParseAdd(string[] args)
    {
        var req = new ApiAddRequest();
        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--url": req.Url = Next(args, ref i); break;
                case "--filename": req.Filename = Next(args, ref i); break;
                case "--path": req.Path = Next(args, ref i); break;
                case "--queue": req.Queue = Next(args, ref i); break;
                case "--mirror": req.Mirrors.Add(Next(args, ref i)); break;
                case "--no-start": req.Start = false; break;
                default:
                    return new CliCommand { Verb = "add", Error = $"unknown option '{args[i]}'" };
            }
        }

        var parsed = ApiAddRequest.FromJson(req.ToJson()); // reuse the API's validation
        return parsed.Error != null
            ? new CliCommand { Verb = "add", Error = parsed.Error }
            : new CliCommand { Verb = "add", Add = parsed };
    }

    private static string Next(string[] args, ref int i) =>
        i + 1 < args.Length ? args[++i] : null;
}

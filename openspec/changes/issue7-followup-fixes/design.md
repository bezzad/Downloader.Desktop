## Context

Three defects reported against v2.5.0 on issue #7. Each was verified against source before this
change was written; none of them is a configuration problem on the reporter's side.

**1. The GET form of `/api/add` drops the request context.**
`ApiAddRequest.FromJson` parses `cookies` and `headers`; `ApiAddRequest.FromQuery`
(`LocalApiService.cs`) parses only `url`, `filename`, `path`, `queue`, `start` and `referer`. The
reporter drives us from a capture tool whose "invoke application" template is a GET URL, so his
cookies have never reached us. Worse, the add still answers `201`, so nothing tells him.

There is a second half to this: what a browser tool has to hand is a **Cookie header string**
(`SID=abc; pref=1`), not the `chrome.cookies.getAll` JSON array our POST body models. Wiring the
parameter up without parsing that shape would still leave him broken.

**2. Pause does not pause a multi-part download.**
`DownloadManager.Pause` pauses `vm.Download`. In a plan run, `vm.Download` is whatever part engine
was published last (`DownloadManager.Plans.cs`: `onPartService?.Invoke(svc)`), and segment plans run
`SegmentParallelism = 4` at a time — so three engines keep transferring. Then the runner loop's only
stop signal is `isCancelled: () => vm.Status == DownloadStatus.Stopped`; `Paused` is invisible to it,
so as each in-flight segment completes the loop starts the next, working through the rest of the
playlist. Meanwhile `DownloadItemViewModel.FlushProgress` drops staged progress for a non-Running
row, so the bar freezes while bytes keep arriving — the "detached from the UI" the reporter
described. `active` (a `ConcurrentBag<DownloadService>`) already exists in the runner for the cancel
path; the pause path simply never learned to use it.

**3. The AES-128 key request ignores the download's context.**
`HlsPostProcessor`'s default key fetcher is `client.GetByteArrayAsync(uri, ct)` over a bare
`HttpClient`. Every segment is fetched with cookies/referer applied; the key — usually served from
the same protected origin as the playlist — is the one request that goes out anonymous, and it
happens at assembly time, i.e. at the very end. That matches "downloaded, failed at around 99%".
This is the likeliest cause but is not proven; the reporter's log would settle it, and the fix is
correct regardless because it is required by the request-context capability either way.

## Goals / Non-Goals

**Goals:**
- A request context supplied through *either* form of `/api/add` reaches the download.
- A caller can tell from the response whether its context was accepted.
- Pause means pause, for every kind of download.
- Every request made on behalf of a download — including assembly-time key fetches — carries its
  context.

**Non-Goals:**
- The browser extension is not touched here. Its own context enrichment belongs to the
  `browser-download-interception` change, which owns the extension surface.
- No authentication or token on the local API (settled earlier: loopback-only, no CORS on `/api/*`).
- No new SDK interface. The post-processor context rides on the existing recipe.
- Not proving root cause 3 from the reporter's log before shipping the fix.

## Decisions

**Accept a request context on the GET query, deliberately, despite secrets-in-URLs.**
Putting cookies in a query string is normally something to refuse: URLs land in logs, history and
referers. Here it is defensible and it is the only thing that unblocks the reporter — the listener is
loopback-only, the "client" is a tool on the user's own machine, and the alternative is a hand-off
that silently does nothing. The mitigations are binding, not optional:
- The request URL, including its query, MUST NOT be logged. `LocalApiService`'s existing error log
  records the route name only (`AppLog.Error($"Local API request failed ({route})", ex)`); that must
  stay true, and no new logging may widen it.
- Cookie and header values are never echoed back in the response — only counts.
- `docs/local-api.md` documents POST as the preferred form and says plainly why.

**Parse the wire shapes, not our internal shapes, in the query form.**
`cookies` = a Cookie-header string, split on `;`, each `name=value` with the first `=` as the
separator, name trimmed, value kept verbatim (it may contain `=`). The cookie's domain is taken from
the **target URL's host** — the caller has no way to give us one per cookie and every cookie a
browser attaches to that URL is by definition valid for that host. `headers` = a newline-separated
`Name: value` block. Both are pure functions, so both are unit-testable without a listener.

**Report accepted context in the `201` body.** Add `cookies` and `headers` counts (and a `referer`
boolean) next to `id`. This is what turns "it silently didn't work" into a two-second diagnosis, and
it costs one line. Counts only — never values.

**Pause the whole active set, and gate the runner on paused as well as cancelled.**
`ExecutePlanAsync` gains an `isPaused` predicate beside `isCancelled`, and publishes the active
engine set (or a pause/resume callback) rather than only the latest engine. The loop checks
`isPaused` before claiming a slot for a new part and waits rather than proceeding. `Pause`/`Resume`
in `DownloadManager` act over that set. Keeping `isCancelled` unchanged preserves the existing
cancel semantics exactly.

*Why not "make Pause set Stopped"*: that would discard the parts folder and lose the work — the
opposite of what pause means.

**Carry the key request's headers on `ConcatRecipe`, not in a new SDK type.**
`ConcatRecipe` already carries `KeyUri`/`IvHex`, and it already round-trips through `PersistedPlan`.
Adding an optional header map there is a non-breaking addition (absent ⇒ today's behavior) and needs
no change to `Downloader.Desktop.Plugins.Abstractions`, so external plugins are unaffected. The
resolver fills it from the `ResolveOptions.Headers` it is already given.

**Cookies reach the plugin as a synthesized `Cookie` header.**
The app applies cookies through `RequestConfiguration.CookieContainer`, which a plugin's own
`HttpClient` never sees, and `DownloadManager.ResolveHeaders` does not include them. So the resolver
must be handed the cookies in header form. Rather than teach `ResolveOptions` about cookies, fold
them into the headers bag as a single `Cookie: name=value; name=value` entry when building
`ResolveOptions` — one place, no SDK change, and it is exactly what the wire expects.

## Risks / Trade-offs

- **Secrets in a URL.** Accepted above, with the no-logging rule as the mitigation. If that rule is
  ever violated the tradeoff stops being defensible — treat "don't log the query string" as part of
  this change's contract, and keep it in mind when touching `LocalApiService` logging.
- **The 99% failure may not be the key fetch.** ffmpeg on the remux is the other candidate. The fix
  is required by the request-context capability regardless, so it is not wasted either way, but the
  issue should not be closed as fixed until the reporter confirms — or his log names the real cause.
- **Pausing a plan mid-part still leaves a partially-downloaded part file.** That is already true
  today and resume handles it; this change does not make it worse, but the part-level resume path is
  worth a test rather than an assumption.
- **A cookie's domain inferred from the target URL is a simplification.** A cookie legitimately scoped
  to a parent domain still works (it is valid for that host); a caller wanting cross-origin cookies
  must use the POST form, which models domain per cookie. Documented rather than solved.

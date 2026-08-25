## 1. Accept a request context on the GET form of `/api/add`

- [x] 1.1 Add a pure `ParseCookieHeader(string cookieHeader, string targetUrl)` to `LocalApiService`
      that splits a `name=value; name=value` Cookie-header string into `CookieDto`s, taking the
      domain from the target URL's host, trimming names, keeping values verbatim (they may contain
      `=`), and skipping entries with an empty name.
- [x] 1.2 Add a pure `ParseHeaderBlock(string block)` that turns a newline-separated `Name: value`
      block into a case-insensitive dictionary, skipping malformed or empty-named lines.
- [x] 1.3 Wire both into `ApiAddRequest.FromQuery` (`cookies`, `headers`), leaving the existing
      `referer` handling as-is. A parse problem must never fail the add.
- [x] 1.4 Unit-test both parsers directly: multiple pairs, values containing `=`, stray whitespace,
      a trailing `;`, an empty string, garbage with no pair, and a header block with a blank line.
- [x] 1.5 Unit-test `FromQuery` end to end — a query carrying cookies + headers + referer produces a
      request whose `BuildItem` yields an item with all three on `item.Request`.

## 2. Tell the caller what was accepted

- [x] 2.1 Extend the `/api/add` `201` body with `cookies` (count), `headers` (count) and `referer`
      (bool) alongside `id`. Counts only — never any value.
- [x] 2.2 Test that the counts reflect what was actually accepted, including the zero case, and that
      no cookie or header value appears anywhere in the response.
- [x] 2.3 Confirm no code path logs the request URL or query string; keep `LocalApiService`'s error
      log to the route name only. Add a comment at the log site saying why, so it is not widened
      later by accident.

## 3. Make Pause actually pause a multi-part download

- [x] 3.1 In `DownloadManager.Plans.cs`, track the set of live part engines and expose it (or a
      pause/resume callback) to the manager instead of publishing only the most recent one.
- [x] 3.2 Add an `isPaused` predicate to `ExecutePlanAsync` beside `isCancelled`; the runner must not
      claim a slot for a new part while it reports true, and must resume cleanly when it goes false.
      Leave `isCancelled` semantics untouched.
- [x] 3.3 Make `DownloadManager.Pause` pause every live part engine, and `Resume` resume them, for a
      plan-backed row. Keep the existing state guards (pause only from Running, etc.).
- [x] 3.4 Test with the loopback plan-runner harness: pause a multi-segment plan mid-run, assert no
      further server requests arrive for new segments after the pause, then resume and assert the
      run completes without re-downloading completed parts.
- [x] 3.5 Test that a paused plan does not keep accumulating bytes behind a frozen progress display
      (the reporter's actual symptom), and that cancel still tears the parts folder down.

## 4. Carry the request context into the AES-128 key fetch

- [x] 4.1 Add an optional header map to `ConcatRecipe` (absent ⇒ current behavior) and make sure it
      round-trips through `PersistedPlan` with older recipes deserializing unchanged.
- [x] 4.2 Fold the download's cookies into the `ResolveOptions.Headers` bag as a single `Cookie:`
      header when `DownloadManager` builds them, so a resolver can pass them on.
- [x] 4.3 Have `HlsResolver` stamp the request headers onto the `ConcatRecipe` it produces, and
      `HlsPostProcessor` send them on the key request instead of using a bare `HttpClient`.
- [x] 4.4 Test with a loopback server that serves the key only when the expected cookie/referer are
      present: the stream assembles with a context and fails without one.
- [x] 4.5 Bump the HLS plugin csproj `<Version>` (standing rule — a stale version means the catalog
      update check never offers the fix).

## 5. Docs, verification and follow-up

- [x] 5.1 Document the GET query's `cookies`/`headers` forms in `docs/local-api.md`, with a worked
      example, and say plainly that POST is preferred and why (secrets in URLs).
- [x] 5.2 Note the new `201` response fields in the same doc.
- [x] 5.3 Full solution build with `-t:Rebuild` reporting `0 Warning(s)`, and a green bounded
      `dotnet test` run.
- [x] 5.4 Append the non-obvious findings to `.claude/skills/downloader-desktop/SKILL.md` — the
      pause-vs-plan-runner trap in particular, since it is invisible from `DownloadManager.Pause`.
- [ ] 5.5 **Author's manual check**: a real gated stream added through the GET query form with real
      cookies, paused mid-download (confirm the network genuinely goes quiet), resumed, and
      completed. Not verifiable headlessly.
- [ ] 5.6 **Blocked on the reporter**: ask for the app log from the encrypted-stream failure and
      confirm whether the key fetch was the ~99% cause. Do not close issue #7 as fixed until then.

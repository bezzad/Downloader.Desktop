## Context

See `proposal.md` — Why. Facts already established, which the implementation must not re-derive:

- **The engine does not fail over between mirrors.** `DownloadPackage.Urls` is load spreading: each chunk
  is pinned to one request instance, and the file-info probe reads `Urls[0]` only. A primary that 4xxs
  fails the whole download while a perfectly good second URL sits unused. This is the single fact the
  v2.8.0 hand-off change got wrong.
- **`DownloadManager.Start` copies `item.Urls` into a LOCAL array** before rewriting `urls[0]` with the
  resolved redirect, so the record always keeps the original pasted/handed-over list. That is what makes a
  per-attempt URL choice possible without mutating the item.
- **`LooksLikeExpiredLinkError`** (DownloadManager.cs:828) currently answers true for **401/403/404/410**
  found by unwrapping the exception chain, and `DescribeFailure` turns that into either
  `Error_BrowserHandoffRefused` (extension hand-off) or `Error_LinkExpiredRefresh`. A 403 caused by
  concurrency lands in exactly that branch today, which is how a working link is reported as expired.
- **The reporter's measurement**: the Softpedia Secure Mirror URL, given directly, succeeds at 1, 2 and 3
  connections and returns 403 at 4+, repeatedly, with the failure starting exactly at 4. Nine other
  download managers succeed with defaults of 6–8 connections. `DownloadSettings.ChunkCount` defaults to
  **8** and is applied to every download.
- **Which link works where** (reporter, v2.7.0 vs v2.8.0): Softpedia External Mirror and ZIP and APKMirror
  all work when the app is given the *end* of the redirect chain and fail when given the clicked page URL.
  GitHub works either way. Softpedia Secure Mirror fails on both, for the concurrency reason above.
- **Neither site can be reached from this machine** (Cloudflare challenge on a datacenter IP), so nothing
  here can be confirmed live locally; tests must reproduce the shapes.

## Goals / Non-Goals

**Goals:**
- Make every URL handed to a download actually get tried, so the choice of which one leads is no longer a
  bet that costs the download when it is wrong.
- Restore what worked in v2.7.0 for External Mirror / ZIP / APKMirror without giving up the expired-link
  recovery v2.8.0 added.
- Stop describing a server's refusal of *this request* as a link that has expired.
- Let a server that dislikes concurrency still succeed.

**Non-Goals:**
- Per-site rules, allow-lists or exception lists. The reporter explicitly argued against special-casing
  APKMirror and they are right: the mechanism should be fixed.
- Changing the engine (`Downloader` package). Failover is orchestrated by the app, which already owns the
  retry loop, rather than by teaching the engine's chunk pinning a new trick.
- A dynamic per-server concurrency model that remembers hosts across runs. One retry with a single
  connection is the whole of it for now.

## Decisions

### 1. Failover lives in `DownloadManager`, over `item.Urls`, not in the engine

`Start` already builds a local `urls` array per attempt. The retry loop gains an index: attempt *n* leads
with `Urls[n]`, keeping the rest as the engine's mirror list (so load spreading still applies when the
lead URL works). A failure that `CanRetryWithAnotherUrl(ex)` approves promotes the next URL and re-attempts,
until the list is exhausted — at most `Urls.Count` attempts total.

*Why:* the app already owns per-attempt state (`PreAttemptSize`, `LinkRefreshAttempts`), the engine's
pinning would have to be redesigned to do this, and an app-side loop is testable against a loopback server
that 403s one path and serves another.

*Which failures qualify:* the same status set the expired-link heuristic uses (401/403/404/410) plus a
connection-level failure. A 404 on the lead URL is precisely the "this address isn't the file" case.

*Alternative rejected:* letting the extension choose perfectly. It cannot — only the app finds out which
address the server actually serves, and only at download time.

### 2. The hand-off leads with the redirect chain's end again

`background.js` reverts to `finalUrl` first, with `item.url` as the fallback — v2.7.0's proven behaviour,
now backed by decision 1 so the other URL is genuinely tried. The clicked link stays on the record as the
address `TryAutoRefreshLink` re-resolves, which is what recovers a genuinely expired signed link.

*Why not keep v2.8.0's order and lean on failover?* Both orders work once failover exists, so the tie is
broken by evidence: the reporter has confirmed `finalUrl` works for three of the four sites, and leading
with it costs one fewer request in the common case.

The choice becomes a pure, tested helper (`handOffUrls(item)` → `{url, mirrors}`) rather than two lines
inside the listener, so the ordering is asserted directly.

### 3. A concurrency refusal is retried with one connection

New pure helper `LooksLikeConcurrencyRefusal(ex, chunksInFlight)`: a 403 (**not** 401/404/410 — those are
about the address, not the request rate) while more than one chunk was in flight. On that, the item is
retried once with `ChunkCount = 1` before it is allowed to fail, tracked per attempt like the link-refresh
counter so it cannot loop.

*Why 403 only:* it is the status a server uses to refuse a request it understood. Widening this to 401/410
would make a genuinely dead link take extra attempts to report.

*Why one connection and not "step down 8→4→2":* the reporter's data shows the threshold is a server
policy, not a gradient, and a single retry keeps the failure fast when the cause is something else. The
setting stays a ceiling; nothing is remembered between downloads.

### 4. Three failures, three messages

`DescribeFailure` currently collapses everything 4xx-shaped into "the link expired". It gains a branch
before that: a concurrency refusal that survived the single-connection retry says the server refused
several connections at once and names the setting to lower. The expired-link and browser-hand-off wordings
are unchanged. New key across all 16 packs.

### 5. What the tests must pin — the mistake, not just the fix

The mistake was believing a second URL would be tried. So the test that matters is behavioural and
end-to-end, not a unit test of the ordering helper:

- A loopback server that **403s the first URL and serves the second** must produce a completed download
  with the file's real bytes. That test fails against today's code and would have failed against v2.8.0
  the day it shipped.
- The reverse (first URL serves, second is dead) must never touch the second, so failover cannot mask a
  working path or double a download's requests.
- A 403 on **every** URL must still fail, once, with a bounded number of attempts — the guard against a
  retry loop.
- A server that 403s while several chunks are in flight and serves a single-connection request must
  complete, and its row must never read "this link is no longer valid".
- The extension's hand-off ordering is asserted directly (`finalUrl` leads, clicked link follows, no
  duplicate when they are equal).

## Risks / Trade-offs

- **Failover doubles the requests for a download whose first URL is genuinely dead** → bounded by the
  number of URLs (in practice two), and only on failures that a different address could fix.
- **A 403 that is really an expired link now costs one extra single-connection attempt** → seconds, and
  the message afterwards is still the expired-link one.
- **Leading with `finalUrl` re-exposes the spent-token case** that v2.8.0 set out to fix → it does not:
  the clicked link is now tried as the fallback, and the record still carries it for `TryAutoRefreshLink`
  to re-resolve. That case is covered by decision 1 rather than by the ordering.
- **None of this can be confirmed against the real sites from here** → every case ships with a loopback
  test reproducing the reported shape; the reporter confirms the live behaviour.

# Design — refresh-expired-link

## R&D: what actually happens when a signed link expires

Verified against the engine source (`../Downloader`, `src/Downloader`) and this app:

- **The failure surfaces as an HTTP status.** `SocketClient.SendRequestAsync` calls
  `EnsureSuccessStatusCode()`, so a `403`/`410` on a chunk request throws `HttpRequestException` with
  `StatusCode` set. `ChunkDownloader` retries it `MaxTryAgainOnFailure` times with backoff **against the
  same URL**, then rethrows; the app receives it on `DownloadFileCompleted` as `e.Error`.
- **The original URL is still intact.** `DownloadManager.Start` copies `item.Urls` into a local array and
  only rewrites *that* copy with the resolved redirect, so re-running `Start` re-resolves the pasted URL from
  scratch and picks up a freshly signed target. This is what makes an automatic refresh cheap.
- **The partial file survives and is reusable.** With `EnableAutoResumeDownload` (app default: on) the engine
  appends chunk metadata to the `.download` file about once a second, and on the next attempt
  `TryResumeFromExistingFile` reads it back. `ClearPackageOnCompletionWithFailure` is off, so a failed
  attempt keeps the file.
- **Resume is gated on the size matching.** `TryResumeFromExistingFile` derives the metadata length from
  `stream.Length - Package.TotalFileSize`, where `TotalFileSize` comes from probing the *new* link. If the new
  link reports a different size the metadata cannot be read, `canContinue` is false, and the engine
  **deletes the partial file** and starts over. That is safe (no corruption) but silently destroys exactly
  what the user was trying to preserve — hence the pre-flight size check on the manual path.
- **Mirrors are not failover.** Each chunk is pinned to one request instance for the whole transfer and the
  file-info probe uses the first URL only, so adding a mirror does not rescue an expired primary. (Full
  evidence in `issue4-followups-batch`.)

## Decisions

**Re-run `Start` rather than resume the in-memory package.** The engine also offers
`DownloadFileTaskAsync(package, urls, ct)`, which would swap the URL on the live package. It only helps
in-session; after a restart there is no package. Re-running the normal start path covers both cases with one
code path and no new engine plumbing, and it re-resolves redirects, applies the request context and re-checks
the queue cap exactly as a normal attempt does.

**Automatic refresh is bounded and only for interrupted downloads.** Conditions, all required: the error is
an expired-link status; the item already has bytes (`Downloaded > 0`), i.e. it is a resume, not a link that
never worked; and fewer than 2 automatic attempts have been made for this item this session. A first-time
link that 403s is a bad link, not an expired one, and must fail immediately with an honest message. The
counter resets when the download completes or when the *user* starts/retries it, so a download that recovers
on Monday can still recover on Tuesday, but a permanently dead link cannot spin.

**Status while refreshing.** The row goes through the existing Failed → queued → Running transitions (that is
how `Retry` re-enters `PumpQueue`), but the message shown is "the link expired — getting a fresh one" rather
than a failure, and no failure notification is raised. Only the exhausted case notifies.

**Pure helpers carry the tests.** `LooksLikeExpiredLinkError(Exception)` (unwraps `AggregateException`,
matches `401/403/404/410`) and `LinkRefreshCheck.Evaluate(knownSize, newSize)` (`Match` / `Unknown` /
`Mismatch`) are static and side-effect free, in the same style as `LooksExpiredOrInvalid` and
`LooksCorruptedAfterResume`.

**The manual path reuses what exists.** The Details window's URL box is already editable for a stopped /
failed / paused row; this change gives it a label, a hint and a "Refresh link" button that validates and
resumes, instead of leaving the user to edit and guess. Probing uses `UrlResolver.ResolveFileInfoAsync` with
the item's own configuration, so a download that needs cookies/headers/referer (issue #7) probes with them.

## Risks

- **A server that reports no size on the new link** cannot be checked. Treated as `Unknown`: the swap
  proceeds, and the engine's own resume check still protects the file (worst case it restarts).
- **404 in the expired set** means a genuinely dead link costs two extra requests before failing. Accepted:
  some CDNs answer 404 for an expired signature.
- **Plan-runner (HLS) and transfer-provider downloads** are out of scope for the automatic path — their
  parts carry their own URLs and are re-resolved by `Retry` already (it clears `PlanJson`).

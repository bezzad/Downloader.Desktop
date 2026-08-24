# Proposal — refresh-expired-link

Issue #6 (@ray2me123, split out of issue #4): a very large file downloaded over several days outlives its
signed / time-limited URL. Give the existing download a fresh link and continue it, instead of losing the
partial file and starting over.

Split out of `issue4-followups-batch`, which holds the shared analysis for #5/#6/#7. This change covers **#6
only**.

## Why

Today the app detects the symptom and leaves the user stranded:

- `LooksExpiredOrInvalid` flags a "completed" download that is small and returns HTML → Failed with
  `Error_LinkExpired`, whose text literally tells the user to re-add the link with a fresh one.
- `LooksCorruptedAfterResume` flags a resume that finishes short of the known size.
- A hard HTTP failure (the common case: the CDN answers `403`/`410` once the signature expires) is reported
  as a generic failure — the user is told to try again, and trying again with the same expired link fails
  the same way.

Two things are missing, one automatic and one manual.

**Automatic.** Most large-file links are a stable page/permalink URL that *redirects* to a short-lived signed
URL. The app already keeps the original URL (`DownloadItem.Urls[0]` is never overwritten — `Start` resolves
redirects into a local copy) and re-resolves it on every attempt, so a fresh signature is one attempt away.
Nothing uses that: an expired-signature failure is terminal, and a download left running overnight is dead by
morning even though the app could have fixed it by itself.

**Manual.** When the pasted link *is* the signed URL there is nothing to re-resolve — the user must supply a
new one. The Details window already lets a stopped/failed row's URL be edited, but it is an unlabelled text
box: nothing says it can be used this way, nothing checks the new link before committing, and nothing
resumes afterwards. Worse, an unchecked swap is unsafe — the engine's file-based resume
(`TryResumeFromExistingFile`) only continues when the new link reports the **same total size**; a link to a
different file silently discards the partial download the user was trying to save.

## What changes

1. **Automatic link refresh.** A failure whose HTTP status says "this link is no longer valid"
   (`401`/`403`/`404`/`410`) on a download that already has bytes on disk triggers a bounded automatic
   retry (2 attempts) that re-resolves the original URL and resumes the partial file. The row reports that
   it is refreshing rather than failing. Only when the retries are exhausted is it marked Failed, with a
   message that names the real problem and points at the manual fix.
2. **A "Refresh link" action in the Details window**, next to the source URL: the user pastes a fresh link
   and presses it. The app probes the new link (with this download's own request context) and compares the
   reported size to the size already known for the file:
   - same size (or the new link does not report one) → the URL is replaced and the download resumes onto the
     existing partial file;
   - different size → a confirmation states that the partial file will be discarded and the download will
     start over, and the user decides;
   - unreachable → nothing is changed and the reason is shown.
3. **Wording** — the failed-row message and the Details hint explain the recovery in end-user terms, in all
   16 language packs.

## What does not change

- No new persisted state: the refreshed URL is the existing `Urls[0]`, already persisted. The automatic
  attempt counter is per session.
- No engine change. Resuming an existing partial file against a different URL is already supported.
- No local-API or browser-extension surface (author's call: app UI only for this round).
- Mirrors keep their current meaning (load spreading, not failover) — see the analysis in
  `issue4-followups-batch`.

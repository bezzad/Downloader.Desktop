> **Read first.** `openspec show fix-handoff-url-and-server-limits`, then `proposal.md` → `design.md` →
> the delta specs. `design.md` — Context lists what has already been measured (by the reporter, on the
> real sites); do not re-derive it, and do not assume this machine can reach those sites.
>
> **The mistake this change exists to correct was an assumption nothing tested**: that handing the app a
> second URL meant something would try it. So group 1's failing test comes FIRST and is behavioural — a
> server that refuses the first address and serves the second must produce a real file. Every task here
> ships with a test that would have caught its bug; "it builds" and "I tried it by hand" are not done.
>
> Groups 1–2 are the regression and ship together, urgently, as a patch release. Group 3 is the Secure
> Mirror finding and can ship with them. Commit per logical step on `develop`.

## 1. A download tries every address it was given

- [ ] 1.1 Write the failing test first: a loopback server that answers **403 on `/a`** and serves real bytes on **/b**, a `DownloadItem` whose `Urls` are `[a, b]`, and an assertion that the download completes with `/b`'s content. Confirm it fails against today's code, and record in the test's comment that this is the v2.8.0 regression's shape.
- [ ] 1.2 Add `DownloadManager.CanRetryWithAnotherUrl(Exception)` (pure, tested): true for 401/403/404/410 and a connection-level failure, false for a cancel, a disk error and a timeout. Unit-test each direction.
- [ ] 1.3 Make `Start`'s attempt loop lead with `Urls[n]` (the rest staying as the engine's mirror list) and promote the next URL when 1.2 approves the failure, at most one leading attempt per URL. Keep the existing `PreAttemptSize` / `LinkRefreshAttempts` behaviour intact.
- [ ] 1.4 Test the two directions failover must NOT break: a working first URL never requests the second as a lead (assert the server saw no `/b` lead request), and every-URL-403 fails once with at most `Urls.Count` leading attempts (assert the count, so a retry loop fails the test rather than the user's machine).
- [ ] 1.5 Test that a single-URL download is completely unaffected — same requests, same failure, same message as before this change.

## 2. The hand-off leads with the address the browser actually fetched

- [ ] 2.1 Move the choice out of the listener into a pure `handOffUrls(item)` in `common.js` returning `{url, mirrors}`, and unit-test it: `finalUrl` leads with the clicked link as the fallback; no `mirrors` when the two are equal; the clicked link is used alone when there is no `finalUrl`; a non-http `finalUrl` is ignored.
- [ ] 2.2 Use it in `background.js`, and keep the record's fallback list in the order 2.1 defines. Verify by test that the app-side `/api/add` turns that body into `Urls` in exactly that order (extend the existing hand-off test rather than writing a parallel one).
- [ ] 2.3 Playwright e2e: a download whose redirect chain ends somewhere the stub app can fetch is handed over and completes; the stub asserts which URL arrived first. This is the case the reporter has confirmed works in the real world.
- [ ] 2.4 Re-check the expired-link recovery still holds with the new order: the clicked link is still on the record, and `TryAutoRefreshLink` still re-resolves it from zero bytes for an extension hand-off (the existing tests must stay green — if one needed changing, say why in the commit).

## 3. A server that refuses concurrency

- [ ] 3.1 Add `DownloadManager.LooksLikeConcurrencyRefusal(Exception, int chunksInFlight)` (pure, tested): true only for 403 with more than one chunk in flight; false for 403 on a single connection, and false for 401/404/410 at any count.
- [ ] 3.2 Retry once with `ChunkCount = 1` when 3.1 approves, bounded per download so it cannot repeat. Test with a loopback server that 403s any request while another is in flight and serves a lone request: the download must complete.
- [ ] 3.3 Test that the retry is not repeated when the single-connection attempt is also refused, and that a normal 403 on a single-connection download is unaffected.
- [ ] 3.4 Add the distinct failure message (a server refusing several connections, naming the setting to lower) to **all 16** `Assets/i18n/*.json` packs; test the message selection for all three cases — concurrency refusal, expired link, extension hand-off — and that no pack is missing the key.

## 4. Close-out

- [ ] 4.1 Full solution rebuild with **0 warnings**, `dotnet test` green, `node --test` green, Playwright `npm test` green — all four.
- [ ] 4.2 Bump the extension version (the hand-off changed) and refresh its README where it describes which link is handed over.
- [ ] 4.3 Update `CLAUDE.md` / `docs/codebase-index.md`, and append to `.claude/skills/downloader-desktop/SKILL.md` the fact this change exists to record: **mirrors are load spreading, not failover** — the app now provides the failover, and any future change to the hand-off ordering must not assume the engine does it.
- [ ] 4.4 Release as a patch version so the reporter can retest, and only then draft the issue #9 reply covering both the regression and the connection finding. **Show the text and wait for the author's explicit OK before posting** (standing rule).
- [ ] 4.5 `/opsx:sync` the delta specs into `openspec/specs/`, then `/opsx:archive` this change.

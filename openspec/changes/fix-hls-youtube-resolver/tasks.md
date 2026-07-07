## 1. Diagnose the actual failure (do this before any fix)

- [x] 1.1 Add a gated (e.g. `DLDESKTOP_NET=1`, matching the existing GitHub-plugin live test pattern) test in `Downloader.Desktop.Tests/Plugins/Hls` that runs `HlsResolver.ResolveAsync` (or `YtDlpBinary.ExtractJsonAsync` directly) against a known-public YouTube video URL, e.g. `https://youtu.be/Wv6LFlehX4k`.
- [x] 1.2 Run it locally and record which outcome occurred: succeeded anonymously; succeeded after a `--cookies-from-browser` retry (which browser); every cookie-browser retry exhausted with no working session; a deno-provisioning warning appeared in the logs; some other yt-dlp stderr. Do not log or persist the video's content — only the structural `DownloadPlan` facts (part count/kind) and the failure category.
- [x] 1.3 Based on the result, confirm (or rule out) the cookie/session hypothesis before proceeding — if the cause is different (e.g. deno), retarget tasks 2-5 below to that actual cause instead.

## 2. Local API: optional cookie hand-off

- [x] 2.1 Extend `ApiAddRequest`/the `/api/add` JSON contract with an optional `cookies` field: a list of `{name, value, domain, path, secure, expires}` objects (the shape `chrome.cookies.getAll` returns).
- [x] 2.2 When present, `LocalApiService` writes the cookies to a short-lived temp file in Netscape cookie-file format, scoped to that one download attempt.
- [x] 2.3 Thread the temp cookie-file path through to the plugin's resolve call for that download (extend `ILinkResolver`/`IYtDlp`'s contract minimally, or pass it via the existing per-request headers/context mechanism — whichever fits the current plugin SDK shape with the least churn).
- [x] 2.4 Delete the temp cookie file in a `finally` immediately after the resolve/download attempt (success or failure).
- [x] 2.5 Unit tests: the temp file is created with the expected Netscape format from a sample cookie list; it is deleted after both a successful and a failed attempt; no cookie value appears in any log output.

## 3. YtDlpBinary: prefer supplied cookies over the browser-file retry loop

- [x] 3.1 When a cookie file is supplied for this resolve call, try `--cookies <file>` FIRST (before the existing anonymous attempt is even needed, or immediately after an anonymous attempt fails — whichever ordering the diagnosis in step 1 suggests is more efficient).
- [x] 3.2 Fall back to the existing `--cookies-from-browser` retry loop if the supplied cookies don't work (e.g. expired since capture).
- [x] 3.3 Unit tests: supplied cookies are tried before the browser loop; a working supplied-cookie file short-circuits the browser loop entirely; an expired/invalid supplied cookie file still falls through to the existing behavior unchanged.

## 4. Browser extension: capture and send cookies for the target URL

- [x] 4.1 Add the `cookies` permission to both `manifest.json` and `manifest.firefox.json`.
- [x] 4.2 When sending a URL to the app (existing `sendToApp`/context-menu/popup flows), also call `chrome.cookies.getAll({url})` for that exact URL and include the result in the `/api/add` payload's new `cookies` field.
- [x] 4.3 Keep the existing URL-only flow working unchanged for sites/requests where cookie capture isn't needed or fails (never block sending the URL on cookie capture failing).
- [x] 4.4 Update the extension's README/`PRIVACY.md` to document the new permission plainly: cookies are read only for a URL the user explicitly sent to the app, sent only to the local app over localhost, never transmitted elsewhere, never logged.
- [x] 4.5 Unit tests (`common.test.js`): cookie capture is attempted for a matching URL; a capture failure doesn't prevent the URL from being sent; the payload shape matches what `LocalApiService` expects.

## 5. Verify YouTube works end-to-end

- [ ] 5.1 Re-run the gated test from task 1 against the same test video — it should now pass (or, if task 1.3 retargeted the fix, against whatever the actual root cause turned out to be).
- [ ] 5.2 Author manual verification: paste a session-gated YouTube video URL into the app (or send it via the updated browser extension) and confirm it downloads and plays.
- [ ] 5.3 Record the diagnosis outcome and the manual verification result in this change before archiving.

## 6. Follow-up (explicitly out of scope for this change)

- [ ] 6.1 Note in this change (not a task to complete here) that broadening the same cookie hand-off to the plugin's other claimed hosts (Instagram, TikTok, Facebook, Vimeo, Twitch, Reddit, Streamable) is separate future work, to be scoped once YouTube is confirmed solid.

## 7. Wrap-up

- [ ] 7.1 Run the full standing verification: `dotnet build Downloader.Desktop.sln`, `dotnet test` (excluding the gated live test in normal CI runs), and the browser extension's `node --test` + Playwright suites.
- [ ] 7.2 Append the diagnosis outcome and any non-obvious gotchas (cookie format quirks, extension permission prompt behavior, yt-dlp argument ordering) to `.claude/skills/downloader-desktop/SKILL.md`.

## 1. Prerequisites (downloader-plugins repo)

- [x] 1.1 Confirm the `bezzad/downloader-plugins` repo + `Downloader.Plugins.Hls` base project exist (per `docs/plugins-hls-torrent-plan.md`); if not, create them and the base direct-`.m3u8` resolver first.
- [x] 1.2 Confirm the SDK (`Downloader.Desktop.Plugins.Abstractions`) is referenced (NuGet or ProjectReference) and the existing `IFfmpeg` provisioning seam is in place to mirror.

## 2. yt-dlp provisioning seam

- [x] 2.1 Define an `IYtDlp` abstraction (run extraction → JSON; ensure-available) so extraction logic is testable without the real binary.
- [x] 2.2 Implement the real `IYtDlp`: download the correct per-OS yt-dlp build into `IPluginContext.DataDirectory` on first use, cache it, set the executable bit on Unix, and reuse on later runs.
- [x] 2.3 Surface clear, logged (`ILogger`) errors when yt-dlp cannot be downloaded or executed.

## 3. Resolver: claim site URLs

- [x] 3.1 Widen `CanResolve` to return true for `.m3u8` AND supported site hosts (x.com / twitter.com status URLs first; general yt-dlp hosts best-effort), with no network I/O.
- [x] 3.2 Add fast host/path matching helpers + unit tests (x.com true, twitter.com true, `.m3u8` true, plain file URL false, no network).

## 4. Resolver: extract + build plan

- [x] 4.1 In `ResolveAsync`, branch: direct media/`.m3u8` → existing HLS parse (unchanged, no yt-dlp); site page → run `yt-dlp -J` via `IYtDlp`.
- [x] 4.2 Parse the yt-dlp JSON: title (→ `SuggestedFileName`), formats, real stream URL(s), required request headers (cookies/referer).
- [x] 4.3 Format selection (D4): prefer single progressive MP4 → one `DownloadPart`; else HLS `.m3u8` → reuse the segment pipeline; else best video+audio → two parts.
- [x] 4.4 Build the `DownloadPlan`: `Parts` (with `Kind`/`Headers`/`ExpectedSize` when known), `PostProcess` (`Concat` for HLS, `Mux` for video+audio, `None` for single progressive), `SuggestedFileName`.
- [x] 4.5 Error handling: no media / private / extractor failure → throw a clear user-readable message (do not return a zero-part plan); log via `ILogger`.

## 5. Post-process reuse

- [x] 5.1 Confirm the existing HLS `IPostProcessor` handles both `Concat` (HLS segments) and `Mux` (video+audio) recipes produced by the extractor; extend the recipe only if needed.

## 6. Tests (standalone, no host, no network)

- [x] 6.1 `CanResolve` matrix tests (task 3.2).
- [x] 6.2 Extraction tests: stub `IYtDlp` with canned x.com `-J` JSON fixtures → assert the produced `DownloadPlan` (parts, kinds, headers, post-process, suggested name) for: progressive MP4, HLS, and video+audio cases.
- [x] 6.3 Direct `.m3u8` regression test: yt-dlp is NOT invoked and segment parsing is unchanged.
- [x] 6.4 Failure tests: empty/no-media JSON → clear throw; simulated provisioning failure → clear throw.
- [x] 6.5 Loadability test: plugin loads in a throwaway `AssemblyLoadContext` and is recognized as `IDownloaderPlugin` (mirrors the host loader).

## 7. Docs & wrap-up

- [x] 7.1 Update `docs/plugins-hls-torrent-plan.md`: site extraction is now part of the HLS plugin (yt-dlp), not a separate future plugin.
- [x] 7.2 Plugin README: how to install, that yt-dlp/ffmpeg auto-download on first use, x.com supported + other sites best-effort, and the public-media-only limitation.
- [ ] 7.3 Manual end-to-end check (after host Phase-2 wiring): paste an x.com video URL in the app → it downloads and plays. Note the result in this change before archiving.
  > 2026-07-04: blocked on the host's Phase-2 pipeline (resolver/post-process wiring in `Downloader.Desktop`), which doesn't exist yet — the plugin side is done (56 tests green on branch `feat/video-site-extraction` in `Downloader.Plugins`). Do this check + archive once Phase-2 lands.

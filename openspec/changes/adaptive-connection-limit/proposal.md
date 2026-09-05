## Why

The connection count in Settings is treated as the number to use for every download, and some servers accept
fewer. Since v2.9.0 a download refused while several connections are open is retried over a single connection
against the address that answered, so it no longer fails ([#9](https://github.com/bezzad/Downloader.Desktop/issues/9))
— but that recovery is per-attempt and all-or-nothing. A server that would happily serve four connections is
downloaded over one (roughly four times slower), and nothing is remembered, so every later download from that
same host repeats the refusal, throws its partial file away, and starts again from zero.

The reporter measured exactly this on one mirror — refused at 4+ connections, served at 1 — and asked for the
setting to behave as a ceiling rather than a fixed number ([#14](https://github.com/bezzad/Downloader.Desktop/issues/14)).

## What Changes

- **Step down instead of collapsing to one.** A concurrency refusal halves the count for the next attempt
  (8 → 4 → 2 → 1) so the download settles at the highest count the server actually accepts, instead of paying
  the single-connection price for a server that only objected to eight.
- **Remember the limit per host.** A host that refused a count has that limit recorded, so the next download
  from the same host starts at a count it is known to accept rather than spending a refused attempt to
  rediscover it. The memory is a hint, not a verdict: it is revisited so a server that was strict last week is
  not held to it forever.
- **The Settings number becomes a ceiling.** Downloads from servers that accept it are unchanged — same count,
  same speed, no extra requests.
- **The attempt budget stays bounded and visible.** Each step discards the partial file (a resumed download
  keeps its original chunk layout), so the number of attempts a download may spend is capped and what the app
  is doing is reported honestly in the row's status rather than looking like a stall.

## Capabilities

### New Capabilities
- `server-connection-limits`: discovering, remembering and reusing the number of simultaneous connections a
  host accepts, including how the memory is stored, when it is trusted, and when it is re-tested.

### Modified Capabilities
- `link-refresh`: the concurrency-refusal recovery changes from "retry once over a single connection" to
  "step down to the highest accepted count", and the bound moves from one retry per address to a capped
  sequence of attempts.

## Impact

- `src/Downloader.Desktop/Services/DownloadManager.cs` — `TryReduceConnections`, `HandleFailure` ordering,
  `ConnectionsInFlight`, `DiscardPartialFile`, and the `Start` path that applies `ForceSingleConnection`.
- `src/Downloader.Desktop/ViewModels/DownloadItemViewModel.cs` — `ForceSingleConnection` becomes a count
  rather than a flag.
- New per-host store (persisted with the app's config) plus its own service seam for testing.
- `src/Downloader.Desktop.Tests/Integration/UrlFailoverTests.cs` and `Unit/UrlAttemptTests.cs` — the existing
  decision tests pin the current one-shot behaviour and must be updated with it.
- Wording: a row that is stepping down needs an honest status string, so all 16 language packs gain a key.
- No engine change: the count is applied through `DownloadConfiguration` exactly as it is today.

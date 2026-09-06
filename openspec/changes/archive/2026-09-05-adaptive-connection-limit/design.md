## Context

`DownloadManager.HandleFailure` offers a failed attempt to three recovery paths in order:
`TryReduceConnections` → `TryNextUrl` → `TryAutoRefreshLink`. Since v2.9.0 the first of those fires on a
403 (or a finished-with-nothing) raised while more than one connection was in flight, sets
`vm.ForceSingleConnection`, discards the partial file, and re-queues the download; `Start` then builds the
engine with `ChunkCount = 1, ParallelDownload = false`. It is a **boolean latch**, bounded to once per
address, skipped when `vm.PreAttemptSize is not null` (a resume), and it remembers nothing.

The discard is not incidental: the engine keeps the chunk layout its package was created with, so a retry
that leaves an eight-chunk partial on disk re-opens the same eight ranges and is refused again. Every step
therefore costs the bytes gathered so far, which is why the number of steps has to be small and why a
resume must never enter this path.

The reporter's evidence is one mirror refusing 4+ connections and serving at 1 ([#9](https://github.com/bezzad/Downloader.Desktop/issues/9)),
and their request is [#14](https://github.com/bezzad/Downloader.Desktop/issues/14).

## Goals / Non-Goals

**Goals:**
- Settle a download at the highest connection count its host accepts, not at 1.
- Stop paying the discovery cost — a refused attempt and a discarded partial — once per download from a
  host we have already measured.
- Keep the failure message honest, and stop telling users to lower a setting the app now manages.
- Leave downloads from unrestricted servers byte-for-byte unchanged: same count, same requests.

**Non-Goals:**
- Probing a server's limit ahead of time. The limit is learned from refusals we were going to receive
  anyway; a speculative probe would add a request to every download to help a small minority of hosts.
- Reacting to slowness. Only an explicit refusal counts — a slow server is not a strict one, and
  throttling ourselves on a timing signal is how a download manager gets quietly worse.
- Per-download configuration. This stays automatic; the Settings number remains the only knob.
- Changing the engine. The count is applied through `DownloadConfiguration`, as today.

## Decisions

**Halving, not a search.** 8 → 4 → 2 → 1 costs at most `log2(ceiling)` attempts (three from the default 8)
and lands within a factor of two of the true limit. A linear walk (8 → 7 → 6 …) would find the exact number
at a cost of up to seven discarded partials, which is a worse trade for a value we cache anyway.

**`ForceSingleConnection` becomes `AttemptConnections` (a nullable int).** The flag cannot express "four",
and a bool beside a count is the kind of pair that drifts apart. `null` means "use the ceiling". `Start`
already captures `vm.PlannedConnections` from the configuration it built; that stays the record of what the
refused attempt actually had open.

**The memory is keyed by host, stored in the app config, and dated.** Host (not full URL) is the unit the
limit belongs to; storing it with `Config` means it survives restart through machinery that already exists,
with no new file format. Each entry carries the count and when it was learned so it can expire — a limit
that never expires would permanently punish a host that had one bad day. Entries are re-tested after an
interval rather than on a schedule, so nothing runs in the background.

**A recorded limit is clamped by the ceiling, never above it.** The user's setting is a maximum, and a
remembered 8 must not override a user who has since chosen 2.

**The step down keeps its position ahead of the address walk.** That ordering is what v2.9.0 fixed: the
address that refused is the only one proven to answer, and spending the other addresses at full concurrency
first left the polite retry aimed at the clicked page link. See the SKILL note "The ORDER of the recovery
paths in `HandleFailure` is load-bearing".

**The resume guard stays exactly as it is.** `vm.PreAttemptSize is not null` → no step down. A 403 on a
resume is the expired-link shape, and that path saves the partial rather than deleting it.

**The row says what happened.** A new localized key (16 packs) for "the server refused several connections;
using fewer" so a download running at 2 instead of 8 is explained rather than mysterious.

## Risks / Trade-offs

- **A wrong lesson gets cached.** A one-off 403 (rate limiting, a bad minute on a CDN) could record a limit
  for a host that had none, quietly slowing later downloads. Mitigated by expiry plus clearing the entry as
  soon as a re-test at the ceiling succeeds; the cost of being wrong is bounded and self-correcting.
- **More attempts per failed download.** Worst case is three reduced attempts instead of one before a
  download fails, each discarding what it had. The cap keeps this bounded, and the per-host memory means it
  is paid once per host rather than once per download.
- **Host is a coarse key.** A CDN that serves several sites from one hostname, or one site across many
  hostnames, will be learned imprecisely. Accepted: the alternative (per-path or per-URL keys) would rarely
  hit the same key twice and so would remember nothing useful.
- **Existing tests encode the one-shot behaviour.** `UrlFailoverTests` and `UrlAttemptTests` assert
  `ForceSingleConnection` directly, so they must be rewritten alongside the change — and rewritten to assert
  the *count the download settled at*, which is the real requirement, rather than the flag.

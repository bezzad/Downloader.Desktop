## Context

`GitHubReleasesResolver` is a **built-in** plugin (bundled with the app, disable-only, version is a string
in code — there is no catalog csproj to bump). Today it is 60 lines: `CanResolve` accepts any github.com URL
with at least two path segments, and `ResolveAsync` re-parses the URL, GETs
`api.github.com/repos/{owner}/{repo}/releases/latest`, and picks the first asset whose name contains
`win`/`osx`/`linux`, else the first asset.

Everything the fix needs on the app side already exists and is already spec'd: `ILinkResolver.GetVariantsAsync`
is a default-implemented interface method (returning null = "no choices"), `ResolveOptions.VariantId` carries
the pick back into `ResolveAsync`, the Add window renders the picker, and `DownloadItem.VariantId` persists it
so a Retry re-resolves the same asset. `link-variants` also already states that variants come only from the
claiming resolver — which is why a GitHub link that offers none currently falls through to the Website
fallback's "Offline copy (.zip)".

Evidence for the defects is a live probe of the shipped plugin, recorded in the proposal's table.

## Goals / Non-Goals

**Goals:**
- The asset a link will download is visible in the Add window, and changeable.
- A link that names a release downloads that release; a link that names a file downloads that file.
- A link that is not about downloading anything is not claimed at all.

**Non-Goals:**
- Authentication. Anonymous `api.github.com` is rate-limited (60/hour per IP) and private repos are not
  supported; adding a token is a separate decision about storing a credential.
- Listing every release. The picker is about *which asset*, not *which version* — the version comes from
  the link the user pasted.
- Source archives (`/archive/refs/tags/x.tar.gz`) and gists. Out of scope; they are ordinary file URLs and
  work today precisely because the plugin will no longer claim them.

## Decisions

**One parser, three callers.** A single pure `GitHubLink.Parse(url)` returns what kind of link it is
(`RepoOrReleases`, `Release(tag)`, `RawFile(owner, repo, ref, path)`, `NotClaimed`) plus owner/repo.
`CanResolve`, `GetVariantsAsync` and `ResolveAsync` all read it. The bug being fixed is precisely that these
three answered from three different readings of the same string, so the fix is to have one reading.

**Claim by SHAPE, and default to not claiming.** The claimed shapes are the repo root, `/releases`,
`/releases/latest`, `/releases/tag/<tag>`, and `/blob|raw/<ref>/<path>`. Everything else under a repo —
`issues`, `pull`, `discussions`, `wiki`, `tree`, `commit`, `actions`, `releases/download/...` — is not
claimed. A resolver that claims a link it cannot improve is worse than one that stays out of the way: the
app downloads the pasted URL correctly on its own, and a wrong claim silently substitutes a different file.

**The tag comes from the path OR the fragment.** GitHub's own releases page links each entry as
`#release-v2.10.0`, which is what the author pasted, so the fragment is read when the path says only
`/releases`. An unrecognised fragment is ignored rather than guessed at, leaving "latest".

**A variant per asset, OS-matched pre-selected.** `LinkVariant.Id` is the asset id (stable, unlike a name),
`Label` the asset name, `Description` its size, `IsDefault` set on the OS match — so the default download is
byte-for-byte what the plugin does today, and the picker only ever *adds* the ability to choose. The
OS-matching rule is extracted as a pure helper so "which asset is for this machine" is testable without a
network call, on every platform (a Windows-only branch that no test can reach is untested code).

**A tag that does not exist is a plain, quotable failure.** `releases/tags/<tag>` 404s; the message names
the tag rather than surfacing `EnsureSuccessStatusCode`'s wording, because a claiming resolver's failure now
reaches the row verbatim.

**One fetch per pick.** `GetVariantsAsync` and `ResolveAsync` both need the release JSON, and the Add window
calls one then the other. A tiny time-boxed cache keyed by the release URL (as the HLS plugin does for
playlists) keeps that to one request, which matters against a 60/hour anonymous rate limit.

## Risks / Trade-offs

- **A narrower claim changes behaviour for links that "worked".** Someone who pasted a repo *issue* link and
  received the latest release got a file, and will now get the page. That was never something they asked
  for, and the new behaviour is what every other site does.
- **Rate limiting.** Listing variants makes the Add window issue a request per pasted GitHub link. Cached
  per release URL and bounded by the same 90 s valve as every other variant lookup; on a 403 the picker
  degrades to "no choices" and the download still resolves as before.
- **Asset ids as variant ids.** Stable and unambiguous, but opaque in a persisted `DownloadItem`. Accepted:
  the label is the name, and an unknown id falls back to the OS-matched asset rather than failing.

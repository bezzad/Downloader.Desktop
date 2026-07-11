# Design — add-window-plugin-badge

## Context
Two facts drive this: (1) `DownloadManager.IsExpiredOrInvalidLink` sniffs a completed file and flags small markup as an expired signed link — correct for `file.zip?token=…`, wrong for a URL that *is* a page (`/docs/`). (2) The Add window already resolves variants asynchronously but shows nothing about which plugin claims the link; `PluginManager.FindResolverPluginId` is cheap and sync (CanResolve pass only, two-pass fallback ordering).

## Goals / Non-Goals
**Goals:** page URLs plain-download successfully; the Add window names the claiming plugin live as the user types/pastes.
**Non-Goals:** badges for transfer schemes the user never pastes; any network probing for the badge (CanResolve only); changing resolver selection.

## Decisions
1. **Heuristic guard, not removal**: `LooksExpiredOrInvalid` stays; the completion path skips it when `UrlLooksLikePage(sourceUrl)` (pure: http(s) + last path segment extension ∈ {none, html, htm, php, asp, aspx, jsp, cfm, shtml} — same shape as the Website plugin's heuristic, duplicated in the app because plugins are optional). Signed CDN links keep real extensions (`.zip`, `.mp4`) so the protection still catches them.
2. **Badge is sync + name-based**: new `PluginManager.FindResolverPluginName(url)` (descriptor Name of the claiming plugin, respecting `IsFallback` ordering). `AddDownloadItemViewModel` takes an optional `getResolverName` func (same seam style as `getVariants`), recomputed on every URL-list change for a single URL; `ResolverName`/`HasResolver` drive a pill badge under the link box, `Add_HandledBy` ("Handled by {0}") in all 16 packs.

## Risks / Trade-offs
- [A real expired link on an extension-less URL now completes with an HTML error page] → rare (signed links carry extensions); the file is small and openable, and the user asked for that URL.
- [Badge shows for a fallback resolver that only *offers a variant* (Website plugin) even when the user picks the plain download] → acceptable: the claim is truthful ("this plugin handles/offers options for this link"); wording stays neutral.

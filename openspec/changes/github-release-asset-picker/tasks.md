## 1. One reading of the link

- [x] 1.1 Add a pure `GitHubLink.Parse(url)` returning the link's kind (repo/releases, a named release, a repository file, or not ours) plus owner/repo, tag and file path — and make `CanResolve` answer from it alone.
- [x] 1.2 Read the release tag from a `/releases/tag/<tag>` path AND from the `#release-<tag>` anchor GitHub puts on its own releases page; an unrecognised anchor leaves "latest".
- [x] 1.3 Stop claiming: a direct `…/releases/download/…` asset URL, and `issues`/`pull`/`discussions`/`wiki`/`tree`/`commit`/`actions` pages.
- [x] 1.4 Unit-test the parser on every shape above, including the exact URLs from the report (the anchor link, the tagged link, the direct asset link, an issue link, a blob link) — pure, offline, no API.

## 2. The asset a link resolves to

- [x] 2.1 Resolve a named release through `releases/tags/<tag>` and a repo/releases link through `releases/latest`; a missing tag fails with a message naming the tag.
- [x] 2.2 Extract the "which asset is for this machine" rule as a pure function over an injected OS, so the Windows and macOS answers are exercised on any box, and unit-test all three.
- [x] 2.3 A release with no assets fails with a plain sentence, not an internal error.
- [x] 2.4 Resolve a repository file link to the raw file (`raw.githubusercontent.com`), keeping the file's own name.

## 3. The picker

- [x] 3.1 Implement `GetVariantsAsync`: one `LinkVariant` per asset (id = asset id, label = name, description = size, `IsDefault` on the OS match); a non-release link offers none.
- [x] 3.2 Honour `ResolveOptions.VariantId` in `ResolveAsync`; an unknown or absent id falls back to the OS-matched asset rather than failing.
- [x] 3.3 Cache the release lookup briefly per release URL so listing then resolving costs one request against the anonymous rate limit.
- [x] 3.4 Test the picker against a stubbed release payload: the OS asset is default, a chosen id wins, an unknown id degrades to the default, and no variants are offered for an unclaimed link.

## 4. Ship it

- [x] 4.1 Bump `GitHubReleasesPlugin.Version` (it is a built-in: the version lives in code, not a catalog csproj).
- [x] 4.2 Keep the live-network test gated behind `DLDESKTOP_NET=1`, and extend it to assert the reported URL now resolves to the OS asset of the release the link names.
- [x] 4.3 Note in `.claude/skills/downloader-desktop/SKILL.md` that the GitHub resolver claims by link shape, reads the tag from path or anchor, and offers assets as variants — and why an over-broad `CanResolve` is worse than none.
- [x] 4.4 `dotnet build Downloader.Desktop.sln -t:Rebuild` — 0 warnings — and `dotnet test` green.
- [ ] 4.5 Verify by hand in the running app with the reported link: the Add window lists the release's assets with the Linux build pre-selected.

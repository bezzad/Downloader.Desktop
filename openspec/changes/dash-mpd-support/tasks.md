# Tasks — dash-mpd-support

Keep build + full `dotnet test` green; commit to `develop` per logical step.

## 1. Manifest model + parser

- [x] 1.1 Fixtures: MPDs covering `SegmentTemplate`+`SegmentTimeline` (`$Time$`), `SegmentTemplate` with
      `$Number%04d$` and no timeline, `SegmentList`, `SegmentBase`/bare `BaseURL`, a live (`dynamic`) one,
      a DRM (`ContentProtection`) one, and one with nested `BaseURL` inheritance.
- [x] 1.2 Tests: `MpdParser` expands each addressing mode into the expected absolute segment URLs, resolves
      `BaseURL` inheritance, substitutes `$RepresentationID$`/`$Bandwidth$`/`$Number%0Nd$`/`$Time$`/`$$`,
      classifies video vs audio, reads the presentation duration, and throws a `DashException` for live and
      for DRM.
- [x] 1.3 `Dash/MpdModels.cs`, `Dash/IMpdParser.cs`, `Dash/MpdParser.cs`, `Dash/DashException.cs`.

## 2. Resolver

- [x] 2.1 Tests: `CanResolve` claims `.mpd` (with query strings) and not `.m3u8`/plain files;
      `GetVariantsAsync` lists video representations highest-bitrate-first with estimated sizes and null for
      a single-quality manifest; `ResolveAsync` emits video parts then audio parts with init segments first,
      stamps headers on every part, and builds a two-group recipe.
- [x] 2.2 `Dash/DashResolver.cs` — variants, plan, playlist cache shared between list and resolve, chosen
      variant via `ResolveOptions.VariantId`, single-file representations as `PartKind.Video`/`Audio`.
- [x] 2.3 Loopback-server test: manifest served over HTTP → plan with absolute segment URLs.

## 3. Multi-stream assembly

- [x] 3.1 Tests: a two-group recipe concatenates each group separately and muxes them; a one-group recipe
      behaves exactly as today (regression); a recipe with no `Streams` field deserializes to one group;
      a wrong input-file count and a three-group recipe fail loudly.
- [x] 3.2 `ConcatRecipe.Streams` + `IntermediateExtension`; `HlsPostProcessor` groups inputs, concatenates
      per group, then remuxes (one group) or muxes (two groups).

## 4. Plugin wiring

- [x] 4.1 Register `DashResolver` in `HlsPlugin.Initialize`; update the plugin name/description; bump the
      csproj `<Version>` to 2.2.0.
- [x] 4.2 Update `packaging/plugins/optional-plugins.json` (name + description mention DASH).
- [x] 4.3 Confirm plugin isolation still holds (`PluginIsolationTests` green — the plugin stays
      optional/catalog-tier and unbundled).

## 5. Browser extension

- [x] 5.1 Tests in `common.test.js`: `.mpd` is a media extension, each `.mpd` URL is its own group.
- [x] 5.2 `common.js` media extensions + `groupKey`; `popup.js` labels a `.mpd` group as DASH.

## 6. Wrap-up

- [x] 6.1 `dotnet build` clean; full `dotnet test` green; `node --test src/browser-extension/common.test.js`
      green; Playwright suite green (`--workers=1`).
- [x] 6.2 Docs: `docs/codebase-index.md` + the project skill note the DASH resolver.
- [x] 6.3 Commit + push on `develop` (`809d937`).
- [ ] 6.4 Author manual check (cannot be automated here): download a real `.mpd` stream end to end, confirm
      the quality picker lists the representations and the produced file plays with both video and audio.

# Tasks — per-download-request-context

Keep build + full `dotnet test` green; commit to `develop` per logical step.

## 1. Model + API surface

- [ ] 1.1 Tests: `ApiAddRequest.FromJson` parses `headers` + `referer`; non-object/non-string entries are dropped without failing the add.
- [ ] 1.2 `Models/RequestContext.cs` (Cookies / Headers / Referer / `IsEmpty`); on `DownloadItem` add `[JsonIgnore] Request` + persisted `Referer`.
- [ ] 1.3 `ApiAddRequest`: `Headers` + `Referer` fields, parsed in `FromJson` (and `referer` in `FromQuery`); `BuildItem` fills `item.Request` (cookies now kept as a list, no temp file written here).
- [ ] 1.4 Test: a `Config` JSON round-trip keeps `Referer` and drops cookies + headers.

## 2. Apply the context to the download

- [ ] 2.1 Tests: `ApplyRequestContext` sets `CookieContainer`, `Headers`, `Referer`; routes `User-Agent`/`Referer`/`Accept`/`Content-Type` to their properties; per-item referer beats the global setting.
- [ ] 2.2 `DownloadManager.ApplyRequestContext(configuration, item.Request)` called in `Start` after the per-item speed cap; cookie file written per attempt from `Request.Cookies` and still deleted in the `finally`.
- [ ] 2.3 Generalize `Plans.ApplyHeaders` to merge item context + part headers (part wins on a key collision); test the merge.

## 3. Resolver path

- [ ] 3.1 SDK: `ResolveOptions.Headers` (init-only, nullable) + doc comment; app folds item headers and referer into it in `ResolvePlanAsync`.
- [ ] 3.2 HLS: replace the three `headers: null` call sites with `options?.Headers` so playlist GETs and every produced `DownloadPart` carry the context. Bump the HLS csproj `<Version>` 2.0.0 → 2.1.0.
- [ ] 3.3 Test: an HLS plan resolved with headers produces parts carrying them.

## 4. Docs + wrap-up

- [ ] 4.1 Document `headers`/`referer` in the local-API docs; note the extension still sends cookies only.
- [ ] 4.2 `dotnet build` clean; full `dotnet test` green; `NoShellSpawnTests` still green.
- [ ] 4.3 Commit + push on `develop`; `/opsx:sync` then `/opsx:archive`.
- [ ] 4.4 Author manual check (cannot be automated here): send a protected `.m3u8` link with cookies + referer through `POST /api/add` and confirm it downloads.

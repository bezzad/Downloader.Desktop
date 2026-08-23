# Design — per-download-request-context

## Shape

One small carrier type, `Models/RequestContext`, hanging off `DownloadItem`:

```csharp
public sealed class RequestContext
{
    public List<CookieDto> Cookies { get; set; } = new();      // transient
    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase); // transient
    public string Referer { get; set; }                        // persisted
    public bool IsEmpty { get; }
}
```

On `DownloadItem`:

- `[JsonIgnore] public RequestContext Request { get; set; }` — the live object, never serialized.
- `public string Referer { get; set; }` — a plain persisted property, mirrored into/out of `Request`.

Why a mirror instead of persisting part of `RequestContext`: `System.Text.Json` has no per-member ignore for
a nested object we also want partially persisted, and `Config` round-trips through STJ. A single persisted
string next to the existing `VariantId`/`ResolverPluginId` properties is the smallest thing that works and
keeps "what lands on disk" obvious at the model level — the security property we care about.

## Cookies: keep the list, not just the file

Today only `CookieFilePath` is kept, and it is deleted after the first attempt. Two reasons to hold the
`CookieDto` list instead:

1. Applying cookies to the **download** needs `CookieContainer` entries, not a Netscape file.
2. The temp file can then be regenerated per attempt, so retry works — today it silently goes anonymous.

`CookieFilePath` stays as the transient per-attempt artifact, written by `BuildItem` exactly as today and
deleted in `Start`'s `finally`. `Start` additionally re-creates it (`EnsureCookieFile`) when the item still
has cookies but the file is gone — that is what makes a retry authenticated. Keeping `BuildItem`'s existing
write means the working add path is untouched.

## Applying to the engine

In `DownloadManager.Start`, after `configuration` is built from `Settings.ToConfiguration()` and the
per-item speed cap is applied:

```
ApplyRequestContext(configuration, item.Request)   // new, pure, unit-tested
```

- **Cookies** → `RequestConfiguration.CookieContainer` (the engine pre-creates one; `??=` covers the rest).
  A `CookieDto` maps to
  `System.Net.Cookie`; a leading-dot domain means include-subdomains. A cookie with no name or domain, or
  one the framework rejects, is skipped rather than failing the download.
- **Headers** → `RequestConfiguration.Headers` via the same try/skip loop `Plans.ApplyHeaders` uses, except
  the four headers the engine models as properties are routed there instead of into the collection:
  `User-Agent` → `UserAgent`, `Referer` → `Referer`, `Accept` → `Accept`, `Content-Type` → `ContentType`.
  Adding those to a `WebHeaderCollection` either throws or is dropped by `SocketClient`.
- **Referer** → `RequestConfiguration.Referer`, overriding the global `DownloadSettings.Referer`.

Precedence is per-item-wins, which matches how the per-item speed cap already behaves.

`ApplyHeaders` in `Plans.cs` is generalized into the same helper so a part's resolver-supplied headers and
the item's context merge in one place (part headers win on a key collision — the resolver knows more about
that specific segment).

## Resolver path

`ResolveOptions` gains `Headers` (`IReadOnlyDictionary<string,string>?`). The app folds the item's headers
**and** referer into it (referer as a normal `Referer` header) so a plugin sees one uniform bag and needs no
new concept. The `referer` field wins over a `Referer` header on both sides of the boundary. `CookieFilePath` stays as-is — plugins that shell out to a tool want a file, not a container.

`HlsResolver` then replaces its three `headers: null` call sites with `options?.Headers`, which stamps them
onto every `DownloadPart` it produces. That is a plugin behavior change → HLS csproj `<Version>` 2.0.0 →
2.1.0 (standing rule: bump on every plugin code change, or the catalog update check never offers the fix).

`ResolveOptions.Headers` is a new **init-only property on an existing type**, so external plugins keep
compiling; nothing implements or overrides it.

## Security

- Cookies and headers are `[JsonIgnore]` and additionally never fed to `AppLog`. The existing rule ("cookies
  are secrets — never persisted/logged") is extended verbatim to headers.
- The API responds with the item id/name/status as before; it never echoes back what it was given.
- No new external surface: same loopback listener, same no-CORS policy on `/api/*`, no URL scheme, no shell.

## Testing

Pure helpers carry the load, matching how this repo tests the rest of the API:

- `ApiAddRequest.FromJson` parses `headers`/`referer`; malformed entries are dropped, not fatal.
- `BuildItem` puts them on the item; referer persists through a `Config` JSON round-trip while headers and
  cookies do not.
- `ApplyRequestContext` sets container/collection/properties correctly, routes the four property-backed
  headers (and keeps them out of the raw collection), and per-item beats global.
- Header merge: part headers win over item headers.
- An HLS test asserting produced parts carry the options' headers.

The end-to-end "a real protected link now downloads" cannot be tested here (needs a live session on a real
site); it is the author's manual check.

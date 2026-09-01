# Extension catalog source

`targets.json` is the **hand-edited half** of `extension-catalog.json`, the asset the release workflow
attaches next to the two extension zips. The app reads that catalog to offer the extension for install
(Settings → Browser extension & local API → Install browser extension).

`scripts/build-extension.sh` fills in the rest: it reads each target's `version` from the manifest named
here and computes the `sha256` of the zip it just built. **Never hand-write a version or a checksum** —
deriving both is what makes it impossible for the catalog to disagree with the assets it describes.

## Fields

| Field | Meaning |
|---|---|
| `id` | Target key. Also the folder the app unpacks into (`<AppData>/Downloader/extension/<id>`). |
| `family` | `chromium` or `gecko`. Decides which install steps and which limitations the app shows. |
| `name` | The browsers this build covers, as shown to the user. |
| `manifest` | Which manifest in `src/browser-extension/` carries this target's version. |
| `assetName` | The zip `scripts/build-extension.sh` produces for this target. |
| `minAppVersion` | Oldest app version whose local API can serve this build. The app hides an entry it is too old for instead of offering a build that would not work. |
| `storeUrl` | The published store listing, or `null`. |

## Publishing a store listing

**`storeUrl` is the whole switch.** Set it here and the app stops offering the manual "load unpacked" path
for that target and starts opening that browser at the listing instead — no application code change, no
release beyond the next one. Leave it `null` until a listing is actually live and public: a dead store link
is worse for the user than the manual steps.

As of this writing both are `null` — no Chrome Web Store or Edge Add-ons listing is published, and the AMO
listing is not linked from the README either (see `src/browser-extension/PUBLISHING.md`).

## Raising `minAppVersion`

Raise it when the extension starts depending on something only a newer app serves (a new `/api/*` route, a
new field on an existing one). Older apps then keep being offered nothing rather than a build that fails
halfway. This mirrors how `packaging/plugins/optional-plugins.json` gates optional plugins.

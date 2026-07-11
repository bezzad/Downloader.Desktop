# website-offline-copy Specification

## Purpose
Saving a web page — or a whole same-host site — as one offline-browsable .zip via the optional Website
plugin (com.bezzad.website-zip): variant trigger, crawl scope/caps, offline link rewriting, transfer
lifecycle, and catalog-tier packaging.
## Requirements
### Requirement: An offline-copy variant is offered for web pages
For a pasted http(s) URL whose path looks page-like and that serves `text/html`, the Website plugin SHALL offer an "Offline copy (.zip)" variant in the Add window's variant picker, alongside (never instead of) the default normal download. The variant SHALL substitute the item URL to a `websitezip:` scheme so the choice is unambiguous across retries and restarts. The content-type check SHALL be bounded (short timeout, HEAD with ranged-GET fallback) and any failure SHALL simply omit the variant — it MUST never block or delay adding the download.

#### Scenario: HTML page offers the variant
- **WHEN** the user pastes an article URL that serves `text/html`
- **THEN** the variant picker shows "Offline copy (.zip)" unchecked and the normal download as the pre-checked default

#### Scenario: Binary file URL offers no variant
- **WHEN** the user pastes a direct link to a `.zip`/`.exe`/image file
- **THEN** no offline-copy variant appears and the add flow is unchanged

#### Scenario: Choosing the variant creates a websitezip item
- **WHEN** the user checks "Offline copy (.zip)" and starts the download
- **THEN** the created item's URL is `websitezip:<original-url>` and it is handled by the Website plugin's transfer

### Requirement: The crawl captures a browsable offline site
Starting a `websitezip:` download SHALL crawl the target: HTML pages on the same host are followed recursively up to a depth and page cap; page requisites (stylesheets, scripts, images, fonts, media — including assets referenced from within CSS via `url(...)`/`@import`) are downloaded from any host, each at most once, up to an asset cap. All captured references SHALL be rewritten to relative local paths so the content browses offline; links to uncaptured pages SHALL keep their absolute original URL. The result SHALL be packaged as a single `.zip` (named after the host) in the user's save folder, and the temporary working files removed. Reaching a cap SHALL end the crawl gracefully — the zip contains everything fetched so far.

#### Scenario: Single page with assets works offline
- **WHEN** a page referencing a stylesheet, an image, and a CSS-declared font is captured
- **THEN** the zip contains the page plus all three assets and the page renders from the zip contents without network access

#### Scenario: Same-host links recurse, cross-host links do not
- **WHEN** the start page links to another page on the same host and to an external site
- **THEN** the same-host page is captured and its link rewritten locally
- **AND** the external site link keeps its absolute URL and that site is not crawled

#### Scenario: Caps bound the crawl
- **WHEN** a site exceeds the page cap during the crawl
- **THEN** crawling stops at the cap, the zip is still produced from the captured content, and the download completes successfully

### Requirement: The crawl behaves like a normal download row
A running crawl SHALL report live progress (fraction done, bytes received, speed) through the standard row UI, and SHALL honor the standard controls: Pause suspends fetching (keeping progress), Resume continues, Cancel stops and removes temporary files. The item SHALL count toward its queue's concurrency cap like any running download. A crawl failure SHALL mark the row Failed with a readable message.

#### Scenario: Pause and resume mid-crawl
- **WHEN** the user pauses a running offline-copy download and later resumes it
- **THEN** no new requests are issued while paused and the crawl continues from where it left off after resume

#### Scenario: Cancel cleans up
- **WHEN** the user cancels a running offline-copy download
- **THEN** the transfer stops, its temporary working directory is deleted, and the row shows Stopped

### Requirement: The Website plugin ships as an optional catalog plugin
The Website plugin SHALL be an optional/catalog-tier plugin: present in the solution for build and test only, never referenced by or bundled with the app, and installable on demand from Settings → Plugins with sha256 verification like other catalog plugins. It SHALL require no external binaries or new package dependencies.

#### Scenario: Fresh install has no Website plugin
- **WHEN** the app is installed fresh
- **THEN** the Website plugin is absent and appears under "More plugins" in Settings once the catalog loads

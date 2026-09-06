## Purpose
Turning a site page a user is watching — YouTube and the like — into a downloadable media file, through an
optional plugin the user installs deliberately, using only the session the user's own browser extension
hands over rather than anything read out of a browser profile.

## ADDED Requirements

### Requirement: Site extraction is an optional plugin, never bundled

The capability SHALL ship as an optional catalog-tier plugin: absent from a fresh install, absent from the
app's own build output, installed only when the user adds it from Settings → Plugins, and verified by
checksum before it is loaded. The main application SHALL NOT gain any dependency on it, and SHALL behave
exactly as it does today when the plugin is not installed.

#### Scenario: A fresh install has no site extraction
- **WHEN** the app is installed and run for the first time
- **THEN** the plugin is not present, and site pages are treated as ordinary URLs

#### Scenario: The user installs it deliberately
- **WHEN** the user adds the plugin from Settings → Plugins
- **THEN** it is downloaded, its checksum is verified before it is loaded, and it appears as a normal removable plugin

#### Scenario: A plugin that fails verification is not loaded
- **WHEN** the downloaded plugin's checksum does not match the catalog's
- **THEN** it is not loaded and the user is told why

### Requirement: A supported site page resolves to a downloadable media file

With the plugin installed, a page URL from a site it supports SHALL be claimed and resolved into one or
more downloadable media parts, with a suggested file name taken from the page's title. Where the site
offers several qualities, they SHALL be offered as selectable variants. A page the plugin cannot extract
SHALL fail with a message naming the reason, and SHALL NOT be reported as a network error.

#### Scenario: A video page downloads as a file
- **WHEN** the user adds a supported site's video page URL
- **THEN** the app downloads the page's media and saves it under a name derived from the page title

#### Scenario: Qualities are offered as variants
- **WHEN** the page offers several qualities or formats
- **THEN** the user is offered one variant per quality before the download starts

#### Scenario: An unextractable page fails with a reason
- **WHEN** the plugin cannot extract the page — it is a live stream, is protected, or the site changed
- **THEN** the download fails with a message naming that reason

### Requirement: A signed-in page is fetched with the session the extension supplies

When a site serves its media only to a signed-in session, the extraction SHALL use the cookies the browser
extension supplied with the link, and the browser's own User-Agent. The app and the plugin SHALL NOT read
cookies, profiles or saved credentials from any browser installation on the machine, under any
circumstance — that is infostealer behaviour and is what the extension exists to avoid. Supplied cookies
SHALL be kept for the download that needs them, never persisted with the download record and never written
to the log.

#### Scenario: A signed-in page downloads
- **WHEN** the page is sent from the extension while the user is signed in on that site
- **THEN** the extraction uses that session's cookies and the download succeeds

#### Scenario: A page pasted by hand with no session says what is missing
- **WHEN** the user pastes the page URL into the app directly and the site refuses an anonymous request
- **THEN** the failure says the site needs the browser session and that sending the page from the extension supplies it
- **AND** it does not tell the user to sign in when the user is already signed in

#### Scenario: No browser data is ever read
- **WHEN** any extraction runs
- **THEN** no browser profile, cookie store or credential store on the machine is read

### Requirement: The extraction tool is fetched and verified, not bundled or trusted blindly

The plugin SHALL obtain the third-party tool it needs on first use rather than bundling it, SHALL verify
what it downloaded before running it, and SHALL run it from an absolute path with arguments it constructs
itself. It SHALL NOT run a command shell, and SHALL NOT be launched by the main application's own process
for any purpose other than this extraction.

#### Scenario: First use fetches the tool
- **WHEN** an extraction runs and the tool is not yet present
- **THEN** it is downloaded, verified, and only then used

#### Scenario: A tool that fails verification is not run
- **WHEN** the downloaded tool does not match its expected checksum
- **THEN** it is discarded, not executed, and the download fails with a clear message

#### Scenario: No shell is ever spawned
- **WHEN** the plugin runs the tool
- **THEN** it is started directly from its absolute path, with no command shell involved

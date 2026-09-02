# extension-install Specification

## Purpose

How a user gets the browser extension from inside the app: which browsers are detected, how a build is
obtained and verified, where it is unpacked, what the app tells the user about the cost of installing it
that way, and how it knows the extension actually arrived.

The boundary this capability exists to hold: **the app never installs an extension into a browser.** No
browser accepts a locally installed unsigned extension into a normal profile, and the one mechanism that
would work — a browser enterprise-policy write — is the browser-hijacker signature, scored higher by
antivirus heuristics precisely because it is elevated. The app fetches, verifies and unpacks; the install
itself happens in the browser, by the user.

## Requirements

### Requirement: The app never installs an extension into a browser
The app SHALL NOT install, register, or force-install a browser extension by any mechanism: it SHALL NOT
write browser extension-registration keys or `External Extensions` JSON files, SHALL NOT write browser
enterprise-policy keys or `policies.json` entries, and SHALL NOT request operating-system elevation for any
part of extension installation. Installing the extension into a browser SHALL remain an action the user
performs in the browser itself.

#### Scenario: No elevation is ever requested
- **WHEN** the user installs the extension through the app by any available path
- **THEN** no operating-system elevation prompt is shown and the whole flow completes with ordinary user rights

#### Scenario: Nothing is written outside the app's data directory
- **WHEN** an extension build is installed
- **THEN** every file created or modified is under the app's own data directory, and no browser profile,
  extension, or policy location is written

### Requirement: Installed browsers are detected without reading browser data
The app SHALL detect which supported browsers are installed on the machine, reporting for each a display
name, a family of either Chromium-based or Gecko-based, and the path to its executable. Detection SHALL
read only the presence and location of the browser. The app SHALL NOT open, read, or enumerate any browser
profile directory, cookie store, saved-credential store, history, or preferences file.

#### Scenario: Installed browsers are listed
- **WHEN** the user opens the install-extension dialog on a machine with supported browsers installed
- **THEN** each installed supported browser is listed once with its name and family

#### Scenario: An absent browser is not listed
- **WHEN** a supported browser is not installed on the machine
- **THEN** it does not appear in the list

#### Scenario: No browser data is read
- **WHEN** detection runs
- **THEN** no browser profile, cookie store, credential store, history, or preferences file is opened

### Requirement: An extension build is downloaded and verified before it is unpacked
The app SHALL obtain the extension build for a chosen browser family from the published release catalog,
and SHALL verify the downloaded archive against the checksum the catalog publishes **before** extracting
any file from it. A checksum mismatch SHALL abort the install leaving nothing extracted, and SHALL be
reported to the user in plain language. An archive entry whose path escapes the destination directory SHALL
abort the install.

#### Scenario: A good build installs
- **WHEN** the user installs the extension for a browser family and the downloaded archive matches the
  published checksum
- **THEN** the files are extracted and the app reports the destination folder

#### Scenario: A tampered or truncated build is refused
- **WHEN** the downloaded archive does not match the published checksum
- **THEN** nothing is extracted, no previously installed copy is disturbed, and the user is told the
  download could not be verified

#### Scenario: A malicious archive entry is refused
- **WHEN** the archive contains an entry whose path resolves outside the destination directory
- **THEN** the install aborts and no file is written outside the destination

#### Scenario: Offline is a clear message, not a silent failure
- **WHEN** the catalog or the archive cannot be fetched
- **THEN** the user is told the build could not be downloaded, and the dialog remains usable

### Requirement: Installed extension files live at a stable per-browser-family path
The app SHALL extract each browser family's build to a fixed location under its own data directory that
does not change between installs, because a browser identifies a manually loaded extension by that path.
Installation SHALL be staged so that an interrupted install never leaves a partially extracted build in
that location.

#### Scenario: Reinstalling uses the same folder
- **WHEN** the user installs the same browser family's build twice
- **THEN** both installs land at the same path

#### Scenario: An interrupted install does not corrupt the installed copy
- **WHEN** an install fails partway through extraction
- **THEN** the previously installed copy at the stable path is either intact or absent, never partially
  overwritten

### Requirement: A build the running app cannot serve is not offered
Each catalog entry SHALL declare the minimum application version it requires, and the app SHALL NOT offer
to install an entry that requires a newer application version than the one running.

#### Scenario: An incompatible build is hidden
- **WHEN** the catalog offers a build whose minimum application version is newer than the running app
- **THEN** that build is not offered for install, and the user is told the app needs updating first

### Requirement: The install dialog states the real cost of each path
For each browser family the dialog SHALL offer the store listing when one is published for that family, and
otherwise the manual load path. When the manual path is offered the dialog SHALL show the destination folder
with a way to copy it and a way to open it, SHALL show the numbered steps for that browser family, and SHALL
state plainly the limitations of a manually loaded extension for that family — including that a manually
loaded extension in a Gecko-based browser is removed when the browser restarts.

#### Scenario: Store listing takes precedence
- **WHEN** a store listing is published for the chosen browser family
- **THEN** the primary action opens that browser at the store listing and the manual steps are secondary

#### Scenario: Manual path shows path, steps and limits
- **WHEN** no store listing is published for the chosen browser family
- **THEN** the dialog shows the destination folder with copy and open actions, the steps for that family,
  and that family's limitations

#### Scenario: The Gecko restart limitation is stated
- **WHEN** the manual path is shown for a Gecko-based browser
- **THEN** the dialog states that the extension is removed when the browser restarts

### Requirement: Connection is confirmed by the extension, not assumed by the app
The dialog SHALL show a browser as connected only after the extension running in that browser has actually
contacted the app. Extracting files SHALL NOT by itself mark a browser as connected.

#### Scenario: Connected appears after the extension calls
- **WHEN** the extension in a browser contacts the app
- **THEN** that browser is shown as connected, with the extension version it reported

#### Scenario: Unpacking alone does not claim success
- **WHEN** the files have been extracted but no request has arrived from that browser
- **THEN** that browser is not shown as connected

### Requirement: An out-of-date extension is reported, never auto-replaced
The app SHALL tell the user when the extension version a browser reports is older than the version
published in the catalog, naming both versions, and SHALL offer the same install path. The app SHALL NOT
replace or modify an extension already loaded in a browser.

#### Scenario: Outdated extension is surfaced
- **WHEN** a browser reports an extension version older than the published one
- **THEN** the app shows that an update is available, naming the installed and available versions

#### Scenario: Up-to-date extension is quiet
- **WHEN** the reported version matches or exceeds the published one
- **THEN** no update prompt is shown

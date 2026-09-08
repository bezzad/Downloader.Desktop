## ADDED Requirements

### Requirement: Only a link that names something downloadable is claimed

The GitHub plugin SHALL claim a link only when it can produce a better download than the link itself: a
repository's releases (its root, its releases list, or one named release) or a file held in the repository.
Any other page under a repository — an issue, a pull request, a discussion, a wiki or a source tree — SHALL
NOT be claimed, and a link that already points directly at a release asset SHALL NOT be claimed, because it
is already the file the user asked for.

#### Scenario: A direct asset link downloads that asset
- **WHEN** a link to a release asset file is pasted
- **THEN** exactly that file is downloaded
- **AND** it is not replaced by an asset from a different release

#### Scenario: A repository page is downloaded as a page
- **WHEN** an issue, pull request, discussion, wiki or source-tree link is pasted
- **THEN** the GitHub plugin does not claim it and the address is downloaded as given

### Requirement: The release named in the link is the release downloaded

A link that names a release SHALL resolve to that release. Only a link that names no release — a repository
root or a bare releases list — SHALL resolve to the latest release.

#### Scenario: A tagged release link downloads that version
- **WHEN** a link naming a specific release is pasted
- **THEN** an asset of that release is downloaded, not of the newest one

#### Scenario: A release page anchor names the release too
- **WHEN** a releases-page link carries the anchor GitHub uses for one release entry
- **THEN** that release is the one resolved

#### Scenario: A repository link still means the latest release
- **WHEN** a plain repository or releases link is pasted
- **THEN** the latest release is resolved, as before

#### Scenario: A release that does not exist is reported plainly
- **WHEN** the link names a release the repository does not have
- **THEN** the download fails with a message naming what was not found

### Requirement: The release's assets are offered as choices

A claimed release link SHALL offer one choice per downloadable asset of that release, each showing the
asset's name and its size, with the asset matching the running operating system chosen by default. Choosing
nothing SHALL download that same default asset, so the behaviour of simply pressing Download is unchanged.

#### Scenario: The user sees which file will be downloaded
- **WHEN** a release link is pasted into the Add window
- **THEN** the release's assets are listed as choices
- **AND** the asset for the running operating system is pre-selected

#### Scenario: A different asset can be picked
- **WHEN** the user selects an asset other than the pre-selected one
- **THEN** that asset is what gets downloaded

#### Scenario: The picked asset survives a retry
- **WHEN** a download of a chosen asset fails and is retried
- **THEN** the same asset is resolved again

#### Scenario: A release with no assets says so
- **WHEN** the named release has no downloadable assets
- **THEN** the user is told the release has no assets, rather than shown an internal error

### Requirement: A file link resolves to the file

A link to a file held in the repository SHALL resolve to that file's contents rather than to the page that
displays it.

#### Scenario: A file page downloads the file
- **WHEN** a link to a file in a repository is pasted
- **THEN** the file's own contents are downloaded, not the surrounding web page

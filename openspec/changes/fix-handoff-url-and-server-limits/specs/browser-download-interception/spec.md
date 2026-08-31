## MODIFIED Requirements

### Requirement: An intercepted download hands over every address the browser had

When the extension takes over a download, it SHALL hand the app both addresses the browser knew: the end
of the redirect chain, which is where the browser was actually fetching the file from, and the link the
user clicked, which can be resolved again if the first has been spent. The address most likely to serve
the file — the end of the chain — SHALL lead, and the other SHALL be handed over as a fallback the app is
required to try (see `link-refresh`). When the two are the same address, only one SHALL be sent.

The ordering is no longer load-bearing: because the app tries both, leading with the wrong one costs a
request rather than the download.

#### Scenario: A redirected download hands over both addresses
- **WHEN** the browser's download went through a redirect before reaching the file
- **THEN** the app is given the end of the chain as the download's address and the clicked link as a fallback

#### Scenario: A download that was not redirected hands over one address
- **WHEN** the clicked link and the address the browser fetched are the same
- **THEN** the app is given that address once, with no duplicate fallback

#### Scenario: The site serves the file from the page's own address
- **WHEN** the file is served from the address the user clicked rather than a separate one
- **THEN** the download still succeeds, because the app tries that address too

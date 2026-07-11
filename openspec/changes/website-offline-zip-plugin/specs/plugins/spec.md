# plugins Specification (delta)

## ADDED Requirements

### Requirement: Fallback resolvers never shadow specific resolvers
The plugin SDK SHALL let an `ILinkResolver` declare itself a fallback via an `IsFallback` property (default-implemented to `false`, so existing and external plugins keep working unchanged). When selecting the resolver for a URL, the host SHALL consult non-fallback resolvers first and consider fallback resolvers only when no non-fallback resolver claims the URL. The same ordering SHALL apply when recording the resolving plugin id for a download.

#### Scenario: Specific plugin wins over a fallback
- **WHEN** a GitHub repository URL is claimed by both the GitHub resolver and a fallback resolver that claims generic web pages
- **THEN** the GitHub resolver performs the resolution

#### Scenario: Fallback handles otherwise-unclaimed pages
- **WHEN** a generic article URL is claimed only by a fallback resolver
- **THEN** that fallback resolver performs the resolution

### Requirement: The host runs plugin-provided transfers end-to-end
When an enabled plugin's `ITransferProvider` claims a download item's URL, the application SHALL run that download through the plugin's `ITransfer` instead of the core HTTP engine: the transfer's progress events drive the row's live progress/speed through the standard staging pipeline, Pause/Resume route to the transfer, Cancel stops it, the item obeys its queue's concurrency cap, and the path returned by the completed transfer becomes the item's final file (marked Completed with its on-disk size). A transfer failure SHALL mark the item Failed with the error message. Transfer selection SHALL happen before link resolution so a claimed scheme never round-trips through resolvers.

#### Scenario: Transfer-backed item completes
- **WHEN** a transfer provider claims an item's URL and its transfer finishes returning a file path
- **THEN** the row shows live progress while running and ends Completed pointing at that file

#### Scenario: Transfer honors pause, resume, and cancel
- **WHEN** the user pauses, resumes, then cancels a transfer-backed item
- **THEN** the transfer's Pause and Resume are invoked and cancel stops the transfer, leaving the row Stopped

#### Scenario: Transfer failure is reported
- **WHEN** a running transfer throws an error
- **THEN** the item is marked Failed and the row shows the error message

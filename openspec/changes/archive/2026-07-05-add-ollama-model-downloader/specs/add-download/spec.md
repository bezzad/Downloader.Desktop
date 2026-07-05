# add-download Specification (delta)

## ADDED Requirements

### Requirement: Non-URL input claimed by a plugin is accepted

When the Add input is not a valid absolute URL, the application SHALL offer the raw text to enabled
plugins' resolvers; if one claims it, the download SHALL proceed through that resolver as if a URL had been
pasted. Non-URL input that no resolver claims SHALL be rejected exactly as today.

#### Scenario: A bare Ollama model name is accepted
- **WHEN** the user enters `gemma3:12b` in the Add box with the Ollama plugin enabled
- **THEN** the input is accepted and resolves through the Ollama plugin to a downloadable file

#### Scenario: Unclaimed non-URL input is still rejected
- **WHEN** the user enters `not a link at all` and no enabled resolver claims it
- **THEN** the input is rejected with today's invalid-link feedback

#### Scenario: Disabled plugin does not accept its names
- **WHEN** the Ollama plugin is disabled and the user enters `gemma3:12b`
- **THEN** the input is rejected as an invalid link

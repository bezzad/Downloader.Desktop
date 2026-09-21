## ADDED Requirements

### Requirement: An add can carry the content type the client already knows
The add endpoint SHALL accept an optional `mime` value naming the content type the client observed,
and SHALL record it on the created download so category detection can use it when the file name
carries no usable extension. An add without the value SHALL behave exactly as before.

#### Scenario: A hand-off supplies the content type
- **WHEN** the browser extension hands off a URL whose file name has no extension, sending
  `mime: "video/mp4"`
- **THEN** the created download records that content type and resolves to the Video category

#### Scenario: The value is ignored when the extension already decides
- **WHEN** an add supplies `mime: "application/octet-stream"` for a file named `song.mp3`
- **THEN** the download resolves to the Audio category, from the file extension

#### Scenario: An add without the value still works
- **WHEN** a client adds a download without sending `mime`
- **THEN** the download is created as before and its category is resolved from its file name

### Requirement: An add can carry a category
The add endpoint SHALL accept an optional category identifier, and SHALL set it on the created
download as the user's explicit choice. An unknown identifier SHALL be ignored rather than
rejecting the add.

#### Scenario: A client picks the category
- **WHEN** a client adds a download naming an existing category
- **THEN** the created download carries that category as an explicit choice

#### Scenario: An unknown category is ignored
- **WHEN** a client adds a download naming a category that does not exist
- **THEN** the download is still created and its category is resolved automatically

#### Scenario: A confirmed add pre-fills the category
- **WHEN** an add in confirm mode names a category
- **THEN** the Add dialog opens with that category pre-selected

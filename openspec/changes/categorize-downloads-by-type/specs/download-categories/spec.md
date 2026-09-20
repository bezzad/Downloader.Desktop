## ADDED Requirements

### Requirement: Every download belongs to exactly one category
Every download in the list SHALL resolve to exactly one category at all times, including while its
file name is still being fetched. A download whose category cannot be determined SHALL resolve to
the "Other" category rather than to no category.

#### Scenario: A named download resolves to a category
- **WHEN** a download's file name is `holiday.mp4` and no category has been set on it by the user
- **THEN** it resolves to the Video category

#### Scenario: A download with no name yet still has a category
- **WHEN** a download has been added but its file name has not been resolved yet
- **THEN** it resolves to the Other category, and re-resolves once the name arrives

#### Scenario: An unrecognized extension falls to Other
- **WHEN** a download's file name is `payload.qqq`, which no category claims
- **THEN** it resolves to the Other category

### Requirement: Category resolution follows a fixed precedence
A download's category SHALL be resolved in this order, stopping at the first source that yields one:
the user's explicit choice on that download, then the file extension, then the `Content-Type`
reported by the server or the browser, then Other.

#### Scenario: The user's choice beats the extension
- **WHEN** a download named `clip.mp4` has been explicitly set to the Image category by the user
- **THEN** it resolves to Image, and the `.mp4` extension is not consulted

#### Scenario: Content type is used when there is no extension
- **WHEN** a download has no file extension and the server reported `Content-Type: audio/mpeg`
- **THEN** it resolves to the Audio category

#### Scenario: The extension beats the content type
- **WHEN** a download named `song.mp3` was served with `Content-Type: application/octet-stream`
- **THEN** it resolves to the Audio category, from the extension

### Requirement: A download's category can be overridden by the user
The user SHALL be able to set any download's category to any existing category, overriding whatever
the app detected, and SHALL be able to clear that choice so detection applies again. The choice
SHALL persist across restarts.

#### Scenario: Override persists across a restart
- **WHEN** the user sets a `.mp4` download to the Image category and restarts the app
- **THEN** that download is still shown in the Image category

#### Scenario: Clearing the override restores detection
- **WHEN** a download has been overridden to Image and the user chooses the automatic option
- **THEN** the download resolves by detection again (Video for a `.mp4`)

#### Scenario: Automatic option names the detected category
- **WHEN** the user opens the category chooser for a `.mp4` download that has no override
- **THEN** the automatic option is shown as selected and identifies the detected category

### Requirement: The category can be chosen from three places
The user SHALL be able to change a download's category from the Add-download dialog before it
starts, from the download's Details window at any time, and from the download row's right-click
menu.

#### Scenario: Chosen in the Add dialog
- **WHEN** the user picks a category in the Add dialog and confirms
- **THEN** the created download carries that category

#### Scenario: Changed from the Details window mid-download
- **WHEN** the user changes the category in the Details window of a running download
- **THEN** the download's category changes immediately and the download is not interrupted

#### Scenario: Changed from the row context menu
- **WHEN** the user right-clicks a download row and picks a category from the category submenu
- **THEN** that download's category changes immediately

### Requirement: Built-in categories exist on first run
On first run, and when an existing configuration is upgraded, the app SHALL create the built-in
categories Video, Audio, Image, Document, Archive, App, Disc and Other, carrying the extension sets
the app recognized before this change. An upgraded configuration SHALL NOT end up with an empty
category list.

#### Scenario: A pre-existing configuration is upgraded
- **WHEN** the app starts with a configuration file written before categories existed
- **THEN** the built-in categories are created and every existing download resolves to one of them

#### Scenario: Built-in extension sets are preserved
- **WHEN** the built-in categories are created
- **THEN** `.mkv` resolves to Video, `.flac` to Audio, `.webp` to Image, `.epub` to Document,
  `.zst` to Archive, `.appimage` to App and `.iso` to Disc, as they did before this change

### Requirement: Category names are stored verbatim and never translated
A category's name SHALL be stored and displayed exactly as entered, in whatever script the user
typed, and SHALL NOT be translated when the app's language changes. Built-in categories SHALL be
named in the app's current language at the moment they are created, and SHALL keep those names
afterwards.

#### Scenario: A user-entered name survives a language change
- **WHEN** the user names a category in Japanese and then switches the app to English
- **THEN** the category is still shown with its Japanese name

#### Scenario: Built-in names do not follow the language
- **WHEN** the app is first run in Persian, creating Persian built-in category names, and the user
  later switches to English
- **THEN** the built-in categories keep their Persian names

### Requirement: The user can create, edit and delete categories
The user SHALL be able to create a category with a name, an icon and a color; edit any category's
name, icon, color and extension list, including the built-in ones; and delete a category they
created. Built-in categories SHALL NOT be deletable.

#### Scenario: A new category claims an extension
- **WHEN** the user creates a category named "Ebooks" claiming `epub`
- **THEN** existing and future `.epub` downloads resolve to Ebooks without the app restarting

#### Scenario: Deleting a category re-resolves its downloads
- **WHEN** the user deletes a category that some downloads resolved to by detection
- **THEN** those downloads re-resolve against the remaining categories

#### Scenario: Deleting a category releases its overrides
- **WHEN** the user deletes a category that some downloads were explicitly overridden to
- **THEN** those downloads lose the override and resolve by detection again

#### Scenario: A built-in category cannot be deleted
- **WHEN** the user opens the editor for a built-in category
- **THEN** no delete action is offered

#### Scenario: A name is required
- **WHEN** the user tries to save a category with an empty name
- **THEN** the save is refused and the reason is shown

#### Scenario: Duplicate names are refused
- **WHEN** the user tries to save a category whose name matches an existing category
- **THEN** the save is refused and the reason is shown

### Requirement: Category order is the single ordering authority
Categories SHALL carry a user-controlled order. That order SHALL determine the order they are
listed in, the order the downloads grid sorts by when sorted on type, and which category wins when
more than one claims the same extension — the earliest in the order wins.

#### Scenario: Reordering changes which category claims a shared extension
- **WHEN** two categories both claim `mp4` and the user moves the second one above the first
- **THEN** `.mp4` downloads with no override resolve to the category now listed first

#### Scenario: Grid sorting follows the user's order, not the alphabet
- **WHEN** the user sorts the downloads grid ascending by type, with categories ordered Video,
  Audio, Image
- **THEN** rows are grouped Video first, then Audio, then Image, regardless of the app's language

#### Scenario: A category can be moved up and down
- **WHEN** the user moves a category up, and then down again
- **THEN** it returns to its original position and the order is persisted

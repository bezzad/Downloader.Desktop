## MODIFIED Requirements

### Requirement: The user controls what is intercepted
Interception SHALL be governed by user-visible settings: an on/off switch, a minimum file size below
which downloads are left to the browser, a list of file types to intercept or ignore, and a list of
sites to exclude. Rules SHALL be evaluated before the browser's download is cancelled.

A download's file type SHALL be determined from what the file actually is, not from whether an
extension happens to appear in the URL path. The type SHALL be resolved from the browser's suggested
filename where the browser provides one, from the filename advertised in the response's
content-disposition metadata (including when that metadata is carried in the URL's query string, as
signed CDN links do), and from the reported MIME type. A download SHALL only be judged as being of
unknown type once none of those sources identify it.

#### Scenario: A small file is left to the browser
- **WHEN** a download's reported size is below the configured minimum
- **THEN** the browser downloads it normally and the app is not involved

#### Scenario: An excluded site is left to the browser
- **WHEN** a download originates from a site on the exclusion list
- **THEN** the browser downloads it normally

#### Scenario: A file type the user does not want intercepted is left alone
- **WHEN** a download's file type is not one the user chose to intercept
- **THEN** the browser downloads it normally

#### Scenario: Unknown size does not block interception
- **WHEN** a download's size is not known at the moment the decision is made
- **THEN** the minimum-size rule does not by itself prevent interception

#### Scenario: A signed link with no extension in its path is still matched by type
- **WHEN** a download's URL path carries no file extension, but the browser's suggested filename or
  the response's content-disposition names a file whose type the user chose to intercept
- **THEN** the download is intercepted, exactly as it would be for a direct link ending in that
  extension

#### Scenario: The MIME type identifies a download nothing else names
- **WHEN** neither the URL path, the browser's suggested filename, nor the content-disposition
  identify the file type, but the reported MIME type corresponds to a type the user chose to
  intercept
- **THEN** the download is intercepted

#### Scenario: A genuinely unidentifiable download is left to the browser
- **WHEN** no source identifies the download's file type and the user's rules list which types to
  intercept
- **THEN** the browser downloads it normally, and the reason recorded for the decision distinguishes
  "type could not be determined" from "type is not one the user wants"

## 1. TitleBar control

- [x] 1.1 Add `CenterTitle` (bool, default false) and `RightContent` (object, default null) styled properties to `Views/TitleBar`
- [x] 1.2 Lay the title centered across the whole bar when `CenterTitle`; render `RightContent` left of the caption buttons
- [x] 1.3 Pure `TitleFits(...)` helper + hide the title when it does not fit
- [x] 1.4 Test: `TitleFits` cases (fits / right side reaches the title / exact edge)
- [x] 1.5 Test: a dialog `TitleBar` (no new properties set) renders exactly as before (title left, no extra content)

## 2. Main window

- [x] 2.1 Move the search `TextBox` and the Update / Donate / About buttons into `TitleBar.RightContent`; set `CenterTitle="True"`
- [x] 2.2 Remove them from the top bar; top bar = sidebar toggle + link box + Add
- [x] 2.3 Size the title-bar buttons to fit the 42 px bar
- [x] 2.4 Test: typing in the title-bar search filters the list (same `SearchText` path)
- [x] 2.5 Test: pressing inside the search box does not call the window drag (handled event), empty bar space still does

## 3. Grow-on-focus

- [x] 3.1 Width transition + focused/has-text style on the search box (200 → 280)
- [x] 3.2 Test (headless): width is 200 unfocused+empty, 280 focused, stays 280 unfocused with text, back to 200 when cleared and unfocused
- [x] 3.3 Test: at `MinWidth` with the search focused, the title is hidden or does not overlap

## 4. Finish

- [x] 4.1 Full solution `-t:Rebuild` → 0 warnings; `dotnet test` green
- [x] 4.2 Regenerate `docs/screenshots/` (main window light + dark) and add a new capture with the search box focused; view every changed PNG before committing
- [x] 4.3 Update CLAUDE.md / skill notes if a non-obvious gotcha came up; commit + push to `develop`

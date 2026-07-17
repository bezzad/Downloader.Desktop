# Design — pages-and-dialogs-ux

## Context
Management pages (Settings/Queues/Scheduler) render inside `MainWindow`'s `ContentControl` and scroll their whole content, title included. Modals (`AddDownloadItemView`, `DownloadDetailsView`, `AboutView`, `UpdatePromptView`, `ShutdownView`) use transparent rounded chrome with `SystemRegionColor` — the same background family as the main window, so an open modal looks like part of the window. Settings sections are a mix of `Border.card` and `Expander`; the Plugins section is an `Expander` (default collapsed). Scheduler's new-item default name mirrors the create button label.

## Goals / Non-Goals
**Goals:** persistent page context (sticky title); findable settings; sensible section defaults; unmistakably-distinct modals; unambiguous schedule names.
**Non-Goals:** redesigning page layouts; a global command palette; per-setting deep-linking.

## Decisions
1. **Sticky title = header outside the ScrollViewer.** Restructure each page as `DockPanel`/`Grid` with the title row `DockPanel.Dock=Top` (or Grid row 0) and the `ScrollViewer` filling the rest — the title simply isn't inside the scrolled content. No sticky-on-scroll hack needed.
2. **Settings search = filter + highlight (author-recommended default, since the question was declined).** A `SearchText` on `SettingViewModel` drives visibility: options whose label/keywords contain the term stay visible (others collapse), the containing section auto-expands, and the matched substring is highlighted. Empty term restores all + prior expansion. Rationale: with a long options list, filtering to matches is far faster to scan than highlight-only (which still leaves the user scrolling). Implementation: give each setting row a searchable key/label; a converter or per-row `IsMatch`/`Highlight` bound to `SearchText`. Rejected alternative: highlight+scroll only (keeps context but slow with many matches).
3. **Section defaults.** Convert each Settings section to an `Expander` with `IsExpanded=true`; Plugins `Expander` flips to `IsExpanded=true`. All remain user-collapsible.
4. **Distinct modal chrome.** Add a shared modal look: an accent-colored border (e.g. `SystemAccentColor` at ~1.5px) plus a slightly elevated background/soft shadow distinct from the main window, applied to all modal windows. Optionally a faint scrim isn't possible on a separate top-level window, so the border+elevation is the signal. Verified with an About-open screenshot (task's own suggestion).
5. **Numbered schedules.** New schedule name = `Schedule {n}` where `n` = smallest positive int not already used by an existing `Schedule {k}` name (or count+1). Distinct from the "New schedule" button label. i18n `Sched_DefaultName` = "Schedule {0}".

## Risks / Trade-offs
- [Filtering settings by hiding rows could hide a section entirely] → then the section header can hide too (or show "no matches"); auto-expand only sections with a match.
- [Accent border on every modal may clash with a chosen accent] → it uses the live accent so it stays coherent; elevation/shadow gives a fallback signal in high-contrast.
- [Sticky header restructure touches several axaml files] → mechanical; each page verified by screenshot.

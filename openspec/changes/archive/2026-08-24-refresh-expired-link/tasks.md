# Tasks — refresh-expired-link

Keep build + full `dotnet test` green; commit to `develop` per logical step.

## 1. Classify an expired-link failure

- [x] 1.1 Tests: `LooksLikeExpiredLinkError` matches 401/403/404/410 (incl. wrapped in `AggregateException`
      / inner exceptions) and rejects timeouts, socket errors, 500s and null.
- [x] 1.2 `DownloadManager.LooksLikeExpiredLinkError(Exception)` — pure static helper beside
      `LooksExpiredOrInvalid`.

## 2. Automatic refresh

- [x] 2.1 Tests: an item with bytes fails with 403 → re-queued (not Failed) and the attempt counter rises;
      exhausted attempts → Failed with the expired-link message; an item with **no** bytes fails immediately;
      a non-expired error is untouched.
- [x] 2.2 `DownloadItemViewModel.LinkRefreshAttempts` (session-only) + reset on completion and on a
      user-initiated `Retry`/`Resume`.
- [x] 2.3 `DownloadManager`: in the completion handler's failure branches, route an expired-link failure
      through `TryAutoRefreshLink(vm, error)` — re-queue via the pump instead of marking Failed, no failure
      notification, status message says the link is being refreshed.

## 3. Manual "Refresh link" in the Details window

- [x] 3.1 Tests: `LinkRefreshCheck.Evaluate(knownSize, newSize)` → Match / Unknown / Mismatch.
- [x] 3.2 `DownloadDetailsViewModel`: `RefreshLinkCommand` + busy/error state; probe via
      `UrlResolver.ResolveFileInfoAsync` with the item's configuration; Match/Unknown → swap + resume;
      Mismatch → `DialogHelper.Confirm` then swap + resume or abort; unreachable → show the reason.
- [x] 3.3 `DownloadDetailsView.axaml`: label the URL box as the place to paste a fresh link, add the
      Refresh button + busy/error line (mirrors the mirror-editor styling).
- [x] 3.4 Headless test for the VM path (Match resumes; Mismatch without confirmation changes nothing).

## 4. Wording + wrap-up

- [x] 4.1 New i18n keys (`Det_RefreshLink`, `Det_RefreshHint`, `Error_LinkExpiredRefresh`,
      `State_RefreshingLink`, mismatch confirmation) in **all 16** language packs.
- [x] 4.2 `dotnet build` clean; full `dotnet test` green; screenshots refreshed if the Details UI changed.
- [x] 4.3 Commit + push on `develop` (`1513abd`).
- [ ] 4.4 Author manual check (cannot be automated here): let a real signed link expire mid-download (or
      stop one and wait), confirm the row refreshes itself and continues, and try the Details "Refresh link"
      button with a fresh link for the same file.

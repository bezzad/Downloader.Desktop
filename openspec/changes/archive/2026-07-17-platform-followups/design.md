# Design — platform-followups
1. **Variant leak**: FallbackResolverTests already pin "only the detected resolver's variants are shown" — but the Website plugin CLAIMS text/html pages (offers websitezip variant) while HLS claims x.com links; when both claim, the variant AGGREGATION apparently unions. Locate GetVariantsAsync in the plugin host: it must pick the single winning resolver (specific > fallback; first-claim order otherwise) and return only its variants. Repro with an x.com URL fake in tests.
2. **X.com extraction**: reproduce via SiteExtractor with an x.com URL — likely yt-dlp needs cookies (x.com now requires auth for most media) or different args; if login-gated is the cause, surface the existing cookies-from-browser retry (like YouTube) and verify the plugin retries with browser cookies; bump plugin version.
3. **Start-menu shortcut (Windows)**: on first run (Windows only), create %APPDATA%\Microsoft\Windows\Start Menu\Programs\Downloader.lnk pointing at the running exe (PowerShell WScript.Shell one-liner, best-effort, skip if exists). Toggle-free; uninstall note documented. Testable: the .lnk path/command construction is pure.
4. **MS Store**: author-gated note only (Partner Center individual account ~$19 → MSIX packaging → submission); prepare when the author has the account.

## Findings (recorded 2026-07-17)
- **x.com root cause (verified live)**: anonymous guest-token GraphQL no longer returns tweet media —
  yt-dlp 2026.07.04 reports "No video could be found in this tweet" for tweets that extract PERFECTLY
  with `--cookies-from-browser chrome` (same tweet verified both ways on this machine). That error text
  wasn't in the plugin's NeedsCookies patterns, so the cookie retry never fired; added (HLS plugin
  v1.3.1). The variant leak was the same failure: the HLS lookup threw → the aggregation fell through
  to the Website fallback's "Offline copy (.zip)". Failure now yields NO variants (empty still falls
  through, so the GitHub-releases/Website pairing keeps working).
- **MS Store (author-gated)**: needs the author's Microsoft Partner Center individual account
  (~$19 one-time). Then: MSIX packaging (can be added to release CI) → submission. Nothing submitted;
  revisit when the account exists.

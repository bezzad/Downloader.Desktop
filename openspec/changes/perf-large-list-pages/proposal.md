# Smooth pages with 2k+ downloads: queues virtualization, page reuse, bulk add

## Why
With 2k+ items: (a) opening the Queues page hangs ~20 s (every queue card materializes ALL item rows in a non-virtualized ItemsControl; expanding a large queue hangs again; leaving the page back to Downloads is slow too); (b) navigating pages recreates each page view/VM every time (repeat cost + churn); (c) clicking Download on a 2k-link bulk add freezes the UI ~2 min (2000 × Add, each notifying/refreshing) and can crash.

## What Changes
- Queues page virtualizes item rows (ListBox/virtualizing panel) and builds row wrappers lazily; rebuilds are batched.
- Page views are created once and reused on navigation (state — scroll, expanders — survives; no re-create cost).
- Bulk add closes the modal immediately and adds items in the background in batches that yield to the UI thread; the list stays responsive while rows stream in.

## Capabilities
### Modified
- `queues`: virtualized, lazily-built queue item lists.
- `add-download`: bulk add is non-blocking (modal closes, items stream in).
- `ui-navigation`: page instances are reused across navigation, preserving their state.

## Impact
Views/QueuesView.axaml(+cs), ViewModels/QueuesViewModel.cs, MainViewModel (page cache + bulk add path), DownloadManager (batched add), tests.

---
name: ngb-reporting
description: Add or extend NGB canonical/composable reports, datasets, provider-side paging, totals, downloads and report UI.
---

Use for reporting contracts and execution. A request to display existing data does
not authorize changing the underlying business calculation.

1. Read [reporting contracts](../../references/reporting.md). Select the supported
   definition/dataset/executor role and inspect its exact installed signature.
2. Freeze expected rows, totals, grouping, filters and drilldown targets. Declare
   permission and feature prerequisites independently of presentation.
3. Implement bounded provider queries, stable sorting/cursors and cancellation.
   Apply row selection as a set; keep totals and visible rows under identical scopes.
4. Use the public shared UI exports. Test multiple pages, tied keys, stale cursors,
   empty results, export cancellation and unauthorized dimensions.
5. Run `node infrastructure/ngb.mjs validate --profile build` and
   `node infrastructure/ngb.mjs test --profile all`; measure query count and
   representative volume separately.

Done: known rows/totals reconcile across UI, pages and exports, and the selected
execution path remains authorized and bounded.

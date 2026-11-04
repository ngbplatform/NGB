---
name: ngb-accounting
description: Implement NGB accounting posting strategies, chart-of-accounts integration, General Journal Entry and financial-period workflows.
---

Use when the feature has accounting meaning. Do not require accounting for a
document that only needs another supported effect engine.

1. Read [accounting contracts]({{references}}/accounting.md). Confirm account,
   dimension, currency, period and reversal semantics in the resolved SDK.
2. Define expected debit/credit entries and report totals before implementation.
   Implement the public per-document strategy in the lifecycle transaction.
3. Compose the supported authorized GJE/period services for interactive workflows;
   keep trusted system operations out of ordinary client endpoints.
4. Test balanced results, closed-period refusal, idempotency, concurrent versions,
   rollback and reversal with real storage and independent expected amounts.
5. Run `node infrastructure/ngb.mjs validate --profile build` and
   `node infrastructure/ngb.mjs test --profile all`.

Done: ledger/report results reconcile, history survives correction and reversal,
and permissions and period rules remain enforced.

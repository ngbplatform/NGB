---
name: ngb-registers
description: Implement NGB operational-register movements, reference-register records, projections and their document posting or reversal behavior.
---

Use for register semantics and effects. Use accounting guidance for ledger entries.
A registered posting handler may intentionally emit no records for a document
whose lifecycle has no register effects. That does not require creating a register
or inventing movements; distinguish it from a missing handler configuration.

1. Read [register contracts](../../../ai/vendor/references/registers.md) and the installed
   definition/runtime signatures. Choose the engine and define dimensions,
   resources, periods and expected state independently of the handler.
2. Implement the matching public posting handler or supported independent write
   path. Keep provider storage out of Runtime and share the lifecycle transaction.
3. Implement bounded read/projection paths only through supported contracts.
   Preserve append-only history and the engine's schema evolution checks.
4. Verify successful write, retry, stale version, competing operation, rollback and
   reversal against actual PostgreSQL. Compare expected state and original history.
   Include changed amounts and earlier/later effective dates across months,
   finalization after several changes, and recorded-time historical RR reads.
5. Run `node infrastructure/ngb.mjs validate --profile build` and
   `node infrastructure/ngb.mjs test --profile all`; record actual volume/query
   measurements separately from unit-test coverage.

Done: independently expected balances/state and reversal history agree, repeated
operations do not duplicate effects, and batch reads remain bounded.

---
name: ngb-frontend
description: Build NGB Vue application pages, navigation, editors, report UI, themes and accessible interactions using public Shared UI exports.
---

Use for application UI composition. Do not copy or import monorepo-private framework
components to bypass a missing public extension.

Use public `NgbDocumentLines` for document parts and keep business behavior in
the application. Apply the [navigation and UI conventions](../../references/application-generation.md#navigation-and-standard-surfaces),
including uniform document/report icons and explicit favicon linkage.
Generate [interactive field dependencies](../../references/interactive-workflows.md),
including defaults, source snapshots, filtered lookups and recalculation. Readonly
derived values must actually populate. Use the shell's `navigate` event once;
`select` is not a second navigation command. Preserve the full product name and
place Dashboard first as a standalone root link.
Use the runnable examples in that reference for whole-chain invalidation, readonly
source navigation and saved-row reference hydration. Preserve primary Name displays;
put disambiguating codes in secondary metadata. Explain conditional editability and
surface actionable [business errors](../../references/errors.md), not context-only reasons.

1. Read [frontend contracts](../../references/frontend.md). Inspect the installed
   export, component props/emits/slots and corresponding backend metadata.
2. Compose the page using supported entry points and theme tokens. Preserve routes,
   expected versions, permission-aware actions and independent feature flags.
3. Cover loading, empty, error, validation and cancellation states. Verify keyboard
   focus, accessible names and responsive behavior.
4. Run `node infrastructure/ngb.mjs validate --profile build`,
   `node infrastructure/ngb.mjs test` and
   `node infrastructure/ngb.mjs test --profile e2e`.

Done: independent typecheck/build passes, public import boundaries hold, component
states are tested, and real browser/API authorization agrees.

Apply the [business outcome checks](../../references/business-outcome-checks.md)
and the named-outcome browser gate before claiming the generated workflow complete.

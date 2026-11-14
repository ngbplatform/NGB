---
name: ngb-frontend
description: Build NGB Vue application pages, navigation, editors, report UI, themes and accessible interactions using public Shared UI exports.
---

Use for application UI composition. Do not copy or import monorepo-private framework
components to bypass a missing public extension.

Use public `NgbDocumentLines` for document parts and keep business behavior in
the application. Apply the [navigation and UI conventions](../../references/application-generation.md#navigation-and-standard-surfaces),
including uniform document/report icons and explicit favicon linkage.

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

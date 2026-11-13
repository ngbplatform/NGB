# Public Vue UI composition

Target: `@ngbplatform/ui` 3.4.0. The exact package entry points and their artifact
hashes are in [the UI contract snapshot](contracts/ui.json).

Use the main entry or the declared `contracts`, `editor`, `layout`, `lazy`,
`navigation`, `work-center`, `styles`, `vite-public-assets` and `tailwind-preset`
subpaths. Inspect the installed TypeScript/Vue contracts for named exports,
props, emits and slots. An exported subpath does not imply every private component
under that directory is public. Never import monorepo-relative framework sources.

Keep metadata/list/editor contracts aligned with the backend definition. Preserve
route identity, permission-aware navigation, error/loading/empty states and
expected document versions. Interactive actions use the authorized API rather
than writing statuses in the client. Feature flags and access checks control
different concerns; the backend must reject unauthorized operations independently.

Use the published preset, styles and existing theme tokens. Extend components
through supported props/slots and application-owned composition. Avoid copying a
private component merely to change a visual detail. If the public component lacks
the requested extension, report the precise missing contract.

Run typecheck/build from the independent application and test public imports.
Exercise keyboard focus, accessible names, validation errors, mobile layout,
loading/cancellation and rejected access. Browser tests must use the real backend
and disposable Keycloak; component mocks alone cannot prove authorization.

The starter's `web/src/` and test configuration are a small working example.
Its eight frontend tests and two browser scenarios establish starter behavior,
not arbitrary future business forms or all framework component combinations.

The equipment example also maps the public Work Center and notification-preference
pages to their default navigation paths. Its two-tab browser scenario observes
actual SignalR delivery, then exercises dismiss, snooze and saved preferences
against the real API. See [Work Center composition](work-center.md) for ownership,
linked identities and the distinction between committed projection and live refresh.

The same example uses `@ngbplatform/ui/lazy` loaders directly as Vue Router
components for shared editor, effects, reporting, accounting and Work Center
pages. Importing a loader does not mean invoking it eagerly during bootstrap.
Its ordinary application pages remain application-owned. The catch-all route
uses `NgbPageHeader` from `@ngbplatform/ui/layout` to display a useful unknown-page
message and an explicit return action.

Navigation configuration comes from `@ngbplatform/ui/navigation`; the site's
Work Center button opens its drawer and **View all** follows the configured
full-page route. The production browser test exercises that path, theme switching,
mobile sidebar visibility, horizontal bounds and the unknown-page return action.
The packaged styles and preset supply real computed colors and layout; the
`vite-public-assets` plugin supplies `favicon.svg` and `silent-check-sso.html` in
the built output. Test those served assets, not just a successful plugin import.

Node-side browser tests use type-only imports from `@ngbplatform/ui/contracts`.
The published UI runtime is TypeScript for the application's Vite build; Node 24
does not strip TypeScript inside `node_modules`. Do not import its runtime enums
directly into Playwright's Node process. Raw HTTP responses encode enum names;
the UI client normalizes them to the numeric DTO representation. Report assertions use an
explicit UTC date range and the created equipment ID: a local-day default can
exclude a document created after UTC midnight. Do not change report semantics
to repair a test's implicit time assumption. Numeric controls are filled using
keyboard events and their visible value is checked before Save.

When browser-mode Vitest tests import additional package subpaths, predeclare
their dependency optimization entries. Otherwise Vite may reload a test while
discovering the new dependency graph. Keep one Vue runtime, retain the per-file
coverage thresholds and verify a fresh package-only build as well as component
tests. The example's current configuration covers its ten source files; it does
not claim that every component exported by the framework has been exercised.

Application startup is an explicit `startApplication` function in `src/bootstrap.ts`.
The browser entry `src/main.ts` calls it once. Tests invoke the function directly
for each authentication, access and failure case, so they do not depend on
re-evaluating a cached browser module. Keep metadata capability assertions on
this actual composition path; no frontend override may grant server-denied rights.

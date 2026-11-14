# From business requirements to a complete application

The user supplies business processes, rules and outcomes, without knowledge of
NGB entities or engines. Make implementation choices autonomously using the
installed public contracts. Keep the business specification in business terms;
record technical decisions in the application architecture and acceptance notes.
Ask about a missing business rule only when it materially changes the outcome,
not whether to use an NGB engine, component or metadata convention.

## Choose effects before implementation

For every business transaction, record its document/workflow, authoritative state,
Accounting entries, Operational Register movements, Reference Register records,
effective date, and Post/Unpost/Repost behavior. Explain an absent effect by its
business meaning. These are decisions for the generator, not questions for the user.

- Use Accounting for monetary recognition, settlement and financial statements.
- Use Operational Registers for quantities, custody, open items, allocations and
  other operational balances needed by business decisions. When the application
  needs invoice-level settlement or held-fund balances, model that operational
  state explicitly and reconcile it with Accounting. Ledger entries alone do not
  establish the required operational analytics.
- Use periodic Reference Registers for effective-dated prices, rates and other
  normative values. Choose independent versus recorder-subordinate semantics
  from the required current/historical read behavior. Snapshot applied values in
  transactions so later rate changes cannot rewrite existing results.
- Pure workflow approval may have no movements. Do not add fictitious entries or
  registers just to populate Effects. Do not replace an appropriate platform
  register with ad hoc aggregation of Posted documents without a demonstrated
  contract limitation and an explicit, tested alternative architecture.

Use one lifecycle transaction for related effects and shared locking for competing
business operations. Distinguish business/effective time from recording time.
Use the same business-day boundary in dashboard, reports and reconciliation;
convert it explicitly at the supported report boundary. Test backdating, future
values, same-date conflicts, retries and corrections. See [registers](registers.md),
[accounting](accounting.md) and [documents](documents.md#withdrawal-and-dependent-documents).

## Navigation and standard surfaces

Generate business-area navigation. Put each operational report beside its related
work; do not create standalone `Reports` or `Administration` groups. Use `Accounting`
for accounting documents/reports and `Setup & Controls` for settings and controls.
These are application-generation conventions, not vocabulary required in the
user's business specification.

Use `file-text` for every document menu item and `bar-chart` for every report menu
item, including reports registered as pages. Validate names against the installed
`NGB_ICON_NAMES`; a renderer fallback is a generation error, not a valid icon.

When Accounting is used, compose its existing document, report and admin menu
contributors together with the corresponding API controllers, public UI routes,
registered permissions and any feature flags. Include Journal Entries, Trial
Balance, Balance Sheet, Income Statement, Cash Flow Statement, Statement of
Changes in Equity, General Journal, Account Card, General Ledger and Ledger
Analysis. Put Chart of Accounts, Period Close, Posting Log and Integrity Checks
in Setup & Controls. Preserve canonical route/resource identities. Normalize
document/report icons when composing these items; do not invent a partial list
of financial reports as a replacement for the enabled module.

Do not blanket-remove `IMainMenuContributor` registrations. A minimal starter
without Accounting intentionally omits its three menu contributors; remove that
starter exclusion when composing Accounting and wire the destinations together.
Inspect the installed contributor and lazy-loader contracts instead of guessing
routes or adding links whose controllers/pages do not exist.

Include Users, Roles & Permissions, Background Jobs and Health in Setup & Controls
for a complete generated application. Compose the worker dashboard and Watchdog
host, deployment wiring and registered external-link capabilities, not just menu
labels. Use configured URLs and the existing administrative-host authorization;
API `/health` is not the Health dashboard. Respect explicitly disabled services
and feature flags and describe that intentional omission. See
[application composition](application.md#watchdog-host) and the background-jobs skill.

## UI and traceability

Use the public `NgbDocumentLines` component for tabular document parts. Keep
calculations, defaults, lookup filters and validation in application-owned code;
share rendering, paging and row interactions. Follow [the Lines contract](frontend.md#document-lines).

All reports need meaningful drill-down: grouped figures lead to filtered details,
and detail rows lead to their source documents or records. Preserve the selected
period, filters and permissions. Follow [report actions](reporting.md#time-fields-and-drill-down).
Use standard document relationships for Document Flow; domain relationships may
supplement them. A reference field alone does not create a visible flow edge.
See [flow composition](documents.md#visible-document-flow).

Connect the favicon explicitly in HTML. Test the built and served asset with the
application base path; the Vite public-assets plugin does not add an HTML link.
Use the shared branding assets unless the user requests different branding.

## Demo identities

An opt-in demo seed creates an active human demo user for every employee role,
including Administrator. Invent readable names (for example Alex Morgan or
Jordan Lee); show their roles separately. Do not require a list of employee names
in the business requirements. A bootstrap administrator or service account does
not substitute for the other roles.

Provision each identity, linked application user and role assignment through the
supported identity/security services. Use deterministic demo keys so rerunning
seed does not duplicate users or grants. Preserve existing non-demo users and
keep demo credentials confined to the explicit demo/test setup. The normal
production startup must not provision demonstration accounts.

Test an actual sign-in and a permitted workflow for each role, plus a denied
operation. Verify active registered administrators without manual capability
grants, inactive users and disabled features. See [security](security.md).

## Acceptance evidence

Build an inventory from the registered documents, reports, menu entries and roles;
do not test only a handpicked subset. For each item record its expected business
outcome and the actual test result:

| Surface | Required evidence |
| --- | --- |
| Reports | Every registered report executes through its authorized endpoint with valid parameters; known figures reconcile; grouped/detail drill-down and source navigation work in the browser |
| Navigation | Document/report icons exist and follow the convention; menu destinations resolve; Accounting/Setup & Controls are complete for enabled services; forbidden items and direct requests are denied |
| Documents | Head and Lines survive save/reload; incomplete Draft and invalid Post differ; Post/Unpost/Repost, retry, competing operations and dependency rejection preserve all effects |
| Document Flow | A related document chain is visible in the actual shared UI, with working links and inaccessible nodes excluded |
| Demo | Every employee role has a usable active identity and assignment; seed is repeatable; each role completes its permitted scenario |
| Operations/branding | Real worker and Health dashboards are reachable by authorized staff; favicon is linked, served with the correct media type and loaded by the browser |

A SQL sum or dashboard assertion is not execution of a report endpoint. A graph
API response is not proof of UI rendering. Component mocks do not prove role
authorization. Unsupported public contracts, unexecuted checks and missing
prerequisites remain explicit gaps; do not claim completion from coverage alone.

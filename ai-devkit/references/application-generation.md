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
Preserve the [complete default local deployment](application.md#default-local-deployment),
including Watchdog, BackgroundJobs, Seq, PostgreSQL, pgAdmin, Keycloak and MinIO
alongside the API and web application. Attachments and Notes are enabled by default;
absence from the business prompt is not a request to omit them or their infrastructure.

Make Dashboard the first standalone root link, before the business-area groups.
Register it as a singleton group whose label matches its item, with an earlier
group ordinal; preserve the starter shell's singleton-to-root mapping. Do not put
Dashboard inside the first business group. A partial capability example without a
dashboard is not a complete application's navigation template.

Preserve the user's complete product name in the shell, browser title and login
branding. Keep the product display name separate from code namespaces and route
identifiers. Do not shorten it to fit the sidebar; verify the actual rendered name.
Use only the shell's `navigate` event to perform navigation. `select` also fires for
the same click and must not perform another router push or `window.open`.

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
The default Watchdog UI path is `/health-ui`; its root is not an implicit dashboard.
Check the complete external URL and an actual OIDC sign-in, including the callback
scheme, hostname, port and path. A running worker or healthy probe does not verify
its interactive dashboard. Never disable authorization to make the link work.

## UI and traceability

Use the public `NgbDocumentLines` component for tabular document parts. Keep
calculations, defaults, lookup filters and validation in application-owned code;
share rendering, paging and row interactions. Follow [the Lines contract](frontend.md#document-lines).
Generate the field dependency behavior as well as the table: see
[interactive workflows](interactive-workflows.md). A saved-document calculation
command does not satisfy a requirement to populate a value when a row is added.

Use the standard numbering policy for demo and user-created documents alike.
Give seeded records separate stable identities; do not replace business numbers
with fixture keys. Generate a server-owned document display from singular type
name, assigned number and business date, and hide that technical display field
from editing. Business notes remain separate. See
[document identity and dates](documents.md#document-identity-and-dates).

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

Follow [business outcome checks](business-outcome-checks.md) and bind each required
business rule to named, executed outcomes in acceptance schema 2. An expected
refusal does not prove an allowed correction; a populated seed does not prove
interactive dependencies or complete historical figures.

Build an inventory from the registered documents, reports, menu entries and roles;
do not test only a handpicked subset. For each item record its expected business
outcome and the actual test result:

| Surface | Required evidence |
| --- | --- |
| Reports | Every registered report executes through its authorized endpoint with valid parameters; known figures reconcile; grouped/detail drill-down and source navigation work in the browser |
| Navigation | Dashboard is the first root link; the full product name is visible; document/report icons follow the convention; each click navigates once; all destinations resolve; Accounting/Setup & Controls are complete; forbidden items and direct requests are denied |
| Documents | Head and Lines are entered through the browser, dependent values populate/recalculate, lookup choices respect the parent, and values survive save/reload; incomplete Draft and invalid Post differ; Post/Unpost/Repost, retry, competing operations and dependency rejection preserve all effects |
| Document Flow | A related document chain is visible in the actual shared UI, with working links and inaccessible nodes excluded |
| Demo | Every employee role has a usable active identity and assignment; seed is repeatable; each role completes its permitted scenario |
| Operations/branding | The complete Compose stack starts; real worker and Health dashboards accept authorized sign-in; Watchdog collects API/worker/web results; Seq receives logs; private attachments and notes work with their default flags; favicon loads correctly |

A SQL sum or dashboard assertion is not execution of a report endpoint. A graph
API response is not proof of UI rendering. Component mocks do not prove role
authorization. Unsupported public contracts, unexecuted checks and missing
prerequisites remain explicit gaps; do not claim completion from coverage alone.

Use API calls to prepare unrelated prerequisites in browser tests, not to perform
the workflow named by the test. A create/edit/price/post scenario must use the
actual fields and action controls. Opening the shell, reloading API-prepared data,
checking a route or taking a screenshot does not establish the business outcome.
Verify the report's rows/figures and drill-down result, not just its surrounding
layout. For external links, assert exactly one new page and successful authorized
content. Test a real Sidebar; a mock that emits only one of its two events hides
duplicate navigation. Match test names and acceptance claims to the actions and
assertions that actually executed.

Maintain one acceptance row per registered document and role-sensitive workflow,
including input dependencies and reference columns. Record: registration code,
role, initial state, UI actions, expected values/target identities, executable test
name, run artifact and outcome (`passed`, `failed`, `not-run` or an explicitly
justified `not-applicable`). Compare that inventory with registrations after every
generation change. Unmatched registrations or required `failed`/`not-run` rows are
unfinished work, even when a subset of tests has 100% code coverage. Include the
[reopen, source replacement and browser interaction cases](interactive-workflows.md#browser-acceptance)
and [visible business refusals](errors.md). Do not report generated tests as passing
until their assertions have actually run against the current implementation.

An unsupported required workflow is an unfinished item, even when documented.
Do not silently accept it as complete or substitute a manual procedure for an
enforced business rule. Keep existing SDK limits explicit and separate from
generation omissions. Exercise each conflicting operation pair, including
effective-dated value changes, rather than extrapolating from a single race test.

Complete the [executed browser acceptance matrix](browser-acceptance.md) before declaring generation complete.
The E2E CLI gate must reconcile runtime documents, catalogs, reports, menu destinations and roles with current-run Playwright
UI evidence. Passing test counts or a prose checklist alone are insufficient.

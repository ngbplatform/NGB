# AI Development Kit implementation evidence

Target: 3.4.0. Baseline: `be691b30a86ae6f636134d5ff15f75a678939e28`.
Implementation is in progress; candidate certification has not passed.

The three subsequently authorized SDK read-path corrections are complete and
verified by targeted tests as of 2026-11-04. See
[platform read regressions](findings/platform-read-regressions.md) for fixes,
coverage, compatibility and rebuilt package evidence. Earlier limitations and
candidate results below are historical. The full Backend/Frontend run remains
with the user and is not claimed by these targeted results.

The 2026-11-05 dependency refresh also synchronizes the current Kit example,
template and acceptance projects with the user's updated platform packages.
[Dependency refresh evidence](findings/dependency-refresh-2026-11-05.md) records
the removal of the stale-package MCP NU1608 warning, rebuilt artifacts and
targeted consumer verification.

## Verified architecture

- The repository and application entry points share
  `packaging/templates/content/infrastructure/ngb/cli.mjs`.
- Upgrades stage dependency changes, build independently and commit dependency,
  CLI/helper and Kit files through one recoverable transaction. Three-way conflict
  decisions preserve custom content; installing a template alone does not update an app.
- The v3.3.0 template supplies API, migrator, worker and Vue projects. It has
  no independent test foundation or application tooling configuration.
- Public capabilities come from SDK packages and public UI exports. Existing
  applications are not sources for Kit instructions or reference implementations.
- Release regression ownership stays in `quality/external-consumer/`.

## Implementation order

1. Verify an independently authored equipment/inspection contract example against
   package references before broad reference authoring.
2. Implement bounded data handling, ownership, three-way plans, conflict decisions
   and recoverable transactions with adversarial tests.
3. Integrate shared CLI diagnostics, validation and execution profiles, then
   deterministic Kit/template/npm packaging and coordinated upgrades.
4. Complete starter tests, capability evidence, references, agent adapters,
   independent consumer scenarios and release gates.

## Agent discovery sources

Checked 2026-10-25. Documentation verification is not an agent evaluation.

- [Codex skills](https://learn.chatgpt.com/docs/build-skills): repository discovery
  uses `.agents/skills` from the working directory to repository root.
- [Codex instructions](https://learn.chatgpt.com/docs/agent-configuration/agents-md):
  root-to-working-directory instruction discovery, with override precedence.
- [Claude skills](https://code.claude.com/docs/en/skills): `.claude/skills` discovery.
- [Claude memory](https://code.claude.com/docs/en/memory): `@AGENTS.md` remains
  supported, including clients/settings without native AGENTS discovery.

## Environment observations

Initial host: Node 25.5.0, .NET SDK 10.0.101; Codex executable present, Claude Code
not on PATH. The current release toolchain pins Node 24.19.0/.NET 10.0.401.
Host smoke tests therefore do not establish pinned-toolchain or Claude support.

The existing managed Node 24.19.0 and .NET 10.0.401 installations were subsequently
located and their installation receipts verified. Tooling and the later starter
probes use those exact versions. Claude Code 2.1.308 is available through the
installed desktop application's bundled client. Its own subscription login was
verified after the user completed terminal authentication.

## Executed development probes

Early probes used existing 3.3.0 packages. Later probes described below use sealed
3.4.0 development packages. None establishes complete platform release certification.
Later source/payload changes invalidate the corresponding earlier candidate evidence.

- Independent equipment/inspection: seven unit tests and one real HTTP test with
  disposable PostgreSQL and Keycloak, including the accepted unsupported Post.
- Starter: thirteen backend unit tests; complete per-file line, branch and method
  coverage for `AdministratorBootstrap`, `ApplicationMenu`, `AssemblyInspector`
  and its CLI entry point. Managed metadata is read without loading application
  assemblies; malformed/native and excessive metadata fixtures are exercised.
- Actual starter API: authenticated Administrator, unauthorized reader and
  anonymous access tested with disposable PostgreSQL/Keycloak.
- Starter frontend: seven tests; all three source files have 100% lines,
  branches, functions and statements in the Vitest JSON report.
- Browser starter: two successful Playwright scenarios against a fresh Docker
  deployment, using loopback ports and credentials created for that run. The
  Administrator opens the role editor without a platform user record; the
  reader receives a backend 403. Owned containers and volumes are removed.
- `test-tooling.mjs`: 115 Node tests, sixteen measured modules at 100% per-file
  lines, branches and functions. The runner requires each declared source and
  test file to appear in the report; an empty coverage report cannot pass.
- A development `@ngbplatform/cli@3.4.0` archive is produced from the shared
  template implementation. Archive integrity, safe entries, package/implementation/
  Kit versions and byte equality for its shared payload are verified. It is not
  published and is not yet part of a fully certified release candidate.

### Latest package-only starter probe

`candidate-hosts-3.4.0` generated `/private/tmp/ngb-kit-hosts-3.4.0` through the
actual template installation. Its `test --profile all` passed with 13 backend unit,
6 integration, 7 frontend and 2 browser tests; no skipped tests. The run is
`a6692de9-d6b3-4528-96d5-df32bac3b403`. Its compiled source inventory contains 13 C#
files: 12 executable files and one audited constant declaration. All executable
files are present with 161/161 lines, 54/54 branches and 19/19 methods. All three
frontend source files have 100% lines/branches/functions/statements. API, worker
and migrator startup paths are exercised against real disposable services.

The parent CLI owns the browser environment, validates the handoff endpoint against
the owned web container and removes owned containers, volumes and networks in its
cleanup path. `verify-cancellation.mjs` interrupted the actual CLI after resource
creation: the profile failed, required browser execution remained not-run, and
all resources bearing that run ID were gone. Evidence is in
`artifacts/ai-devkit/cancellation-hosts-3.4.0.json`.

The later ownership implementation persists a private receipt before provisioning
and labels resources with both application and run identities. An actual SIGKILL
probe passed active-owner refusal, preservation of a volume with a different
application label, nonmutating preview, cleanup of all owned resource kinds and
idempotent retry. Evidence: `artifacts/ai-devkit/resource-recovery-3.4.0.json`.
Recovery conservatively refuses an unrecorded launch window or a live/reused PID;
it never assumes that an unverified provisioner has stopped.

The `starter-http-v1` k6 development probe passed three repetitions, 1200 measured
requests and zero failures. Route p95 values were 1.23–1.90 ms on the recorded local
runner. The frozen profile and machine details are in
`artifacts/ai-devkit/load-3.4.0-probe-2/`. This measures seeded starter role/menu reads,
not application business workloads or historical performance equivalence.

A real frozen 3.3.0 application was upgraded through the separately acquired exact
3.4.0 npm CLI using `candidate-locks-3.4.0`. Its isolated backend/frontend builds
passed and 45 unchanged user files plus root instruction text were independently
checked. This development probe does not replace persisted-data upgrade acceptance.

### Guidance and public artifact facts

Canonical authoring now supplies 15 skills, shared references and deterministic
Codex/Claude adapters. Preparation checks frontmatter, instruction budgets, local
links/anchors and semantic adapter identity; negative tests reject drift.
`prepare-contracts.mjs` builds the packaged data-only metadata helper in isolation
and reads 31 sealed SDK assemblies (1706 public types) plus ten UI entry points.
The generated snapshots record exact package/assembly/contract identities. They
are symbol facts, not a claim that all those types are supported extension points.
The capability-to-workflow/acceptance registry remains to be completed.

The later `candidate-recovery-3.4.0` generated a fresh `KitRecovery` application.
Its complete `certify --profile external-application` passed with the same
13/6/7/2 unit/integration/frontend/browser discovery, full per-file coverage and
three actual k6 repetitions. Run `ebc8cd6c-ba53-4ad2-9391-6134a5552774` is retained
under `/private/tmp/ngb-kit-recovery-3.4.0/.ngb/reports/`. A separate exact CLI
bootstrap upgraded the frozen 3.3.0 fixture, preserved application instructions
and a custom skill, rendered both adapters and passed isolated builds. Evidence:
`artifacts/ai-devkit/upgrade-recovery-3.4.0-2/upgrade.json`. These probes do not
establish persisted-state or full capability acceptance.

Release orchestration now invokes explicit Kit artifact/contracts/application/
upgrade/agent gates and the full platform quality mode, with separately bound
backend/frontend coverage summaries and child-evidence hash verification. The
new runner wiring still needs a complete passing candidate run. Agent task IDs,
prompts, contexts and expected outcomes are recorded in `agent-evaluations.json`.
The saved-evidence verifier rejects missing inputs/attempts/oracles and does not
invoke paid models. The skill-discovery oracle is frozen and covered by positive
and adversarial tests; the other task oracles and workflow evaluations remain
pending. Actual Claude discovery passed from both a fresh application's root and
its API subdirectory using the Team subscription. Both attempts natively exposed
all 15 NGB skills, identified the correct versions and guidance, preserved the
missing-handler refusal, and left all fixture files unchanged. The observed
model was `claude-opus-5-5`. Frozen inputs, raw transcripts and independent checks
are in `artifacts/ai-devkit/claude-discovery-3.4.0-3/`.

Earlier discovery profiles used Claude's restricted mode, which suppressed
project skill discovery. Their failed reports are retained alongside the exact
earlier profiles/oracle. The current profile permits native project discovery
while restricting tools to reading and skill loading, forbidding shell execution,
file changes, MCP and external reads. This two-context discovery result does not
establish the remaining 22 client/context/workflow cases.

On 2026-10-26 the user identified an incorrect generalization in the guidance:
absence of movements is not absence of a registered handler. Public lifecycle
resolution requires a handler, but a registered reference-register handler can
complete without adding records. Current guidance and matrix v2 distinguish both
cases. The earlier agent passes do not certify this corrected payload/matrix;
their frozen inputs and transcripts remain unchanged for audit.

The first actual Codex discovery profile also ran against the older payload.
The root attempt passed; the subdirectory answer was semantically correct but
exceeded the frozen 100,000-token acceptance ceiling (111,939 input plus 527 output
tokens). That attempt remains failed. Evidence is in
`artifacts/ai-devkit/codex-discovery-3.4.0-3/`. No token threshold was relaxed.

The package-bound contracts gate passed for `candidate-gates-3.4.0-2`. The new
starter runner initially failed before creation because it checked the future
application directory with `realpath`. It now checks acquisition isolation
against the existing staging parent instead. Its actual package-only create and
complete application certification passed afterward; evidence is in
`artifacts/ai-devkit/starter-gates-3.4.0-3/starter.json`. Later quality-source changes
require resealing before these probes can participate in a final release run.

Root UI typecheck and all 1332 unit tests passed after synchronizing 3.4.0 versions
and updating the affected development dependencies. Current root UI, starter and
standalone CRM npm lockfiles audited with zero reported vulnerabilities in those
probes. The full backend run measured all 1540 applicable source files at 100%
lines/branches/methods. The overall run failed because the existing CRM package
guard expected release 3.3.0, and 44 opt-in native Ollama tests were skipped.
The guard now expects 3.4.0; its two targeted tests passed. A complete backend
rerun is still required.
This is not complete backend acceptance. Full frontend/browser/load release gates
remain pending. Evidence: `artifacts/ai-devkit/full-backend-3.4.0-2.log` and
`artifacts/coverage/backend-full/`.

The package-only equipment/inspection acceptance runner subsequently passed all
eight tests with no skips and complete coverage of all seven source files:
73/73 lines, 8/8 branches and 10/10 methods. Evidence:
`artifacts/ai-devkit/examples-coverage-3.4.0-1/examples.json`. This remains the
minimal backend fixture, not the required complex application or browser example.

The first full frontend probe used an incorrect API-origin override and lacked
the exact browser binaries; its failure evidence is retained. The corrected run
passed all ten unit/browser projects, but exposed two uncovered report-page
branches and a Firefox launch failure before application tests. New regression
scenarios cover a single date parameter with an application-defined name and a
variant save completing while another report's metadata is loading. The focused
73-test suite passes with 100% of all four metrics; no report logic or coverage
threshold changed. Full rerun and cross-browser acceptance remain pending.

Evidence for the current tooling inputs is written to
`artifacts/ai-devkit/tooling.json`. Re-run after source changes:

```bash
node quality/ai-devkit/test-tooling.mjs
node ai-devkit/prepare.mjs
node packaging/cli/pack.mjs
```

## Remaining implementation gates

Complete capability classification and executable acceptance mapping, business
and tooling performance profiles, the complex independent application, actual agent
evaluations, full repository regression/coverage and release-stage orchestration
remain in progress. Skill presence and public symbol snapshots do not satisfy
those outstanding gates. The expanded guidance was added after the starter probe;
its new payload still needs fresh package-bound conformance evidence.

## Posting configuration and empty effects

The independent HTTP fixture successfully creates an incomplete Draft and rejects
an invalid Post. A complete inspection without any posting handler is rejected by
the existing lifecycle with `doc.posting.handler.not_configured`, even when the
Post action is explicitly registered. This proves missing-handler refusal only.
The earlier description that called all zero-effect posting unsupported was
incorrect and has been corrected in the specification, guidance and registry.

The public `IDocumentReferenceRegisterPostingHandler` contract permits completion
without adding records. The example now includes an explicit workflow handler and
separate HTTP configurations for successful Post with empty effects and rejection
with no handler. Both HTTP configurations and twelve unit tests passed against
the sealed development SDK packages. All nine source files have 100% lines,
branches and methods (79/79, 8/8 and 13/13). Evidence:
`artifacts/ai-devkit/examples-posting-3.4.0-6/examples.json`. Authorization is checked
on a complete Draft with its current version, before the authorized Post changes
that version; a stale-version conflict cannot substitute for the permission check.
The user's decision against changing SDK business behavior remains in force;
the example consumes existing public contracts and introduces no platform change.

The rebuilt `candidate-posting-3.4.0` passed artifact parity/public-contract checks
and the same fourteen independent tests. Claude also passed both discovery
contexts against this corrected payload. Codex correctly distinguished the two
posting configurations, but the root attempt returned prose in the status field
and the subdirectory attempt exceeded its frozen token ceiling. Those attempts
remain failed in `artifacts/ai-devkit/codex-discovery-posting-3.4.0/`.

The acceptance runner stages examples from the candidate's actual CLI
payload, retains raw TRX and coverage files, records compiled test sources and
binds exact executed cases to capability rows. Its adversarial checks reject
stale candidates, changed source inputs, missing cases, modified reports and
ambiguous suite identities. The registry remains not-ready until every required
classification and executable capability is complete; passing the minimal
fixture cannot certify the other capabilities.

`candidate-capability-binding-3.4.0-2` passed all fourteen example tests with
complete coverage and retained raw reports. The capability verifier bound six
actual acceptance rows and remained not-ready: 6/93 capabilities accepted and
57/1716 public contracts classified. Evidence is in
`artifacts/ai-devkit/examples-capability-binding-3.4.0-2/` and
`artifacts/ai-devkit/capabilities-binding-3.4.0-2.json`. Later runner/CLI checks
require new evidence before a final release seal.

The preceding `candidate-capability-binding-3.4.0` passed all four corrected
discovery attempts: both clients from root and subdirectory, with the frozen
budgets unchanged. The schema now specifies the status field format without
supplying the expected value. Instructions identify `versions.sdk` and
`versions.kit` to avoid speculative manifest reads. Evidence is in
`artifacts/ai-devkit/codex-discovery-binding-3.4.0/` and
`artifacts/ai-devkit/claude-discovery-binding-3.4.0/`. This remains four discovery
attempts, not the complete twenty-four-attempt workflow matrix.

Upgrade interruption tests now exercise the actual CLI during isolated restore:
SIGINT returns 130, SIGTERM returns 143, the child process exits, temporary build
files are removed and the application's original files remain byte-identical.
Evidence: `artifacts/ai-devkit/upgrade-signals-3.4.0.log`.

The full backend rerun enables the previously skipped native tests using the
already installed local `qwen3.5:9b` model. Compilation passed without warnings;
the run has already exposed a native answer-quality failure and cannot certify
the release. Preserve its final report under
`artifacts/ai-devkit/full-backend-3.4.0-3.log`; do not suppress the native tests or
substitute capability-discovery results for model/runtime acceptance.

## Persisted upgrade acceptance

The sealed `candidate-upgrade-3.4.0-1` passed core, notes and attachments upgrades
from 3.3.0 to 3.4.0 using the same disposable infrastructure across both versions.
Each profile preserved 46 application-owned files, custom skills and root
instructions. CLI and Kit upgrades and interruption/recovery checks passed.
The candidate manifest hash is
`4fe0f5b3b21935be2da2d92e7bbc13781563edbc5bfc3b8b8cf70eeb2e36cc59`;
the collected reports are indexed by
`artifacts/ai-devkit/progress-upgrade-3.4.0-1.json`. Subsequent example changes
require new candidate-bound evidence and do not modify that sealed candidate.

## Runnable equipment and inspection application

The minimal example now includes standalone Api and Migrator projects and a Vue
application consuming the published UI package. HTTP and browser tests use the
actual Api entry point with isolated PostgreSQL and Keycloak. Startup validation
checks the definition contributors and concrete DI registrations. The migrator
applies platform and application packs and verifies a repeated invocation.

UI composition explicitly registers metadata, lookup, navigation, document
actions, command palette and Work Center. Reference search and document effects,
flow and print pages use public framework adapters. The editor wrapper preserves
the framework's optional Boolean defaults so saving a new document replaces the
`/new` URL with its persistent ID. Navigation uses the SDK's `view` permission.

Development verification passed 14 component tests with all eight frontend
source files measured at 100% for lines, branches, functions and statements.
Four actual-host composition cases passed after linking the browser's personal
Work Center identity. The broader HTTP suite retains the two previously recorded
SDK replay assertions; practical exploitability has not been established and no
platform authorization change is included.

The independent-example runner now builds and tests packaged frontends in its
isolated workspace, verifies the candidate UI package, and retains unit, browser
and coverage reports even when a backend suite fails. Capability evidence checks
the exact frontend source inventory and raw report hashes; missing, changed,
skipped or retried results are rejected. Final browser and candidate-bound
results must be read from the current run's reports, not inferred from component
coverage. Both browser workflows passed in
`artifacts/ai-devkit/equipment-browser-e2e-3.4.0-14.log`: incomplete Draft validation,
reference selection, successful Post, persistence after reload and empty effects;
and a real API denial with disabled editing for an ordinary actor. The complex
example and remaining workflow evaluations are outstanding.

## Inspection reporting workflow

The `candidate-equipment-ui-3.4.0-1` package-only run completed with 29 backend,
14 frontend and two browser cases passing. The two previously documented SDK
replay assertions remain failed; no additional failure occurred. All thirteen
executable backend files had 131/131 lines, 10/10 branches and 21/21 methods;
all eight frontend files had complete coverage. The immutable candidate manifest
hash is `954dc7cfe934ecd2dc63203da7276a1edad40b84c4c76c4283bed3d41ca43b2e`.
Artifact and tooling gates passed. Raw evidence is retained under
`artifacts/ai-devkit/examples-equipment-ui-3.4.0-1/`.

The subsequent reporting implementation adds a composable inspection summary,
provider SQL bindings, standard authorized report controller and shared report UI.
Its independent expected dataset contains five Posted inspections and one Draft.
Four HTTP development cases passed grouping, filtered totals, cursor validation,
administrator/denied/inactive access and actual XLSX values. A fifth provider
case passed bounded selected-key reads and an empty selection. The browser
workflow now follows its Posted inspection into the report and checks denied
direct navigation; both browser cases passed. All fourteen component cases and
the 116-test tooling suite passed. These development probes are recorded in
`artifacts/ai-devkit/equipment-report-tests-3.4.0-2.log`,
`artifacts/ai-devkit/equipment-report-selection-3.4.0-1.log` and
`artifacts/ai-devkit/equipment-report-browser-3.4.0-1.log`.

The registry maps the exact reporting test methods and classifies 35 additional
reviewed reporting contracts (92/1716 total). These mappings do not certify
unexecuted capabilities or make the overall failing example suite pass. Fresh
package-bound reporting evidence remains required. Accounting/register effects,
background processes, integrations, broader reporting shapes and the remaining
real-agent workflow matrix are still outstanding.

The later `candidate-reporting-3.4.0-1` package run passed all five reporting
cases, 34 backend cases overall, fourteen frontend cases and both browser
workflows. The same two replay assertions remain failed. All fifteen executable
backend files have 203/203 lines, 10/10 branches and 26/26 methods; all eight
frontend files have 100% of all four metrics. Artifact and tooling gates passed,
and the post-run candidate verification passed. Candidate manifest SHA-256:
`cc9dd89340810d73588863566462e11a157d83c026c3aabc7f10e1623c02ae74`.
Evidence: `artifacts/ai-devkit/examples-reporting-3.4.0-1/examples.json`.

## Equipment usage and operational register workflow

The next independent example increment adds a usage document with a positive
hours limit, equipment dimension and an Operational Register handler. A new
immutable application migration supplies the table and stable dimension. The
separate Setup executable configures the register using public management and
maintenance services; Api does not seed or change register schema on startup.
Runtime only builds movements and uses provider-neutral read/dimension contracts.

Development checks passed ten new unit cases, four initial HTTP cases and a
separate two-equipment projection case. The lifecycle tests verify exact retry,
append-only Repost/Unpost history and 6.25-to-zero turnover reconciliation. Fault
injection observes a real written movement inside the transaction before throwing;
the subsequent independent read confirms rollback of effects, status and version.
Setup is tested twice on an empty database and again after posting. The latest
addition also asserts rejection of removing established resources or dimensions;
that addition still needs the new package-bound run. Projection tests verify
independent totals of four and five hours, one-row pages, dimension filters and
repeat finalization without further work.

The third browser workflow creates usage and displays its actual movement and
enriched equipment reference. All three browser workflows and fourteen frontend
cases passed; all eight frontend files retain 100% coverage. Development reports
are `equipment-usage-http-3.4.0-3.log`, `equipment-usage-projections-3.4.0-1.log`
and `equipment-usage-browser-3.4.0-1.log` under `artifacts/ai-devkit/`.
Registry review now covers 110/1716 public contracts and 95 capability rows,
including explicit configuration/finalization operations. Full candidate testing
and the remaining capabilities are still required; no overall readiness is claimed.

The sealed `candidate-usage-3.4.0-1` subsequently passed artifact/tooling checks
and post-run release identity verification. Its independent application run has
49 passing backend cases and the same two previously recorded replay assertions,
14 passing component cases and three passing browser workflows. All 23 executable
backend files have 287/287 lines, 26/26 branches and 42/42 methods; eight frontend
files retain 100% across all four metrics. The schema-removal negative assertions
also passed. Evidence: `artifacts/ai-devkit/examples-usage-3.4.0-1/examples.json`.
Candidate SHA-256: `785b03d16b5734b4a6cead0d9c093b0c63d445eab1714c87b997c46048d2be3a`.

## Equipment assessment and reference register workflow

The independent example now includes a separate equipment-assessment document,
its validation/reader/metadata and a nonperiodic Reference Register subordinate to
the recorder. Setup configures fields and dimensions through public services.
Post/Repost emit current values; Unpost relies on the engine's recorder tombstones.
The original inspection remains a zero-effect document.

Three development integration cases and ten unit cases passed temporal history,
repeat Setup, incompatible metadata rejection, one-row history/current pages,
filtered enrichment, recorder isolation, concurrent Post and rollback after a
real write. The first setup probe found that RR dimension IDs must be derived
from their normalized code. The unpublished example's arbitrary equipment UUID
was corrected to the public deterministic value, shared by both register examples;
the unit suite checks it. Existing sealed candidates are preserved. This is not
an installed-data migration recipe and no SDK implementation changed.

Browser probes also corrected two test assumptions: the Fields cell includes its
label, and Unpost requires the standard confirmation dialog. Effects projects the
current Posted document and is empty after Unpost; historical tombstones are
verified through the public reader, not the Effects page or its synthetic row IDs.
A prior inspection test now waits for lookup selection and explicit result-field
focus before typing. Failures remain in the development logs; final package-bound
validation is still required. Registry review covers 125/1716 public contracts
and 96 capability rows, without claiming complete evidence for other RR modes or
periodicities. Relevant logs: `equipment-condition-tests-3.4.0-3.log`,
`equipment-condition-ui-3.4.0-1.log`, and the numbered
`equipment-condition-browser-3.4.0-*.log` under `artifacts/ai-devkit/`.

The sealed `candidate-reference-3.4.0-1` passed artifact/tooling gates and the
post-run identity check. Independent package acceptance passed 62 backend cases,
14 frontend cases and all four browser workflows. The same two replay assertions
remain failed; no SDK authorization changes were made. All 30 executable backend
files have 368/368 lines, 36/36 branches and 56/56 methods. All eight frontend
files have 100% across all four metrics. Candidate SHA-256:
`4c640f4d69acd99a6803de6c97a737810ba84b2d116ca952bf1f4f384bc5f0c7`.
Evidence: `artifacts/ai-devkit/examples-reference-3.4.0-1/examples.json`.


## Equipment service and the combined effect transaction

A further independent document now combines Accounting, OR and RR handlers. Its
known acceptance values are a 125.50 expense/payable entry, 2.5 service hours in a
separate operational register and recorder-owned condition `Ready after service`.
Explicit Setup creates the two example-owned accounts and rejects incompatible
existing configuration, while preserving custom labels and account IDs.

Development integration passed lifecycle/reconciliation, one-row journal history,
Setup preservation/refusal and atomic rollback after an audit fault observed all
three written effects. Fifteen new unit cases and fourteen frontend cases passed.
Initial browser acceptance needed its account-label assertion corrected to the
standard code-plus-name display. Logs are `equipment-service-http-3.4.0-2.log`,
`equipment-service-unit-3.4.0-1.log`, `equipment-service-ui-3.4.0-1.log` and numbered
`equipment-service-browser-3.4.0-*.log` under `artifacts/ai-devkit/`. Raw development
reports are retained in `equipment-service-development-3.4.0-1/`.

The registry now has 100 capability rows and 148/1716 classified public contracts.
Accounting configuration, posting, combined transaction, journal paging and trial
balance are mapped to exact independent cases; period/fiscal-year closing and the
rest of the accounting/reporting surface remain outstanding. This increment still
requires a fresh sealed package run; no overall release readiness is claimed.

The sealed `candidate-service-3.4.0-1` subsequently passed artifact and tooling
profiles and post-run candidate verification. Its package-only application passed
80 backend cases, 14 frontend cases and five browser workflows; only the same two
previously observed replay assertions failed. All 37 executable backend files
have 494/494 lines, 74/74 branches and 76/76 methods. Eight frontend files have
100% across all four metrics. Candidate SHA-256:
`90c1a6e54592352b3f247f5989f8ee55fea279107f1085ccb07c1ae509dee193`.
Evidence: `artifacts/ai-devkit/examples-service-3.4.0-1/examples.json` and the
compact `artifacts/ai-devkit/progress-service-3.4.0-1.json`.

## Financial periods and browser capability evidence

The application now composes the public period-closing UI with the existing
authorized controller. Independent HTTP scenarios verify closed-period refusal,
sequential closing and explicit reason-based reopening, snapshot correction,
administration and denied actors, retained-earnings eligibility, two equipment
dimensions through year close/reopen/reclose, and atomic rollback after real
balance and closed-marker writes. Concurrent closing of an empty year completes
once and writes no synthetic accounting activity. No SDK implementation changed.

Initial probes corrected a duplicate role in test setup and a simulated future
system clock that excluded posting-state reads from their default query bounds.
Historical document dates now use the public draft service in trusted fixture
setup with real clocks. Four HTTP scenarios passed in
`equipment-period-http-3.4.0-4.log`; the empty/concurrent case passed separately in
`equipment-period-http-3.4.0-5.log`. Browser close/reopen and denied direct
navigation passed against real services in `equipment-period-browser-3.4.0-1.log`.
Fourteen component tests retained 100% per-file coverage.

Capability evidence previously checked complete frontend reports but could only
bind named C# cases. It now also binds exact packaged browser sources, Playwright
project names and describe/test title paths. Changed roots, unknown files,
duplicate cases, unexecuted titles, altered reports and retries are rejected.
The complete tooling suite passes 119 tests with 16 modules at 100% per-file
coverage. These changes do not bypass the requirement for the overall independent
example to pass before capability certification.

The registry has 107 capability rows and 172/1716 reviewed public contracts.
Period UI evidence covers month interactions; fiscal-year browser workflows,
remaining register modes, background workers and agent evaluations are still
outstanding. A new sealed candidate run is required for this increment.

The sealed `candidate-periods-3.4.0-1` passed artifacts/contracts profiles and
post-run identity verification. Package-only acceptance has 85 passing backend
cases and the same two known replay failures, 14 passing frontend cases and six
passing browser workflows. All 37 executable backend files remain at 494/494
lines, 74/74 branches and 76/76 methods; eight frontend files retain all four
metrics at 100%. Candidate SHA-256:
`6cc6d39b125775a490b5712d728e4700a29e1281c21d113316c6d7da515cae55`.
Evidence: `artifacts/ai-devkit/examples-periods-3.4.0-1/examples.json` and
`artifacts/ai-devkit/progress-periods-3.4.0-1.json`.

## Independent background worker

The reference application now has a separate SDK Hangfire host and shared
`AddEquipmentRuntime` registration for its API and worker. The worker enables
only the existing OR finalizer every five minutes in UTC and emits standard
console diagnostics. Application migration/Setup remain explicit deployment
steps. No platform business or permission implementation changed.

Development acceptance executes the stored recurring job through the actual
worker, verifies 6.25 hours and zero after Unpost, repeats execution, restarts on
the same storage and removes registrations with scheduling disabled. It checks
health and a real Keycloak challenge for the protected dashboard. Test-only
contributed jobs verify Failed-state history, no automatic retry, explicit
requeue, overlap refusal and cancellation releasing the distributed lock.

Probes corrected the bundled Hangfire API signature, the null cancellation-token
placeholder used only in queued expressions, and the missing worker HTTPS
callback in the disposable Keycloak realm. Auth checks remain enabled. The retry
case passed in `equipment-worker-tests-3.4.0-2.log`, overlap/cancellation in
`equipment-worker-tests-3.4.0-3.log`, and the complete lifecycle in
`equipment-worker-tests-3.4.0-5.log`. Earlier diagnostic failures are retained.

The separate `ngb-background-jobs` skill and reference describe catalog/schedule
ownership, actual retry semantics, health and projection timing. The capability
registry has 113 rows and 196/1716 reviewed contracts. This increment still needs
package-bound validation; other built-in jobs, external integrations, remaining
register/report variants and the agent workflow matrix remain outstanding.

The sealed `candidate-worker-3.4.0-1` subsequently passed artifacts/contracts
profiles and post-run candidate verification. All three worker scenarios passed
in package-only acceptance: 88 backend passes and the same two previously
observed replay failures, 14 frontend passes and six browser passes. All 39
executable backend files have 512/512 lines, 74/74 branches and 79/79 methods;
eight frontend files retain 100% for all metrics. Candidate SHA-256:
`a85d913d56e55a0367ef06b9fb4ee83952c23793026747a8c818caa7b20b0115`.
Evidence: `artifacts/ai-devkit/examples-worker-3.4.0-1/examples.json` and
`artifacts/ai-devkit/progress-worker-3.4.0-1.json`.

## Independent and periodic reference-register contracts

Four independent provider scenarios now exercise all six periodicities, effective
versus recorded time, bucket-scoped history, independent command idempotency,
tombstones, concurrent retry, filtered/enriched cursor paging and caller-owned
transaction rollback for both upsert and tombstone. They use trusted public SDK
services in the disposable fixture; no HTTP mutation endpoint or SDK behavior is
added. Guidance explicitly distinguishes the service from an authorization
boundary and explains the single as-of parameter and deletion semantics.

Initial probes corrected required history-period arguments, the recorder validation
exception type and audit queries that had included already committed register
configuration. All four scenarios passed in
`artifacts/ai-devkit/equipment-independent-reference-3.4.0-2.log`. The subsequent
subordinate-mode refusal assertion is included for the package-bound run. The
registry now has 118 capability rows and 199/1716 reviewed public contracts. This
increment requires a new sealed candidate; the overall Kit remains incomplete.

`candidate-independent-reference-3.4.0-1` passed artifacts/contracts profiles and
post-run identity verification. All four new cases passed, but one worker case
failed before execution: the first application migration timed out opening its
PostgreSQL connection during concurrent container startup. Raw TRX retains the
Npgsql connection timeout. The run has 91 backend passes and three failures
(the setup timeout plus the two known replay assertions), with 14 frontend and
six browser passes. Its evidence is preserved under
`artifacts/ai-devkit/examples-independent-reference-3.4.0-1/`.

The integration assembly now explicitly limits concurrent test collections to
two using xUnit's conservative scheduler. This bounds disposable PostgreSQL and
Keycloak instances instead of deriving their count from CPU threads. Internal
concurrent-command tests and all existing timeouts remain unchanged. Runner
diagnostics are enabled to verify the applied configuration in the next candidate.

The registry also binds application authorization, document effects and transaction
composition to nine actual discovered passing cases. Other unbound rows are not
inferred from nearby coverage: typed-storage overrides, invalid definitions,
cross-type lookup and previous-version migration require their own evidence.

The corrected `candidate-independent-reference-3.4.0-2` passed artifacts/contracts
and post-run verification. Its TRX confirms `collections [2 threads]`. Package-only
acceptance has 92 backend passes and only the two known replay failures, with no
container-startup failure. All four new register scenarios and three worker cases
passed together. Fourteen frontend and six browser tests pass; all 39 executable
backend files retain 512/512 lines, 74/74 branches and 79/79 methods. Candidate:
`48a1eb60f6188b80909ac896fee278bd8b36d9045f758dd7b795031480046d52`.
See `artifacts/ai-devkit/progress-independent-reference-3.4.0-2.json`.

## Work Center service and HTTP contracts

Three new provider/HTTP scenarios exercise actual task and notification services,
test-owned preference definitions and the SDK's existing API controllers. They
cover role/user assignment, repeated commands, competing versioned claims,
read/dismiss/snooze state, completion/cancellation, notification cursor pages,
mandatory and optional preferences, source permissions, inactive recipients and
an enclosing transaction's rollback. Production policies and platform behavior
are unchanged. Outbox retries, event projection and hub/browser delivery remain
separate outstanding cases; no external messages are sent.

After correcting the DTO namespace, all three initial cases passed in
`equipment-work-center-3.4.0-2.log`; the expanded paging/source-access/personal-task
cases also passed in `equipment-work-center-3.4.0-3.log`. Raw TRX is preserved in
`artifacts/ai-devkit/equipment-work-center-development-3.4.0-1/`. The registry has
118 capabilities, 64 with complete source bindings, and 213/1716 reviewed public
contracts. These changes still require a new sealed package run.

The sealed `candidate-work-center-3.4.0-1` passed artifacts/contracts profiles and
post-run identity verification. All three Work Center cases, four independent
register cases and three worker cases passed together. Package-only acceptance
has 95 backend passes and the two previously observed replay assertions, plus
14 frontend and six browser passes. There were no skips or new setup failures.
All 39 executable backend files retain 512/512 lines, 74/74 branches and 79/79
methods; eight frontend files retain all four metrics at 100%. Candidate SHA-256:
`7dcab05b08f305512c3825a5b12087ee1f871b3d12c19867853b41b371e885d4`.
Evidence: `artifacts/ai-devkit/examples-work-center-3.4.0-1/examples.json` and
`artifacts/ai-devkit/progress-work-center-3.4.0-1.json`.

After verification, this progress note and the human guide's missing background-job
skill listing were updated. Those documentation edits are outside the Kit payload;
the next whole-tree candidate must be sealed with them. The candidate above is
historical evidence, not a declaration that every Kit release gate passed.

## Document-event policy and transactional outbox

The independent application now contributes a withdrawal policy and preference
definitions. An inspection's completed `unpost` event creates a personal review
and optional informational notification for its linked actor. Stable event-based
keys preserve identity across retries; this historical review is independent of
subsequent document status. Unrelated actions and unlinked actors create no work.
Platform business logic and authorization remain unchanged.

Two HTTP/provider cases verify the actual dispatcher event, both writes inside
the projection transaction, later policy failure/rollback, delayed retry, action
replay, repeated policy execution and after-commit visibility during the notifier
callback. A separate rolled-back envelope cannot deliver; a realtime exception
cannot undo a committed projection. Two standalone provider cases verify the
25-event batch bound, concurrent claims, expired leases and attempt fencing,
fresh runtime-container retries, and the eighth-failure dead letter. The timing
probes use a controlled clock with real PostgreSQL. They do not claim process-kill
recovery, manual redrive, SignalR transport or browser acceptance.

All four integration cases and six unit cases passed in development. Evidence:
`artifacts/ai-devkit/equipment-outbox-development-3.4.0-1/`; strengthened HTTP
assertions passed in `equipment-outbox-3.4.0-2.log`, delivery contracts in
`equipment-outbox-delivery-3.4.0-1.log`, and unit cases in
`equipment-withdrawal-unit-3.4.0-2.log`. The first unit compilation corrected the
source-reference property names; the initial unit runner needed local-socket
permission. Earlier outputs are retained. Registry: 118 capabilities, 66 with
complete source bindings, 225/1716 reviewed public contracts. A new sealed package
run remains necessary; the overall Kit is incomplete.

`candidate-outbox-3.4.0-1` subsequently passed artifacts/contracts and post-run
identity verification. All four new integration and six unit cases passed in
package-only acceptance: 105 backend passes and the same two known replay
assertions, 14 frontend passes and six browser passes. All 41 executable backend
files have 542/542 lines, 80/80 branches and 82/82 methods; eight frontend files
retain all metrics at 100%. Candidate SHA-256:
`5ba65deabe3f8d94c17c5e24b885e367d5f76798936b50b6bd6c7b41c22281d5`.
See `artifacts/ai-devkit/progress-outbox-3.4.0-1.json`. Subsequent source edits need
a new candidate; this historical record does not certify the remaining Kit scope.

## Public Work Center UI and SignalR

The external example now maps `/work-center` and `/settings/notifications` to the
public package components. A two-tab Playwright scenario observes the actual
SignalR handshake and `workCenterChanged` invocation, then confirms committed
items appear without refreshing. It dismisses the notification, snoozes the task,
persists a disabled optional preference, and verifies a later withdrawal still
creates a mandatory task. The UI is not supplied with mocked HTTP/hub responses.

The development scenario passed in `work-center-ui-browser-3.4.0-3.log` after
correcting the expected no-content status and the actual Needs Attention label.
Earlier browser diagnostics are retained. The route composition's existing 14
frontend cases retain 100% coverage. The final source uses the specific public
`@ngbplatform/ui/work-center` entry; package validation must verify that final
import and the complete seven-scenario browser suite. Registry: 118 capabilities,
67 with complete source bindings, 225/1716 reviewed contracts. The overall Kit and
the required agent workflow evaluations remain incomplete.

The final specific-subpath composition passed in `candidate-work-center-ui-3.4.0-1`:
artifacts/contracts profiles and post-run candidate identity all pass; 105 backend
passes plus the same two known assertions, 14 frontend cases, and all seven browser
scenarios. Backend remains 41 executable files at 542/542 lines, 80/80 branches
and 82/82 methods; frontend remains eight files at 100% for every metric. Candidate:
`30abf4ee063879ceaac440ff716517529362d8fc136c850963e76d4d03cc5651`.
See `artifacts/ai-devkit/progress-work-center-ui-3.4.0-1.json`. This note was added
after verification; subsequent source changes need a new whole-tree candidate.

## Custom inspection action contracts

The independent example now contributes an explicit reason-required one-day draft
deferral. Five runtime types separate metadata, mutation, authorization, availability
and bounded fact loading. The action requires source view plus draft-edit permission,
joins the dispatcher transaction and uses the public draft service; no SDK behavior
is changed. Four HTTP/provider cases and eighteen unit cases passed in development:
`equipment-deferral-http-3.4.0-2.log` and `equipment-deferral-unit-3.4.0-1.log`, with
raw TRX under `equipment-deferral-development-3.4.0-1/`. The initial HTTP compilation
needed the public audit-code namespace; that diagnostic is retained.

The document reference's old "blocking test" wording was also aligned with the
already recorded `observed` replay decision: preserve failed assertions and the
unproven-exploitability qualification, continue Kit work, and do not reopen SDK
investigation. Test expectations and quality gates remain unchanged. This increment
still needs package-bound validation before it can be reported as verified.

The sealed deferral candidate `5490cc24d98632b63c9200d486c3474d36a88c8d5a2921b53da585201bd5c263`
passed artifact/contract and post-run identity checks. Its four HTTP action cases
passed; aggregate backend result was 127/129, with only the two retained replay
assertions failing. Frontend 14/14 and browser 7/7 passed. Coverage identified one
unvisited availability branch (99/100 branches). Three additional cases now reject
null, string and integer fact values; `deferral-unit-coverage-3.4.0-1` passes 21
cases and covers that file's 10/10 branches. The next full candidate must confirm
aggregate completeness. Raw previous evidence is preserved unchanged.

## Notes and parent audit acceptance

The independent API's existing public content composition is now exercised with
Notes explicitly enabled. Five HTTP cases cover both administrator identities,
catalog/document parents, input limits, cursor paging, competing edits, deletion
marks, separate parent/content permissions, inactive identities and disabled
features. Public parent audit verifies actor/target/note attribution and text
changes, including filtered pages whose cursor must still advance. No SDK source
or permission rules were changed.

Four cases passed in `notes-http-3.4.0-1.log`; the access fixture initially tried
to create its role twice. Its second diagnostic exposed the fixture's bypass of
access-version invalidation. The final fixture changes grants through the public
security API; `notes-http-3.4.0-3.log` passes the remaining case. Both diagnostics
and raw TRX are retained. The real browser case in `notes-browser-3.4.0-1.log`
passes create/edit/reload/delete, literal HTML rendering and a separate reader
session with disabled mutation controls. The browser host enables Notes and
seeds that reader explicitly; ordinary example deployments still opt in through
configuration. Registry: 76/118 capabilities have all acceptance source bindings,
254/1716 public contracts reviewed. Package-bound acceptance remains pending.

Candidate `candidate-notes-3.4.0-1` completed package-only acceptance and passed
post-run identity verification with SHA-256
`2d627fd5816948cae45fd7a8a9bd0306ae9808628fe8b6c80769633e32b12650`.
All note cases pass together: backend 135/137 (the same two retained assertions),
frontend 14/14 and browser 8/8. All 46 executable backend files are present with
587/587 lines, 100/100 branches and 90/90 methods; frontend eight files remain at
100% for all metrics. Artifact/contracts profiles pass. The compact receipt is
`artifacts/ai-devkit/progress-notes-3.4.0-1.json`. This historical note was appended
after verification; it does not turn the incomplete Kit into a release candidate
that satisfies all certification gates.

## Real attachment storage acceptance

The independent API now optionally selects the public MinIO adapter through the
feature-gated storage callback. Application configuration owns its endpoints,
credentials and private bucket. The disposable fixture provisions a bucket-scoped
user; the example does not use production storage or root credentials for runtime
operations. Five HTTP cases cover real bytes, concurrent completion, sealed-object
isolation from later staging PUTs, metadata validation, reserved quota, bounded
paging, access checks, logical deletion, retained audit-download and explicit retry
after storage credential access is restored. Expiration advances an injected clock
and invokes the public service; no immutable creation timestamp is rewritten.

Development `attachments-http-3.4.0-1.log` passed three cases. Its two diagnostics
were fixture problems: restarting the container did not preserve the selected
storage endpoint, and repository Save does not mutate immutable creation time.
The revised cases use scoped credential disable/enable and a controllable clock;
`attachments-http-3.4.0-2.log` passes both. This checks storage access failure,
not every network restart mode. The real browser in `attachments-browser-3.4.0-1.log`
passes file upload/download byte comparison, persistence, reader controls and
deletion. Raw TRX and browser reports remain under `artifacts/ai-devkit/`.

Registry: 81/118 capabilities have complete acceptance bindings; 267/1716 public
contracts reviewed. The next sealed candidate must verify the combined source.
The discovery profile also records the locally installed Codex CLI change to
0.165.0-alpha.1.2; its supported invocation flags and disabled feature inventory
were checked locally. Personal instruction identity and the disabled skill
inventory remain unchanged. Fresh real-agent results are still required.

Candidate `candidate-attachments-3.4.0-1` passed post-run identity verification
with SHA-256 `cd99b8b394d708fdd932623ace4c5ab8788f247042dc4fc27c5cd3a51deae23d`.
Backend 140/142 passed, including all five attachment cases; the same two retained
replay assertions remain. Frontend unit 14/14 and eight of nine browser cases
passed, including real attachments. An existing usage-form case saved null hours.
The browser fixture now enters numbers through keyboard events and asserts the
value before saving. Two affected workflows each passed three independent repeats
in `numeric-browser-3.4.0-2.log`; unique equipment names isolate those repetitions.
The first repeated diagnostic reused names and failed on ambiguous lookup options;
its raw report remains intact. The next combined run must confirm the correction.

The attachment candidate covered 588/588 lines and 90/90 methods; the nested MinIO
configuration lambda introduced a compiler-generated cached-delegate branch,
leaving 101/102 branches. Passing the captured configuration section's Bind method
directly removes that unnecessary nested delegate without changing configuration.
Four fresh real discovery attempts (Codex and Claude, root and subdirectory) all
passed against this candidate. Their identities and raw transcripts are retained;
these do not replace the outstanding workflow evaluations.

## Opt-in typed runtime MCP example

The independent API now composes the public MCP package and a scoped equipment
read handler through an explicit contributor. The handler calls the existing
authorized catalog service and supplies an explicit source from that result.
No SDK, business service, schema or authorization policy was changed. The host
remains disabled unless application configuration opts in.

`mcp-http-3.4.0-5.log` passes both actual HTTP scenarios. They verify discovery,
read result/source shape, malformed and missing IDs, input/body bounds, unchanged
record/audit despite instruction-like content, absent mutation tools, cancellation,
active reader denial followed by a public permission grant, inactive and anonymous
identities, denied Origin and disabled host. Handler/contributor coverage is 100%
in the development report; aggregate package-bound coverage is still pending.

The test login uses the pinned disposable Keycloak's authorization-code flow and
PKCE. Earlier diagnostic runs used an unsuitable password-grant token, then found
HttpClient's Secure-cookie handling on loopback and an unlinked ordinary test user.
The fixture now preserves the real login exchange and links its reader explicitly;
no token claims or platform authentication handlers are replaced. All raw TRX
reports remain under `artifacts/ai-devkit/mcp-development-3.4.0-*`.
Registry: 83/118 capabilities have complete acceptance source bindings and
278/1716 public contracts have reviewed classifications. Overall Kit remains
incomplete, including additional capability acceptance and agent workflows.

Candidate `candidate-mcp-3.4.0-1` passed post-run identity verification with
SHA-256 `239a453a543e080438ca2698532aba1f29c547bee50d027dde8b7e403eb91ae9`.
Backend 142/144 passed with only the two retained replay assertions failing.
All 48 executable backend files are present: 612/612 lines, 102/102 branches,
96/96 methods. Frontend unit 14/14 and eight of nine browser cases passed;
MCP and both corrected numeric-entry workflows passed. The remaining report
browser assertion used a local-day default that excluded a newly created UTC
next-day document. The test now passes an explicit date range and equipment
filter through the public report route context. Two repetitions passed in
`report-period-browser-3.4.0-1.log`. Historical reports remain unchanged.

## Catalog composition and public frontend entries

The independent runtime now extends its equipment catalog with a registered
single-line name validator. Four new integration cases pass in
`catalog-http-3.4.0-1.log`: effective-field upserts and unchanged audit on failure,
bounded authorized lookup, typed draft storage participating in an outer rollback,
and refusal to start when the declared validator is missing from DI. Nine unit
cases pass for metadata preservation, invalid definitions, text validation and
cancellation; combined new-file coverage is 100%. This development invocation
used one TRX filename for two projects, so the unit TRX overwrote the integration
TRX; the console log and both coverage reports remain, and the forthcoming normal
package runner writes each project's results into separate directories.

The frontend now imports the public navigation and lazy entry points directly;
shared pages load on navigation. A useful unknown-route page uses the public
layout header. Its browser test checks theme changes, Work Center drawer-to-page
navigation, mobile bounds and the emitted public assets. The first diagnostic
expected the header button to navigate immediately; the corrected scenario uses
the drawer's View all action and passes in `frontend-entries-browser-3.4.0-2.log`.
The report test initially imported the contracts entry for enums and request types;
the fresh package-only run below corrected its Node runtime assumption.

New Vitest imports initially caused Vite dependency reoptimization during tests.
The final configuration explicitly prebundles the public subpaths and deduplicates
Vue. `frontend-entries-unit-3.4.0-3.log` passes 15/15 and nine source files at 100%
lines, branches, functions and statements. Thresholds and source inventory are
unchanged. Registry: 98/118 capabilities have complete acceptance bindings;
286/1716 public contracts are reviewed. New combined package evidence is pending;
remaining document/report/assistant workflows and real agent development attempts
are still required.

## Report variants, streaming and specialized execution

The combined catalog/UI candidate `f7d13cefeeae1f9f16bf89eb84bf7ac75060744af673ea7d2d85de0bc83ebcb3`
passes artifacts, contracts and post-run verification. Backend 155/157 passes;
only the two retained historical replay assertions fail. All 50 executable files
have 625/625 lines, 110/110 branches and 99/99 methods covered. UI unit 15/15 passes.
Browser discovery failed because Node 24 cannot strip the package's TypeScript
runtime enum under node_modules. The corrected test uses a type-only contract
import and asserts the raw HTTP enum names before UI normalization. The first
diagnostic incorrectly expected numeric wire values; the second full development
run passes all 10 scenarios in `report-browser-node-3.4.0-2.log`. Both raw reports
are preserved; historical candidate evidence is unchanged.

Variants now explicitly use `HttpReportVariantAccessContext` in the example host.
`variants-http-3.4.0-1.log` caught the missing registration; `-2` passes both real
HTTP cases, covering separate private owners, shared mutation permissions, default
replacement, route identity, limits and execution of saved filters. The unlinked
administrator gets an owner projection through the existing public service.

`streaming-http-3.4.0-1.log` passes the real 501-document test, including a concurrent
HTTP insertion between fetches, bounded batches, repeatable-read behavior,
cancellation cleanup and subsequent reuse of the provider. The specialized report
passes two HTTP cases in `specialized-http-3.4.0-3.log`; both new executable files
have 100% lines and branches. Earlier compilation/layout diagnostics are retained.
The independent expectation is 3 Passed / 5 Posted = 60%, with Draft exclusion and
an undefined percentage for empty input. No platform business source changed.

The aggregate integration suite took 8m53s before these additions. Its runner now
allows 20 minutes per test project; restore/build/UI operations retain their
10-minute bound. No test is skipped or coverage threshold relaxed. New package
verification and remaining document, migration, assistant and real agent development
evaluations are still required.

## Document composition and head/part persistence

Reporting candidate `3db4c91ffdc48b07fc891693b0766087ccea9144a63daf140db846d4c0e56dd1`
completed its second aggregate attempt and passed post-run identity verification.
Backend 160/162 passed with only the two retained historical replay assertions;
52 executable files reached 699/699 lines, 114/114 branches and 105/105 methods.
Frontend unit 15/15 passed. Browser 9/10 passed; usage entry retained `6` instead
of `6.25` before saving. Eight instrumented keyboard repetitions did not reproduce
the failure, so no exact cause is asserted. The fixture now fills the complete
number, blurs and verifies both the input and actual saved decimal. Usage and
combined-engine workflows each passed five repetitions in
`numeric-fill-browser-3.4.0-1.log`; the earlier diagnostics remain unchanged.

The first reporting aggregate attempt was interrupted and produced no final
example report. Its raw integration evidence was preserved separately. The
second attempt, rather than the interrupted run, supplies the counts above.

The independent inspection now uses a registered numbering policy and an explicit
reinspection derivation. Six HTTP scenarios pass across
`document-composition-http-3.4.0-3.log` and `document-mirror-http-3.4.0-1.log`;
two definition checks pass in `document-composition-unit-3.4.0-3.log`. They exercise
parallel number allocation, lifecycle preservation, stable cursor pages,
cross-type lookup with separate Lookup permissions, one derived Draft on replay,
endpoint/cardinality checks, graph bounds, bidirectional links, inaccessible graph
node filtering and rejection of incompatible mirrored-field metadata. The earlier
prototype diagnostics and their source manifest are retained in artifacts.

The API-only checklist adds an immutable application migration, custom typed
head/part storage and separate common-Draft, payload, completeness and approval
contracts. `checklist-http-3.4.0-1.log` caught a missing audit-constant import.
The second run passed four storage/validation cases and exposed an incorrect
assumption in the example: generic Post does not invoke a merely registered
ApprovalPolicy. The final Post validator explicitly resolves and calls the
registered policy inside the posting transaction. No SDK code was changed.
`checklist-http-3.4.0-3.log` passes all six cases, including both administrator
kinds, 501 rows, omitted/replaced/cleared parts, real post-write rollback and
typed Draft deletion. `checklist-unit-3.4.0-1.log` passes 16 cases. Combined
development coverage for its 11 new executable files is 107/107 lines,
22/22 branches and 25/25 methods. The review flag is an example completion
condition, not a separate approver authorization workflow.

Registry: 111/118 capabilities have complete acceptance source bindings and
314/1716 public contracts have reviewed classifications. This is source linkage,
not a claim that final capability certification has passed. A new combined sealed
candidate, remaining migration/assistant acceptance, public-contract review and
real agent development evaluations are still required.

## Frozen application-schema migration acceptance

The checklist browser build and all ten workflows pass in
`checklist-browser-3.4.0-1.log`; its raw report is preserved under
`checklist-browser-development-3.4.0-1`. Tooling before the migration fixtures
passes 119 tests and 16 modules at complete per-file coverage.

Two resource-only migration fixture assemblies were added without contributors.
The baseline's four SQL files come from the verified reporting template artifact,
with exact hashes and candidate identity in its manifest. The deliberately changed
first script is isolated in the negative checksum fixture. This proves an earlier
application-schema revision on SDK 3.4.0, not an additional SDK-version upgrade.

The first development run caught a wrong embedded-resource prefix; the public
migrator requires the assembly name. The next diagnostic caught a process-fixture
mistake: the Migrator DLL copied into the test output lacked dependencies that the
test host obtains from ASP.NET's shared framework. The fixture now runs the
Migrator's own complete build output. `migration-http-3.4.0-4.log` passes all three
cases: persisted baseline upgrade and repeated runs, checksum rejection before
API composition in the test startup sequence, and actual CLI discovery/dry-run/
missing-storage exit codes. Application deployments must stop their own startup
sequence on migration failure; the API does not automatically run migrations.
The initial sandboxed restore hung and was cancelled; the bounded single-node
restore outside sandbox succeeded. No platform business sources were changed.

Registry now binds 113/118 capabilities. The five remaining capability workflows
are Assistant hosting and provider configuration. Public-type classification,
real agent development evaluations and final combined certification remain open.

## Documents/migrations candidate and Assistant acceptance

Candidate `candidate-documents-migrations-3.4.0-1`, identity
`25b737f1e74ef8c52ef5b5badf643f68c158ff633ce72286e81f71b4ef35bb0b`,
completed its isolated package run. Backend: 193/195 pass, with only the two
unchanged historical replay assertions. Frontend: 15/15 unit and 10/10 browser.
All 65 executable backend files have 845/845 lines, 136/136 branches and
134/134 methods covered. Release verification passed after completion; no new
regression or practical-exploit conclusion is attached to the historical cases.
See `artifacts/ai-devkit/progress-documents-migrations-3.4.0-1.json`.

The Assistant prototype was developed outside canonical sources during that run.
Its first restore used a wrong relative NuGet configuration path; the corrected
restore succeeded. The first development run passed all six HTTP cases and the
disabled-registration case, but the 19 remaining configuration cases omitted the
required MCP feature. The test configuration now enables it and separately proves
its absence is rejected. `assistant-configuration-3.4.0-2.log` passes all 20 cases.
`assistant-request-3.4.0-1.log` passes both timeout/client-disconnect cases with real
HTTP concurrency rejection and observed transport cancellation.

The example now selects an explicit provider through application composition.
All four published native adapters use real current-user MCP reads in the HTTP
tests, with only vendor protocol responses supplied by deterministic fixtures.
The Bedrock fixture uses anonymous test credentials and never consults a personal
credential chain. These results do not claim live vendor or model-quality coverage.

All 118 capability rows now have executable acceptance source bindings; 341/1716
public contracts have reviewed classifications. This is not final certification.
The new combined candidate, remaining public-contract review and real agent
development evaluations are still required.

## Assistant candidate verification and public-surface review

Candidate `candidate-assistant-3.4.0-1`, identity
`9e0a20481f359ab2dfe168fedb73d1e2a5a7be174220dfc8dfd5feee406b86ef`,
passes artifacts/contracts profiles and post-run identity verification. Its
package-only example executes 223 backend cases: 221 pass, including all 28 new
Assistant cases, with only the two unchanged historical assertions failing.
All 15 frontend unit and 10 browser cases pass. Backend coverage is complete for
66 executable files: 867/867 lines, 142/142 branches and 135/135 methods. The exact
receipt is `artifacts/ai-devkit/progress-assistant-3.4.0-1.json`.

Source review classified another 106 DTO/metadata/limit contracts, with individual
source hashes in `contracts-review-3.4.0-1.json`. It also identified an actual gap
in the initial 118-operation inventory: standard General Journal Entry workflows
were mentioned but not separately mapped to acceptance. They cannot be counted
as ordinary accounting posting-handler coverage. Nine distinct operation rows
now cover its typed Draft, approval, posting, reversal, automatic reversal,
bounded reads, deletion marks, authorization and trusted atomic composition.

The first journal prototype incorrectly reused generic effects reads and assumed
Rejected journals could be edited. Package/source evidence shows the dedicated
typed workflow and immutable Rejected state. The final tests use the public
bounded accounting reader for reconciliation and preserve those SDK semantics.
`general-journal-development-3.4.0-2.log` passes five HTTP cases. The validation
run passes invalid-dimension rollback; its atomic case initially omitted mandatory
reason code. `general-journal-atomic-3.4.0-2.log` passes the corrected case, including
the exact unbalanced-lines exception and no partially created document. No SDK
business sources were changed. Registry: 127 mapped capabilities, 482/1716 reviewed
public contracts. Newly discovered public workflows may require further rows;
source linkage is not final certification.

An independent document-change agent oracle is prototyped outside the application
under `artifacts/ai-devkit/evaluation-draft-3.4.0-1`. Its HTTP checks use a separate
test project and real identity/provider fixtures. Correct implementation passes;
removing only Post note validation is detected as HTTP 200 instead of 400. Initial
oracle assumptions about the missing-handler status and Draft-edit action version
were corrected against existing behavior before inference. No real development
agent attempt has run yet. The frozen runner, source review, remaining task
oracles and their controls are still required.

## Independent development oracles

`run-development-oracle.mjs` now restores and builds the whole produced solution
from a fresh source-only copy before compiling the frozen sibling HTTP harness.
It requires a reviewed source-tree identity, exact candidate packages, isolated
caches and the pinned toolchain. Logs, input inventories and frozen harness,
oracle, runner and contract sources accompany the independently collected TRX.
Agent-authored tests or self-assessments cannot satisfy these checks.

Document control `document-oracle-process-positive-3.4.0-4` builds all application
projects without warnings and passes seven independent HTTP cases. These include
the earlier five lifecycle checks, preservation of existing Post requirements and
the 2000-character input boundary. Metadata alone did not return a useful 400 for
oversized notes; the control now uses the public application payload-validator
contract. Removing only Post note validation yields exactly one expected failure
in `document-oracle-process-negative-3.4.0-2`, with six other cases still passing.

Catalog control `catalog-oracle-process-positive-3.4.0-3` builds the full solution
without warnings and passes six independent cases: both administrator identities,
CRUD/partial updates, required fields/length limits, duplicate and simultaneous
creates, denied actors and repeated Migrator execution with preserved records.
Removing database uniqueness produces the three expected failures in
`catalog-oracle-process-negative-3.4.0-1`. The first catalog probe was cancelled
after an in-process migrator invocation hung without a completed TRX. Running the
actual Migrator output as a separate bounded process resolves the fixture issue;
no platform defect is inferred from that diagnostic.

Earlier document prototype results also exposed reuse of stale incremental output
after restoring a source timestamp. The independent runner never reuses agent
build directories. Receipt: `development-oracle-controls-3.4.0-1.json`.
These are trusted positive/faulty controls, not real-agent evaluations.

Matrix profile v3 adds precise observable prompts for document, catalog and report
tasks; v2 is preserved under `history/`. Contract tests prevent prompt/acceptance
drift. The report fixture supplies category metadata/storage before inference, so
the report task does not silently depend on a previous agent succeeding at the
catalog task. Its independent HTTP/provider controls are in progress. Permission
and customized-upgrade oracles, real development attempts, the remaining public
contract review and final candidate verification are still outstanding.

Report control `report-oracle-process-positive-3.4.0-3` now passes all four cases,
including known category totals, cursor/filter integrity, real bounded provider
aggregation before sheet rendering and both administrator identities. Removing
the Posted predicate fails both the HTTP total and direct provider aggregate;
two other cases still pass (`report-oracle-process-negative-3.4.0-1`). The two
earlier probes exposed harness-only issues: a missing namespace and duplicate
test role codes. The positive control uses only public SDK contracts.

Permission control `permission-oracle-process-positive-3.4.0-2` passes five HTTP
and three independent Chromium cases. It verifies active application administrator
role codes, unlinked trusted Keycloak administrators, view-only API/UI behavior,
inactive/display-name-only refusal and independent feature flags. The first
browser process could not discover its generated MVC content-root manifest from
the web directory; the independent executable now starts from its own build output.
Separate faulty-role and faulty-UI controls are retained as required negative probes.

`prepare-development-fixture.mjs` materializes the package example with the actual
template's official/custom instruction paths, both adapters and shared CLI. It
records a trusted initial inventory outside the application, applies frozen task
overlays and provides a candidate-only NGB feed. Report and permission fixture
preparation and installed `ai status` pass. The example's conditional SDK version
was incompatible with the CLI's exact-version contract; canonical example inputs
now declare 3.4.0 unconditionally and the preparation pins the candidate version.
These package-only setup probes do not claim any model completed a development task.

Permission negative controls completed as expected. With incorrect UI capabilities,
all five HTTP cases and both administrator browser cases pass, while the view-only
reader case detects an enabled Save button. With the wrong registered role code,
the application administrator receives 403 and the other four HTTP cases pass.
Receipt: `artifacts/ai-devkit/permission-oracle-controls-3.4.0-2.json`.

The customized-upgrade fixture now starts from the verified frozen 3.3.0 external
application, adds a canonical custom skill and one application-owned file colliding
with the incoming official instructions. Its independent preparation confirms that
preview reports exactly that conflict without changing the application. The exact
task contract requires preserving the collided bytes under `ai/custom/`, retaining
root and unrelated skills, and reviewing a hash-bound incoming resolution.
The control upgrade passes the actual target CLI's backend/frontend/helper builds.
`upgrade-oracle-process-positive-3.4.0-1` independently passes exact preservation,
clean locked backend/frontend/helper builds and installed Kit status. Three separate
negative controls detect lost archived instructions, removed root policy and a
missing Claude adapter before executing any produced code. Their byte-bound receipt
is `artifacts/ai-devkit/upgrade-oracle-controls-3.4.0-1.json`.
The oracle deliberately does not infer the agent's own preview or lack of deployment
from produced files: those actions require separate transcript verification.

All five development task oracles now have positive and negative process controls.
Tooling passes 136 tests with complete per-file coverage for 21 measured modules.
No real development agent attempt has run. The combined candidate with General
Journal acceptance, current guidance and exact example version still requires
package verification, followed by actual Codex/Claude development evaluations.

Candidate `7fc2a2d1cb492fe7f0f884b5123e39edf6672c7ed1bbc391454135b5055fc3b8`
subsequently passed artifact/contracts verification and source post-verification.
Its package-only example passed 228 of 230 backend cases, all 15 frontend and 10
browser cases, and complete line/branch/method coverage for 66 executable files.
The only failed cases are the two unchanged historical lifecycle replay assertions;
no new SDK behavior or practical exploit claim follows from them. Receipt:
`artifacts/ai-devkit/progress-oracles-journal-3.4.0-1.json`. Later changes below are
not covered by that sealed candidate.

## Public security administration

Public-contract review added nine previously missing security administration
capabilities: provisioning, user updates/activation/membership/paging/effective
access, role management/permissions and protected administrator semantics. The
registry now has 136 capabilities and 623 of 1716 public contracts classified.
The remaining contract review is still required before claiming full coverage.

Five independent HTTP cases pass in the package-only security prototype using a
disposable Keycloak realm, its own randomly generated service-account secret and
the public security API. They verify both administrator identities, identity-provider
updates, retained audit, current effective access after role/membership changes,
cursor/filter binding, denied actors and protected administrator role codes.
Two initial fixture assumptions were corrected: the realm must allow username
updates, as the starter already does, and effective-access groups retain their
definitions when all actions are denied. No SDK code changed. These tests and
their fixture configuration are now included in the canonical example and await
the next combined package verification.

Catalog, report and permission source guards now accept normal C# subdirectories
within their required application projects. Contract tests cover those paths;
all instruction, migration and dependency preservation checks remain in force.
Earlier process-control receipts retain their original oracle identities.

The Codex development runner now prepares exact public dependencies, checks native
filesystem restrictions and builds the entire unchanged external solution before
inference. `codex-development-preflight-3.4.0-1` passes without invoking a model.
The probe verifies application/scratch writes, denied SDK-checkout/sibling reads,
read-only managed guidance and the pinned Node/.NET tools. Explicit home and temp
directory denials are necessary in addition to the client's minimal runtime paths.
This is an environment preflight, not a successful agent task. Client/profile,
prompt, configuration, before/after tree, transcript and usage are frozen per
attempt; a completed client process still awaits independent source review and
acceptance. The installed Codex version is now 0.166.0-alpha.5.1; the earlier
discovery profile remains under `history/`.

Candidate `13bd4a6e9bcaa3c0ab23cadbc7ce0694be0b61329b2c19c1d4c511fe7bf5ee60`
passed artifact/contracts checks and source post-verification. Its example passed
233 of 235 backend cases (only the two unchanged historical replay failures), all
15 frontend and 10 browser cases and complete coverage for 66 executable files.
All five security administration cases passed in the combined package run.

The first real Codex catalog attempt completed in 108 seconds and produced a
buildable public-SDK implementation. Six independent catalog cases and all 104
application unit cases passed. One of its two authored HTTP tests failed because
its fixture created distinct roles with the same unique code. The attempt also
exceeded its frozen total-token ceiling (700108 reported tokens, including 646528
cached input tokens). It remains failed; sources, transcript and both kinds of
test evidence are retained in `codex-new-catalog-assessment-3.4.0-1`.

That attempt exposed incomplete CLI configuration in the development materializer.
Preparation now includes the metadata helper in the solution, classifies resource-only
migration fixtures as tooling, generates locks and uses the existing manifest-bound
local-package layout while preserving the official registry config. A separate
prototype passes all eight inspection and 39 build checks. No agent result was
repaired or silently retried. Profile v2 declares separate cumulative and uncached
token ceilings before future attempts. Full real-agent acceptance remains pending.

Further source review classifies 89 platform migration types by their explicit
bootstrap ownership or supporting migration contract, bringing the reviewed total
to 712 of 1716 public contracts. It does not claim new application workflows.
Outstanding review includes Chart of Accounts administration, server menu
contributions, bounded cross-definition search and built-in accounting reports;
their existing application services must be exercised without modifying SDK behavior.

## Development evaluation execution and evidence

Claude's declared native sandbox now passes the standalone boundary/build preflight
from the application subdirectory. The runtime overrides `TMPDIR`; explicitly
pointing `CLAUDE_CODE_TMPDIR` at the isolated scratch directory fixes .NET temporary
file creation without opening other temporary data. This probe uses pinned sandbox
runtime 0.0.81 and does not claim binary equivalence with the installed Claude
client's own sandbox. No model task is counted by this environment check.

Development acceptance now requires review of every changed application test file,
execution of all unit tests and selected authored HTTP classes, in addition to the
independent task harness. A separate negative control reconstructed the first
catalog output with the corrected materializer: all six independent checks and 104
unit cases passed, but the known duplicate-role fixture failure correctly kept
acceptance at `not-ready`. This control does not repair or reclassify the original
failed agent attempt. A separate positive control exercises the successful path.

Saved evaluation verification now binds the actual client profile, client version,
runner, fixture preparer, independent acceptance runner and supporting verification
sources, as well as exact SDK/UI/CLI/template/Kit bytes. It retains configuration,
frozen profile/runner sources and the raw acceptance logs/test reports. The upgrade
client runners remain unimplemented and the complete 24-attempt matrix remains
pending; a missing implementation or incomplete matrix cannot satisfy the gate.

Candidate `47df981e03e313efeb9b924c4280f77a8c5cbe015ba42ba7e11b601b6e407be5`
passed artifact/contracts checks and source post-verification with 137 tooling
tests. Positive authored-test control passes six independent checks, 104 unit
cases and six HTTP cases; the negative control retains its intended failure.
The real Codex document attempt stopped at the subscription usage limit before
changing any source. The real Claude catalog process disappeared without a final
transcript or receipt; its observed source changes and interruption record are
retained separately and are not accepted. The cause of termination is unknown.

Both development runners now write bounded stdout/stderr journals during execution
and persist the started-attempt identity before inference. Process tests verify
partial output before completion, cancellation, deadline and evidence-collision
handling without invoking a model. Missing completion remains an interrupted
attempt, never an implicit success or a reason for an automatic retry.

Four package-only Chart of Accounts HTTP cases pass after the example's new admin
controller explicitly injects `PermissionAwareAdminService`. Its initial draft
injected the trusted `IAdminService`, which correctly exposed the missing access
boundary in that draft. No SDK change was necessary. Current canonical content
includes the corrected controller, tests, documented lifecycle/paging/access
contracts and capability mappings; this later content needs fresh combined package
verification and is not certified by the candidate above.

## Chart of Accounts candidate and server navigation/search

Candidate `4ebcbec37127f9ba5444014e62a98c3e140313de86b31551ef0a9d60e4f94b43`
passed artifacts/contracts checks and source post-verification. Its first full
example run was interrupted without a final receipt; no results are inferred from
that run. The separate replacement run passed 237 of 239 backend cases, including
all four Chart of Accounts cases, with only the two unchanged historical replay
assertions failing. All 15 frontend and 10 browser cases passed. All 66 executable
backend files have complete coverage: 868 lines, 142 branches and 136 methods.
Receipt: `artifacts/ai-devkit/progress-coa-administration-3.4.0-1.json`.

Independent menu acceptance passes four HTTP cases and one unit case. Search
acceptance passes seven HTTP cases and eleven public-pipeline contract cases.
The external application also passes eight CLI inspection checks and 39 build
checks. The prototype initially omitted its managed UI archive during copying;
restoring the exact hash-bound package fixed that setup error. These results are
separate prototypes, not a combined certification of the subsequent canonical edits.

Search acceptance clarified two SDK contracts before adoption: record searches
require both metadata `view` and record `lookup`, and standard MVC binding treats a
blank optional type-code filter as omitted. It also confirms that the built-in
General Journal Entry metadata lacks the `display` column required by generic
document lookup. The Kit records the unavailable-source result and the existing
typed journal read path without changing platform metadata, storage or services.

The canonical example now includes server menu contributions, an authenticated
search adapter, their tests and guidance. The registry maps 147 capabilities and
classifies 808 of 1716 public contracts. Supporting exception/data-model review
does not claim coverage of additional operations: Work Center retention, operational
register administration/physical health, dimension-resource net reads, built-in
financial report families and remaining MCP operations still require review and
distinct acceptance. Real-agent evaluations and final combined certification remain
unfinished. No SDK business or authorization source was changed for this increment.

## Financial statements, retention and real report evaluations

Candidate `26f0a0384b3d676c0b5b262a80ec30fc82de8b3285d1a991ed99636fe284159d`
passed artifacts/contracts checks and source post-verification. Its combined run
passed 260 of 262 backend cases, retaining only the two historical replay failures,
and all 15 frontend unit cases. All 68 executable files have complete coverage:
887 lines, 142 branches and 139 methods. Browser acceptance passed nine of ten
cases; the usage form's numeric input was empty after fill/Tab. The failed report
remains unchanged in `progress-menu-search-3.4.0-1.json`.

The same numeric workflow passed ten isolated repetitions. Repeating the entire
suite exposed duplicate lookup names in two other fixtures; those names now include
the repetition index. Numeric input events are retained in browser diagnostics.
Neither a passing repetition nor that distinct fixture correction establishes the
cause of the original intermittent input failure.

The real Codex report/root attempt completed within its frozen usage/time limits
and passed all four independent report cases and 103 unit tests. One of its three
authored HTTP tests failed: it assigned a new administrator role through raw test
repositories after the same reader's permissions had been resolved. That bypassed
the public access-version update contract. The attempt remains failed and unchanged
in `codex-report-root-assessment-3.4.0-1`; testing guidance now explains the fixture
boundary without repairing the agent's output or changing platform authorization.

The independent Claude report/root attempt completed in 423 seconds using the
client-reported `claude-opus-5-5` model. Its seven changed files were reviewed; all
four independent report cases, 104 unit cases and nine authored HTTP/provider/XLSX
cases passed. The single attempt was accepted without hints, source repairs or a
retry. Evidence is retained in `claude-report-root-assessment-3.4.0-1`. This is one
successful scenario, not the complete 24-attempt compatibility matrix.

Eight package-only prototype cases pass for four built-in financial statements
and Work Center retention. The statement fixture reconciles known public-journal
postings, group children, inactive account history, request bounds and both
administrator identities. Retention checks completed receipts, terminal tasks,
notification deliveries and outbox terminal states with a controlled clock and
the real provider. Its first draft omitted application persistence registration;
statement drafts also incorrectly assumed root pages include account details and
all empty statements contain rows. The corrected tests preserve actual SDK contracts.

These tests and guidance are now canonical. The registry maps 157 capabilities
and classifies 834 of 1716 public contracts, including 26 reviewed statement and
retention contracts. The prior candidate does not certify these later edits.
Tooling still passes 139 tests with complete coverage for 21 measured modules.
Remaining financial report families, operational-register maintenance/net reads,
MCP operations, upgrade agent runners and full final certification remain open.

The instrumented numeric failure reproduced once: the hours input received focus
and blur but no input event. The installed combobox schedules focus restoration
after option selection. Explicitly clicking and checking focus on the numeric
input before filling now passes 40 repetitions across usage and service workflows,
followed by all ten browser scenarios on a fresh database/realm. The receipt is
`browser-focus-proof-3.4.0-1.json`. The diagnostic ten-repeat full suite also retained
separate fixture failures from a stale literal assertion and shared Work Center
state across repetitions; those diagnostic counts are not release acceptance.
No SDK/UI runtime source was changed.

Four further package-only cases pass for operational-register health, targeted
schema ensure, finalization administration and transactional resource nets. The
default provider automatically projects a new register. An initial draft wrongly
expected it to block; the accepted case verifies default success and a distinct
host explicitly missing the default projector verifies BlockedNoProjector. No
platform source was changed. All four net-read methods reconcile separate equipment
amounts and storno without finalization. Evidence: `register-admin-proof-3.4.0-1.json`.

Canonical guidance and registry now include these workflows: 161 capabilities and
854 of 1716 public contracts classified. Their trusted service boundaries do not
claim HTTP authorization, load/query-count measurements or complete helper coverage.

## Ledger reports and permission evaluation

Candidate `73d383036df2c6a1813ebab9121dc3a9a31361e41dcb7f764af778f2311736c9`
passed artifacts/contracts checks and source post-verification. Its first example
run lost its controlling process and left no final receipt; its cause is unknown.
The orphaned test processes were stopped, and a separate replacement run completed.
It passed 272 of 274 backend cases with only the two unchanged historical replay
assertions failing, all 15 frontend cases and all ten browser cases. All 68
executable files retain 100% coverage: 887 lines, 142 branches and 139 methods.
Evidence: `progress-statements-retention-3.4.0-1.json`. The example runner now saves
a start receipt and per-process diagnostic records, reports each step and propagates
cancellation to child processes; a stored PID is not proof of current ownership.

The real Codex permission/root attempt completed in 119 seconds without hints or
source repairs. Independent acceptance passed five HTTP cases and three browser
cases; 102 unit cases, five authored integration cases, 15 frontend unit cases and
the modified authored browser case also passed. The initial external authored-browser
filter selected no tests; correcting that filter executed the unchanged source.
Both invocation records are retained. Evidence: `codex-permission-root-assessment-3.4.0-1`.

Seven package-only ledger HTTP cases pass for General Journal, Account Card,
General Ledger, Trial Balance, grouped/flat Ledger Analysis, Posting Log and
Accounting Consistency. Tests reconcile known public journal postings, cursor
continuation, grouping, required inputs and authorization. Earlier drafts confused
Trial Balance turnover with closing balance and grouped headers with separate
subtotals; failed draft receipts remain unchanged. No SDK report or authorization
implementation was changed. Evidence: `ledger-reports-proof-3.4.0-1.json`.

The ledger tests, instructions and reviewed public contracts are now canonical:
171 capabilities and 885 of 1716 contracts classified. The previous candidate does
not certify these edits. Built-in MCP operation checks, the remaining contract
review, upgrade agent runners, the full agent matrix and final certification remain
unfinished.

## Built-in MCP and document evaluation

Six package-only HTTP cases now cover all 18 built-in MCP names. Positive cases
verify authorized metadata, records, search/fetch, reports, flow/effects, notes,
audit, unread Work Center notifications and real Ready attachment metadata.
The separate journal-detail case records the existing missing-display-column
constraint: discovery and typed listing work, but generic detail metadata, record
read and compatibility fetch return sanitized internal errors. The dedicated
authorized journal HTTP API works. No SDK source was changed. Earlier failed
drafts remain in the evidence; `mcp-builtins-proof-3.4.0-1.json` records the final
six-case prototype. Canonical registry: 190 capabilities and 929 of 1716 contracts
classified. This is not a complete capability certification.

Claude's document/subdirectory attempt completed without human hints or source
repairs. All seven independent document cases and 114 unit cases passed; 20 of
21 selected authored integration cases passed. The agent's migration fixture
appends its replacement baseline definitions after a runtime catalog extension,
so startup fails before migration acceptance. The attempt is failed, preserved in
`claude-document-subdirectory-assessment-3.4.0-1/result.json`. Application and
persistence guidance now explain contributor ordering and baseline composition;
the failed agent output is unchanged. A later candidate must validate those edits.

The upgrade-agent preflight reproduced a CLI transport problem: isolated child
environments dropped the native sandbox's proxy variables, so an allowed registry
was unreachable. `executionEnvironment` now retains bounded, credential-free
standard proxy origins and bypass lists while continuing to exclude secrets and
registry overrides. The real restricted-network probe restores a public NuGet
package into an empty cache, queries npm and still rejects an undeclared domain.
Its .NET first-run probe requires the runtime's shared-memory/lock directories;
private TMPDIR alone is insufficient. Evidence: `upgrade-proxy-fixed-3.4.0-3`.
All 140 tooling tests pass, with complete coverage for 21 measured modules.
The full upgrade-agent runners and actual upgrade attempts remain unfinished.

## Native upgrade runners and reference-register maintenance

Candidate `94d53dd719c4da34e850d38dc9b2e76adde02ed20375e40bffd0a61a8372fa17`
passed artifacts/contracts checks and source post-verification. Its combined run
passed 284 of 287 backend cases, all 15 frontend cases and all ten browser cases.
The two historical replay assertions remain failed. A third case stopped during
initial PostgreSQL connection encryption negotiation before its checklist scenario;
Npgsql reported EndOfStream immediately after container readiness. Its cause is
not established and the failed run remains unchanged. All 68 executable backend
files retain complete coverage: 887 lines, 142 branches and 139 methods.
Evidence: `progress-ledger-mcp-proxy-3.4.0-1.json`.

Two reference-register administration prototype cases pass for independent and
subordinate nonperiodic modes. Public persistence ports model incomplete metadata-only
setup; the trusted runtime facade diagnoses missing tables and selectively ensures
append-only storage without creating records. The tests and guidance are now canonical.
The registry maps 191 capabilities and classifies 954 of 1716 public contracts.
This does not claim every periodic index/corruption variant or HTTP authorization.
Evidence: `reference-admin-proof-3.4.0-1.json`.

The upgrade evaluators now have bounded Codex and Claude process runners, frozen
client profiles, immutable supplied-CLI/candidate checks, partial output journals
and separate native environment preflights. Initial preflights found two transport
requirements: Claude's sandbox supplies authenticated loopback proxies, and native
macOS TLS needs the narrow trustd.agent lookup. The CLI now retains loopback proxy
authentication while redacting user/password values from diagnostics and rejecting
credentials on remote proxy hosts. Both native transport prototypes pass cold NuGet
restore, npm metadata, filesystem denial and rejected direct/undeclared egress.
Claude's standalone runtime probe does not establish installed-client equivalence.

A deterministic full upgrade control then failed during restore: MSBuild launched
additional nodes whose named-pipe bind was denied by the native sandbox. All 65
original application files were independently confirmed unchanged. Both application
and helper restores now use one MSBuild node with node reuse disabled, matching the
existing build policy. The failed control is retained in
`upgrade-native-control-3.4.0-1`; the change still needs a new sealed-candidate control.
Actual upgrade-agent attempts and the complete evaluation matrix remain unfinished.

All 143 tooling tests pass, including cancellation/journal checks for both new
upgrade runners; 21 measured modules retain complete per-file coverage. Both
checklist variants pass in a separate unchanged-source diagnostic run. That repeat
does not establish the original startup failure's cause or replace combined acceptance.
Evidence: `checklist-startup-diagnostic-3.4.0-1/result.json`.

Candidate `5ae295016e3891ca42c87323372212e3cb0c8e4948936e07b6d7d298a8729689`
passed artifacts/contracts checks, both upgrade environment preflights and source
post-verification. Its native full control restored and built successfully, then
failed during managed skill installation: Codex retained its automatic `.agents`
protection despite the parent application write grant. Transaction rollback preserved
all 65 original files. A separate native probe confirms that explicit skill-directory
write grants work while Git/client configuration remains protected. Codex upgrade
profile v2 now declares those narrow grants, and both upgrade preflights test a
real create/write/remove operation in the adapter directory before inference.
The original failed control remains in `upgrade-native-control-3.4.0-2`.

Candidate `6b60c83428bc6b8986e7800a81f97ea2e177066ecaa34dbc4eec4a1e35d4ee14`
passed artifacts/contracts checks and source post-verification. The deterministic
native upgrade control and its independent clean locked-build oracle both pass.
The real Codex upgrade/root attempt completed in 113 seconds; its exact tree matches
the accepted control. Independent preservation, package identity, backend/helper,
frontend and Kit-status checks pass without human hints or source repairs. Evidence:
`codex-upgrade-root-assessment-3.4.0-1/result.json`.

The real Claude upgrade/subdirectory attempt completed in 133 seconds but did not
upgrade: native Claude blocked `.claude/skills` during commit after staged builds
passed. Rollback preserved all original files; the required legacy copy is the only
source addition. Independent acceptance correctly fails that output. Evidence:
`claude-upgrade-subdirectory-assessment-3.4.0-1/result.json`. Official client docs
confirm that explicit allowWrite entries cannot override this protected path.
The standalone runtime preflight was insufficient to establish native commit
compatibility. That profile is still not release-ready; the failed result remains
unchanged and no broader filesystem permissions or SDK changes were introduced.

Three independent package-only Watchdog HTTP cases now pass. A live local web
server exercises healthy/unhealthy/recovered reports; the packaged stylesheet is
present. Separate cases cover normalized custom paths, missing assets/options and
real synthetic-cookie access through the unchanged ngb-admin policy. The tests use
local HTTP and do not certify external OIDC login, production TLS or deployment.
The initial restore selected the public-only feed and failed to find unpublished
3.4.0; the corrected run uses the supplied candidate feed and exact package version.
Evidence: `watchdog-proof-3.4.0-1.json`.

Watchdog tests, guidance and reviewed contract classifications are now canonical:
194 capabilities and 960 of 1716 public contracts classified. These additions and
the corrected Claude sandbox guidance need fresh combined candidate verification.
The remaining capability review, compatible Claude upgrade execution profile,
complete real-agent matrix and final certification remain open.

## Provider composition and agent-output redaction

Candidate `191c27798ed6dfd475d4381110f3b2ea36001537557ca024a2e11d4583a2b157`
passed artifacts/contracts and source post-verification. Its combined run passed
290 of 292 backend cases, retaining the two historical replay failures. All 68
executable backend files retain complete coverage (887 lines, 142 branches and
139 methods), and all 15 frontend unit cases pass. Nine of ten browser cases pass;
the equipment assessment case reached Post without a condition value and received
HTTP 400. That browser failure is being investigated; the failed run remains
unchanged. The previous PostgreSQL startup failure did not recur. Evidence:
`progress-watchdog-3.4.0-1.json`.

Nine package-only PostgreSQL composition cases pass without database I/O. They
verify startup option validation, scoped closed connections, transaction guards
and retention of an application-owned clock. Source review distinguishes default
provider implementations from application extension contracts; content persistence
records and safe exceptions are also mapped. The registry now has 195 capabilities
and 1078 of 1716 contracts classified. Three separate paged resource-net contracts
remain unmapped because the existing aggregate-net test does not exercise them.
Evidence: `provider-composition-proof-3.4.0-1.json` and the reviewed classification
draft. These additions still require fresh candidate verification.

Agent runners now sanitize complete UTF-8 records before writing journals, including
partial output on cancellation. A real saved Claude diagnostic exposed proxy
credentials both inside URLs and in CLOUDSDK_PROXY_USERNAME; both representations
are covered. The offline check preserves 171 event identities and their numeric
usage/cost fields while removing 24 credential-bearing URL occurrences and both
individual credential values. Original failed attempts are not rewritten. Every
new attempt binds the redactor source/hash, and certification rejects unsanitized
transcripts. Evidence: `agent-redaction-draft-3.4.0-1/real-transcript-diagnostic-v2.json`.
The redactor has complete line/branch/function coverage and all four subprocess
journal/cancellation probes pass. The full tooling gate passes all 145 tests with complete per-file coverage for
22 measured modules.


The unchanged assessment browser scenario passed five isolated repetitions, so
that diagnostic did not reproduce the combined-run failure. The installed combobox
schedules a focus restoration on the next animation frame, consistent with a race
when immediately filling another field. The example now waits for the option to
close, explicitly clicks/focuses condition, commits through Tab and verifies the
saved response value before Post. All 20 targeted repetitions pass; no sleeps,
backend seed shortcut, retries or relaxed effect assertions were added. The exact
original event sequence was not captured, so its cause is not claimed proven.
Evidence: `assessment-browser-validation-3.4.0-1/result.json`. The original failed
combined report remains unchanged; later full certification must test the updated
example together with the remaining workflows.

The first provider/redaction candidate passed artifacts/contracts and source
post-verification. Final review identified a second output path in Codex runners:
`--output-last-message` wrote directly to disk outside journal redaction. Those
runners now extract the final answer from the sanitized JSONL stream. A retained
native upgrade transcript reproduces its original answer exactly apart from the
terminal newline; the independent result is not rerun or changed. The new output
test and all four process tests pass with complete redactor coverage. Evidence:
`codex-answer-redaction-3.4.0-1.json`. This final refinement needs a new source seal.
Saved runner environment configuration now passes through the same sanitizer before
its evidence hash is calculated, so an inherited authenticated loopback proxy also
remains private in that artifact. Runtime transport retains the actual credentials.

The dimension-history example now exercises all nine public movement-query reader
methods against disposable PostgreSQL. Three cases pass: signed month nets,
cumulative offset/cursor balances before and after finalization, exact-date history,
equal-time continuation, storno, dimension filters and maximum-month reads. A null
occurrence cursor exposes SQLSTATE 42P08 in the existing provider; the Kit records
that limitation and verifies bounded offset startup followed by non-null cursor
continuation. No provider SQL or business behavior changed. Evidence:
`dimension-paging-proof-3.4.0-1.json`; the first failed history attempt is retained.

The readable capability registry crossed the former 1 MiB file boundary. Individual
Kit assets now allow 2 MiB while total payload, snapshot and general file-read
ceilings remain 16, 32 and 256 MiB. UTF-8 byte-boundary rejection and complete
tooling coverage pass. The sealed `candidate-dimension-paging-3.4.0-1` passes
artifacts/contracts and source post-verification. Its new external starter passes
all 48 certification checks, including real browser tests and three load repetitions.

That candidate's complete example run passes 302 of 304 backend cases, retaining
only the two historical action-replay assertions. All 68 executable backend files
retain 100% lines/branches/methods; 15 frontend unit and all 10 browser cases pass,
with complete per-file frontend coverage. The strengthened equipment assessment
scenario therefore passes in the combined run as well as its earlier targeted
repetitions. This remains a not-ready release result. Evidence:
`examples-dimension-paging-3.4.0-1/examples.json` and
`starter-dimension-paging-3.4.0-1/starter.json`.

After that source post-verification, the Kit adopts 109 reviewed standard runtime
implementations and 38 supporting financial-report ports/data contracts. The raw
Account Card readers remain deferred because they differ from the exercised
canonical effective stream. Two separately verified error-boundary cases add
runtime policy guidance and 12 actual Production HTTP response scenarios. They
preserve cancellation/provider exception identity and verify stable codes,
field validation, tracing and safe provider mapping; arbitrary application Context
is explicitly not described as sanitized. Evidence: `error-boundary-proof-3.4.0-1.json`.
These two cases are not part of the preceding combined candidate run. The expanded
registry has 201 capabilities and 1251 of 1716 public contracts classified; a fresh
package/source seal is required for these additions. Remaining capability work,
the compatible Claude upgrade profile, the real-agent matrix and final certification
are still incomplete.

The expanded error-contract candidate passes package parity, public contracts,
146 tooling tests and source post-verification. Its receipt distinguishes the
earlier combined example/starter candidate from the two targeted error tests:
`progress-dimension-errors-3.4.0-1.json`.

Claude upgrade profile v2 now supplies one immutable no-argument installer under
a separate restricted OS sandbox; normal Bash retains the native sandbox. This
allows the explicitly authorized skill-adapter installation while keeping client
settings, Git, target packages, target CLI and unrelated files protected. Preflight
checks both adapters and restricted NuGet/npm transport. A deterministic control
applies the upgrade and passes independent exact-tree/clean-build acceptance:
`claude-bounded-upgrade-control-acceptance-3.4.0-1/acceptance.json`. The failed
environment probes remain retained; the corrected launcher explicitly keeps both
temporary-directory settings inside its private scratch. This control is not a
Claude evaluation. A fresh real-client attempt and complete matrix remain pending.

The launcher rejects arguments, quotes literal paths and clears inherited runtime
options/credentials before starting Node. Its two executable tests pass with full
coverage; the complete tooling suite passes 148 tests across 23 measured modules.
The profile pins the generator, the saved configuration retains its source and OS
policy, and verification checks their identities. All six evaluation runners also
sanitize preparation logs, including failed sandbox diagnostics, before saving.

The bounded Claude v2 subdirectory upgrade now passes a real one-attempt evaluation
and independent acceptance on `candidate-bounded-installer-3.4.0-1`. It completed
in 173.26 seconds, preserved application/custom content and installed both adapters;
clean locked backend/helper/frontend builds and Kit integrity pass. The client
overrode ambient TMPDIR; the agent independently selected its allowed scratch for
ordinary commands. No human hint or source repair was supplied. This is one matrix
cell, not the complete matrix. Evidence:
`claude-upgrade-subdirectory-bounded-assessment-3.4.0-1/result.json`.

Maintenance acceptance found three omissions in the independent example's schema:
missing typed-document immutability triggers, varchar columns rejected by the
provider's canonical text mapping, and a checklist part without its direct registry
foreign key. Three new application migrations install the existing SDK guards,
retain length bounds as CHECK constraints after text conversion, and add that
reference while preserving the existing head cascade. Prior migrations and SDK
sources are unchanged. Fifteen targeted cases pass, including seven maintenance
jobs executed twice, upgrade/history preservation, catalog/checklist regressions
and direct Posted storage deletion rollback followed by normal Unpost/editing.
Evidence: `maintenance-jobs-proof-3.4.0-1.json`. The registry now records 209
capabilities and 1278 of 1716 public contracts; complete combined verification of
these new application migrations is still required.


## Maintenance regression and report boundaries

The maintenance candidate passes package/contracts checks and source post-verification.
Its complete example run passes 307 of 309 backend cases, retaining only the two
historical replay assertions. All 68 executable backend files retain 100%
lines/branches/methods; all 15 frontend unit cases pass. Nine of ten browser cases
pass; the withdrawal case reaches Post with an empty inspection result and receives
HTTP 400. The failed candidate evidence remains unchanged. A newly generated
starter passes all 48 certification checks.
The first starter run was stopped before build/test progress in the restricted
execution environment; its cancelled receipt is retained. The separate run with
the required dependency/container access passes. Evidence:
`progress-maintenance-3.4.0-1.json`.

The browser scenario now explicitly focuses the result field after lookup closure,
commits its value through Tab and checks the saved response before Post. The full
ten-case browser suite and five independent fresh-host repetitions pass. An initial
repeat-each diagnostic reused the same database and failed because the prior run's
required review remained in the feed; that interrupted diagnostic is retained.
The isolated repetitions preserve the empty-feed assertion and create a fresh
database/realm each time. No SDK behavior, sleeps or test retries were introduced.
Evidence: `withdrawal-browser-validation-3.4.0-2/result.json`.

Four additional package-only reporting cases pass against 31 exact candidate SDK
archives. They verify independent page/download capacity and HTTP 429/recovery,
queued cancellation, API signing-key binding and same-key continuation integrity,
ordered definition enrichment, unchanged access and report identity preservation.
They do not establish distributed quotas, deployed replica topology, execution
timeout expiry or workload throughput. Evidence:
`reporting-boundaries-proof-3.4.0-1.json`.

The Kit adopts those cases, explicit runtime/provider boundary guidance and 54
reviewed report contracts: 212 capabilities and 1332 of 1716 contracts classified.
Source review also corrects five older metadata/page DTO descriptions: their public
declarations do not establish current HTTP use, and the standard definition catalog
consumes IReportDefinitionEnricher rather than IReportMetadataContributor. These
additions follow the combined maintenance run and require a new package/source seal.
Remaining contract review, complete real-agent evaluations and final release
certification are still unfinished. SDK authorization/business code is unchanged.


## Register-specific projections and trusted storage data

The reporting-boundaries candidate passes artifact/contracts checks and source
post-verification. Its full example run passes 310 of 313 backend cases, all 15
frontend unit and all 10 browser cases. All 68 executable backend files retain
100% lines/branches/methods; all nine frontend files retain 100% required metrics.
The backend failures are the two retained SDK replay assertions and one initial
migration interrupted by PostgreSQL SQLSTATE 57P01 (unexpected postmaster exit).
Both administrator variants of that chart scenario pass a separate unchanged-build
run with fresh resources. The original failed combined result is retained rather
than replaced by that diagnostic. Evidence: `progress-reporting-boundaries-3.4.0-1.json`.

A real Claude new-catalog/subdirectory attempt completed without human assistance
or source repair. Its exact produced tree passes six frozen independent acceptance
cases, all 116 unit cases and seven agent-authored HTTP cases. It verifies both
administrator identities, unauthorized denial, field validation, exact code
uniqueness under concurrency, partial updates and migration preservation. The
native sandbox blocked the agent's attempted broad pkill cleanup; the transcript
and source-style observations remain recorded. This is one passing cell on the
reporting-boundaries candidate, not a complete or newer-candidate evaluation matrix.
Evidence: `claude-catalog-subdirectory-reporting-assessment-3.4.0-1/result.json`.

Three package-only custom operational-register projector cases pass against 31
exact candidate SDK archives. They verify code normalization/registration,
register-specific selection with default fallback for another register, an active
owned month transaction, rollback after writes, recovery and storno reconciliation.
The test delegates aggregation to the existing default projector; it does not
claim a new aggregation algorithm, external-transaction rollback or throughput.
Evidence: `custom-projector-proof-3.4.0-1.json`.

The Kit adopts that acceptance and guidance, six reviewed projection contracts and
50 reviewed persistence/accounting data declarations. The registry now records
213 capabilities and 1388 of 1716 public contracts. The data descriptions explicitly
preserve trusted-storage versus authorized-service boundaries; these declarations
do not create new executable workflow claims. Generated payload/packages and a
new combined run are required for these additions. Remaining public-contract
review, complete agent evaluations and release certification remain unfinished.
SDK business and authorization sources are unchanged.

## Report dimension scopes and runtime/provider contracts

The custom-projection candidate passes artifact/contracts checks and final source
verification. Its combined example run is incomplete: the integration process
timed out without a final TRX, while browser installation encountered ECONNRESET
and timed out before frontend tests. All 102 unit cases pass; unit-only coverage
does not establish full example coverage. The original partial logs are preserved
in `progress-custom-projection-3.4.0-1.json`; no complete backend count or frontend
success is inferred from that run.

Three package-only report dimension scope cases pass without compiler warnings
against 31 exact SDK archives. They verify ordered application contributors,
explicit report metadata opt-in, authorization before expansion, unchanged
unrelated selections, cancellation, the 5000-value limit and bounded recovery.
The example supplies explicit synthetic relationships; it does not introduce or
certify a general hierarchy provider. Evidence: `dimension-scope-proof-3.4.0-1.json`.

The Kit adopts that example and four related contracts, plus 88 reviewed runtime
and persistence declarations. Guidance distinguishes trusted storage operations
from authorized service workflows and unused public DTOs from implemented APIs.
These source-backed classifications do not claim an alternative provider or
coverage of every optional storage method. The registry now records 214
capabilities and 1480 of 1716 public contracts. A new package seal and combined
verification remain necessary, alongside the remaining contract review, complete
real-agent matrix and release certification. SDK business and authorization code
is unchanged.

## Trusted batches, composed posting validation and implementation boundaries

The dimension-scope candidate passes artifact/contracts checks and final source
verification. Its complete example run passes 317 of 319 backend cases: 215 of
217 integration cases and all 102 unit cases. The only failures are the two
retained SDK replay assertions. All 15 frontend unit and 10 browser cases pass.
All 68 executable backend files retain 100% lines/branches/methods; all nine
frontend files retain 100% required metrics. Evidence:
`progress-dimension-scope-3.4.0-1.json`. This result does not establish a complete
Kit release or authorize changes to SDK behavior.

Four additional package-only cases pass without compiler warnings against the
same 31 SDK archives. Two verify an explicitly composed accounting validator,
both administrator identities, refusal before effects and successful correction.
Two verify trusted draft/post batches, rollback including number reservations,
custom number formatting, duplicate handling, size limits and cancellation.
These are small isolated acceptance cases, not a public batch API, throughput
proof or every possible combination of effects. Evidence:
`posting-validator-proof-3.4.0-1.json` and `document-batch-proof-3.4.0-1.json`.

The Kit adopts those examples and source-reviewed descriptions of diagnostics,
host identity, runtime/provider boundaries and built-in report implementations.
The registry records 218 capabilities and 1708 of 1716 public contracts. The
remaining contracts require specific catalog hierarchy/parts, relationship batch,
accounting repair and report/filter extension acceptance. Describing supporting
storage and implementation types does not claim a new provider or replace those
workflow checks. New payload/packages, the complete agent matrix, performance,
upgrade and final release certification remain unfinished. SDK business and
authorization sources are unchanged.

## Completed public-contract classification and remaining workflow acceptance

Nine further package-only cases pass without compiler warnings against all 31
exact SDK archives from the dimension-scope candidate. They exercise catalog
parts and trusted hierarchy query helpers, relationship batches, explicit derived
accounting recovery, provider document filters and a custom streaming executor.
Sources, resolved packages, final TRX files and logs are bound in
`remaining-workflows-proof-3.4.0-1.json`. Initial compile diagnostics and the first
catalog run with a Dapper analyzer warning remain preserved; the final catalog
fixture uses Npgsql directly and has no warning.

The registry now classifies all 1716 public contracts and describes 224
capabilities. This closes the inventory review, not all release acceptance.
The examples explicitly distinguish trusted helpers from authorized endpoints,
test setup from application migrations, and sequential recovery from concurrent
close/rebuild or parent-write guarantees. They add no SDK business capability.

The expanded integration suite needs a new combined package-only run. Its bounded
deadline is now 30 minutes: the prior 217-case run took 19m37s before these 13
additional cases. No case, assertion, coverage threshold or failed result is
removed. Starter, upgrade, real-agent matrix, performance and final platform
release checks remain required. SDK business/authorization sources are unchanged.

The first contract-complete candidate passes artifact parity but exposes one
obsolete tooling-test expectation: a synthetic fixture containing all declared
acceptance cases was still required to return not-ready. The test now requires
the complete synthetic result to pass and separately removes one acceptance
result to prove it remains not-ready. All 148 tooling cases pass with complete
coverage of the 23 measured modules. No production acceptance result or gate is
changed. The first candidate's example/starter runs were explicitly cancelled
before completion and retained; source post-verification passes. A new source
seal is required for the corrected test.

## Complete-contract candidate acceptance and certification integration

`candidate-contract-complete-3.4.0-2` completed its package-only example run:
228/230 integration cases and 102/102 unit cases passed, with no skips. The only
failed assertions are the two previously recorded SDK action-replay observations;
the user's instruction against changing that SDK behavior remains in force. All
13 newly added workflow cases passed. Backend coverage includes all 68 executable
source files: 887/887 lines, 142/142 branches and 139/139 methods. The two existing
declaration-only exclusions remain explicit. Frontend acceptance passed 15 unit
and 10 browser cases, with all required metrics at 100% across nine source files.
The completed results and successful source/package post-verification are bound
in `artifacts/ai-devkit/progress-contract-complete-3.4.0-2.json`.

The clean starter passed its full external-application profile, including three
load repetitions. Persisted core, notes and attachments upgrades from 3.3.0 all
passed, including continued writes, migration history, queued/idempotent work,
browser checks and failed-migration startup refusal. The coordinated CLI/Kit
upgrade also preserved root instructions and custom skills/adapters. These are
candidate-specific results, not authorization to publish or proof of full release
readiness.

The broader Linux certification exposed integration gaps in its own test setup.
The external tooling suite now includes the existing Kit argument-contract tests.
The historical release-process fixture copies the real certification module and
its CLI dependencies into its isolated tree and template archive. The workspace
pins Playwright and its runtime to the browser image's existing 1.59.1 contract;
the environment check now verifies every locked runtime, rather than just the
`@playwright/test` entry. All other locked dependencies remain unchanged. A
lookup-formatting browser case now explicitly requests preview-only mode, so its
unawaited autoprint cannot increment the next test's print spy. Dedicated print
assertions and production UI code are unchanged.

These six changes passed together in the isolated Linux workspace: measured
tooling/generated-helper coverage is 100%, all 2319 browser tests passed across
426 files in Chromium/Firefox/WebKit, and offline performance-tooling/metric
contracts passed. Earlier failures remain recorded. Review, exact source hashes
and preflight reports are under
`artifacts/ai-devkit/external-tooling-commands-draft-3.4.0-1/`. A new source seal
and official candidate-bound run are required after adoption.

Real evaluations remain incomplete. Claude's root document change passed seven
independent HTTP cases plus 112 unit and 29 reviewed integration cases. Codex's
root document, subdirectory catalog and root report implementations passed their
independent HTTP oracles, but their authored integration tests contained errors
and the attempts remain failed. Neither the produced files nor those outcomes
were repaired after inference. Codex discovery answered correctly but exceeded
its frozen total-token ceiling; Claude discovery passed both directories. The
remaining matrix cells and full platform regression/certification are still
required. The evaluation verifier permits reuse only when every declared relevant
input matches; unrelated certification-test source edits alone do not authorize
discarding failed attempts or relabeling candidate-bound deterministic evidence.

## Completed v3 evaluations and reviewed v4 corrections

The complete 24-cell Codex/Claude matrix for the contract-complete package set
finished with 14 passed and 10 failed attempts. The candidate-2/candidate-3 input
equivalence and each original attempt, source tree and independent result remain
recorded in `artifacts/ai-devkit/progress-evaluations-contract-complete-3.4.0-3.json`.
All four permission implementations passed independent HTTP/browser acceptance
and their own 15 frontend unit and 11 E2E tests. All four upgrade outputs passed
clean independent builds and preservation checks. Claude's subdirectory upgrade
nevertheless remains failed because its agent process exceeded 30 minutes.
Correct output does not cancel the frozen execution budget.

The official candidate-3 external certification-quality run passed, including
2319 browser cases across Chromium, Firefox and WebKit. Its separate full-platform
backend run covered all 1540 required executable files at 100% for lines, branches
and methods; 35 of 36 test projects passed. The remaining project had 188 passing
cases and 44 failed skip assertions because native Ollama was not enabled in that
Linux run. The subsequently completed full frontend run passed all ten Vitest and
five Playwright projects, with all required metrics at 100% across 343 executable
files and all 854 public exports verified. The initial frontend invocation without
the Docker socket remains preserved. Backend and frontend receipts are under
`backend-full-reviewed-contract-complete-3.4.0-3/` and
`frontend-full-verified-contract-complete-3.4.0-3/`. These separate results do not
turn the stopped full-quality run into passed certification. After the user
started the local provider, a separate 44-case native run was recorded under
`native-ollama-contract-complete-3.4.0-3/`; its final TRX and model identity must be
read independently of the earlier unavailable-provider result.

Agent-authored tests exposed a defect in the reference application's test helper:
linking a second user to the same role attempted to create another role identity
with an already registered code. `InspectionHost.LinkUserAsync` now looks up the
registered code before creating a role. It preserves the existing role's name and
flags and independently applies the requested user state. A new real HTTP
regression verifies shared membership, repeated linking and inactive-account
refusal. The original helper failed that regression; the corrected helper passed
it and all five existing authorization cases. This changes test setup only, not
SDK authorization. The catalog reference also explains the public service's
conversion of JSON values to declared column types before application validation;
a string validator cannot infer the original JSON token kind.

Protocol `terminal-macos-arm64-v4` corrects a contradictory document-task boundary:
TypeScript test sources under `web/tests/unit` and `web/tests/e2e` may be updated
when a new required Post field changes existing browser workflows. Frontend
application source, dependency/build configuration and installed guidance remain
protected. Both development evaluators prepare frontend dependencies for document
and permission tasks. Independent acceptance now requires reviewed frontend test
files and runs the complete authored unit/coverage and E2E suites, in addition to
the frozen HTTP and permission-browser oracles. It verifies complete source and
test-file discovery and retains raw reports on failures. The release verifier
binds the new support module and requires those frontend artifacts.

Diagnostic replay validates the new runner without reclassifying earlier agent
attempts: the document control passed seven independent HTTP cases, 114 unit and
28 selected integration cases, 15 frontend unit and ten E2E cases; the permission
control passed its independent HTTP/browser checks, 102 unit and eight integration
cases, 15 frontend unit and 11 E2E cases. Both measured all nine frontend sources
at 100%. Before/after hashes, review and promotion receipts are retained under
`fixture-reliability-draft-3.4.0-3/` and `frontend-evaluation-draft-3.4.0-4/`.

After regeneration, the payload contains 308 files; capability and public-contract
classification remain 224 and 1716/1716. All 152 tooling tests pass with complete
per-file coverage of 24 measured modules. New CLI/template bytes and protocol v4
require fresh candidate-bound acceptance; old successful evaluations are not v4
evidence. Release readiness remains unproved. The previously recorded SDK replay
assertions and the user's restriction against changing that SDK behavior remain
unchanged; no SDK business or authorization sources were modified.

## Completion audit and evidence-format corrections

Candidate `candidate-completion-3.4.0-5` completed package parity, all 152 tooling
tests, the full starter including its three load repetitions, all three persisted
upgrades and the coordinated Kit upgrade. Cancellation and owned-resource recovery
also passed. The developer guide, candidate release notes and verification runbook
were completed; the documentation site builds successfully.

Ten fresh Claude development/upgrade attempts completed within their frozen
budgets. Independent acceptance passed both catalog, both permission, both upgrade
results and the subdirectory report. The root report failed during migrator
startup, before its filter assertions ran; see the corrected diagnosis below.
The subdirectory document's authored migration setup failed to
register the catalog before extending it. These agent outputs are preserved and
are not repaired or retried. The root document's seven independent HTTP cases and
117 authored unit cases passed, but its first acceptance run rejected legitimate
parameterized TRX results that share a test definition ID.

The TRX verifier now identifies executions by `executionId`, retaining unique case
names, complete counters and the requirement that every case pass. Negative tests
still reject repeated executions, missing identities, failed/skipped cases and
contradictory counters. The original 117-case TRX independently reproduces the
correction. Both discovery runners also preserve Buffer source artifacts as bytes
instead of JSON-encoding them. Earlier discovery artifacts retain their original
format and cannot be silently substituted for corrected evidence. These changes
affect verification tooling only; package payloads and SDK sources are unchanged.

The candidate-5 complex integration run reached its time bound under concurrent
acceptance load and did not produce a complete integration TRX; its 102 unit tests
and frontend checks passed. The two existing SDK replay assertions were observed
before termination and remain outside the authorized change scope. Two complete
cross-browser runs each passed 2318/2319 cases, with different existing UI failures
(download completion and audit drawer closure). Both failures and a separate
browser-session connection diagnostic remain recorded. Offline performance checks
passed separately: 34 Node tests, 18 Python tests and 202 metric-grouping requests.
No full-platform or release-ready result is claimed. Corrected verification and
the reduced-concurrency completion run use a new source seal and retain the earlier
candidate's failures. No new Codex inference is required for these checks.

## Final completion follow-up

The candidate-6 completion summary misclassified the root report evaluation's
`Expected: 0, Actual: 1` assertion. Its stack points to `InspectionHost.StartAsync`:
these were the migrator's exit codes, before the filter assertions ran. Independent
acceptance of the unchanged, previously reviewed agent output was repeated in a
fresh workspace and passed. The original failed run remains preserved under
`claude-report-root-oracle-completion-3.4.0-5`; the successful recheck is under
`claude-report-root-oracle-completion-3.4.0-7`. This corrects the diagnosis; it does
not manufacture a successful original run or retry inference.

The migration preservation fixture previously seeded the old schema through the
current application's registrations. New fields therefore required editing the
historical setup and could break contributor order before migrations were tested.
`MigrationBaseline` now owns the frozen metadata and storage registrations for
that schema. Preservation checks compare every old field and document property;
new fields require their own migration-value assertions. Current definitions and
existing SQL migrations remain unchanged. The corrected fixture passed all three
migration cases both unchanged and with an added current metadata/SQL column in
an isolated diagnostic application. No SDK sources were modified.

The installed Codex client and ChatGPT login were verified, and a bounded
transport-only request succeeded. This establishes current access, not agent
conformance. Updated Kit package bytes require fresh bound evaluations and a new
candidate seal; earlier certificates retain their original identities.

Candidate 7 completed all 231 complex integration cases: 229 passed and the two
previously recorded SDK action-replay authorization assertions failed. All 102
unit cases passed. The previous migrator and MinIO startup failures did not recur.
The Codex root document result passed the complete independent acceptance profile,
including authored tests and the frontend/browser suites. Other matrix attempts
retain their actual outcomes: Codex encountered an account limit, Claude's
subdirectory document attempt timed out, and subsequent fixture preparation
encountered NuGet connection resets. None of these failures is recorded as a pass.

Discovery diagnostics isolated two environment/guidance defects: fresh non-Git
applications need a native Codex root marker when invoked from a subdirectory,
and the read-only sandbox must permit Node's system OpenSSL configuration. Root
instructions now explicitly anchor paths and supply a focused installed-version
query. A diagnostic template passed both directories with unchanged 300-second
and 100,000-token limits (78,724 and 62,343 reported input/output tokens). Earlier
unsuccessful profile experiments remain preserved and are not release evidence.
The canonical revision uses the original low reasoning effort and default tool
output limit. Development profiles also describe direct offline unit commands and
the provisioning profiles that cannot run in the restricted editing environment.
These changed instructions/profiles require fresh evaluations; they do not alter
SDK behavior or relax independent acceptance.

## Native Claude compilation and TRX character references

The candidate-8 Claude root document result failed independent compilation. Its
new integration test referenced `DocumentStatus` without importing its namespace.
The native client fell back to a shared temporary directory when the configured
path was too long, which also prevented the agent from completing its final build.
A native diagnostic rebuilt all 12 projects with a short private directory. The
revised development runner creates such a directory per invocation and removes it
on completion. It keeps socket binding disabled and describes the independent
execution of VSTest and Vitest accurately.

The candidate-8 subdirectory catalog produced a passing 22-case integration TRX.
Its whitespace parameter was serialized as `&#x9;`. The Kit XML reader rejected
that numeric reference during evidence collection. The reader now decodes valid
XML 1.0 decimal and hexadecimal references once and rejects malformed references,
invalid Unicode values and undeclared entities. Regression tests cover escaped
case names, duplicate decoded names and encoded project paths. All 22 original
cases were parsed from the preserved TRX. The original failed acceptance receipt
remains available with candidate 8.

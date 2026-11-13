# AI Development Kit verification

The specification is [SPECIFICATION.md](../../ai-devkit/SPECIFICATION.md).
Application users start with the [developer guide](../../docs/guides/ai-development-kit.md)
and [application verification guide](../../docs/guides/ai-development-kit-verification.md).
Historical investigation and implementation evidence is in [implementation.md](implementation.md);
an earlier successful run does not qualify a changed candidate.

The [11 November reporting refresh](findings/reporting-refresh-2026-11-11.md)
records the SDK optimizations, package synchronization, current verification
receipts and the owner's disposition of the September 320-VU performance finding.
Canonical/template parity alone does not establish that the SDK archives contain
the current implementation.

## Inputs and execution

Use Node 24.19.0 and .NET SDK 10.0.401, a local Docker engine and the pinned tools
from [toolchain.json](../external-consumer/toolchain.json). Generate and review
template assets before packing. Seal a new candidate after any source change,
including documentation. Keep source and package bytes fixed during acceptance.
The [release runbook](../external-consumer/README.md) describes package preparation,
source identity, checkpoints and automatic certification gates. The maintainer
runs platform/application Backend/Frontend tests and full coverage independently;
FULL Certification neither launches them nor requires their reports. The Kit's
own external-consumer acceptance and tooling checks remain mandatory.

Set `ngb_candidate` to an absolute sealed candidate directory. Each direct runner
needs a new output directory; preserve earlier failures rather than overwriting
them. These commands do not publish packages or modify deployment resources.

```sh
node quality/external-consumer/release.mjs verify "$ngb_candidate"
node quality/ai-devkit/certify.mjs artifacts "$ngb_candidate"
node quality/ai-devkit/certify.mjs contracts "$ngb_candidate"
node quality/ai-devkit/certify.mjs application "$ngb_candidate"
```

The artifacts profile compares canonical, template and npm CLI contents, verifies
the UI archive against current shipped sources and checks the starter/example UI
lock integrity against the actual candidate. `packaging/templates/prepare-locks.mjs`
regenerates the starter, CRM package consumer and canonical example locks together.
The profile also extracts actual public package contracts. The contracts profile runs deterministic
tooling, architecture, malformed-input, ownership, recovery and process tests.
The application profile creates a fresh starter, executes its complete suite/load
profile, then builds and tests the independent complex example. Capability binding
requires successful example evidence; a failed example cannot produce a green
capability report. If examples fail, capabilities is recorded as `not-run` with a
failed prerequisite instead of launching another command against incomplete evidence.

Each independent example test project has a 45-minute deadline, matching authored
integration acceptance. The enclosing examples command retains its one-hour limit.
Every child command saves its configured deadline and PID in `processes/<step>.json`,
its redacted output in `<step>.log`, and its exit status, elapsed time and stop reason
in `<step>.result.json`. These receipts diagnose execution; they cannot replace the
required complete TRX and coverage reports. Test output includes individual case
results and authentication warnings from the isolated test host. Failures are not
retried or reclassified as successes.

The [12 November interruption analysis](findings/example-certification-2026-11-12.md)
records the earlier timeout, the unreproduced HTTP 401 and the targeted checks of
this execution and diagnostic change.

## Reliability regression coverage

The independent `equipment-inspection` application consumes exact candidate NuGet
packages. Its tests complement the platform suites; they do not import platform
test fixtures or reference projects from the NGB checkout.

| Contract | Package-consumer evidence |
| --- | --- |
| Amount 1000 → 900 → 1400, with an earlier date | `EquipmentServiceHttpTests`: HTTP Post/Unpost/replay, unchanged prior rows, linear OR history, monthly projections, trial balance and nonperiodic RR state |
| Posting before a later closed month | `AccountingPeriodHttpTests`: refusal without effects, explicit reopen, retry and correct subsequent balance |
| Existing periodic subordinate RR tables | `ReferenceRegisterUpgradeTests`: real versioned migration prefix, ordinary application Migrator twice, preserved legacy values/deletions, hidden internal column, earlier replacement and historical reads |
| Job longer than the Hangfire storage lease | `BackgroundJobsTests`: two worker compositions sharing PostgreSQL, overlap refused before/after expiry, successful acquisition after completion |
| Draft action version versus save concurrency | `DraftSaveBoundaryTests`: stale action refused after save; stale Draft/catalog saves still accepted by the existing contract |
| Generic payload date versus registry date | `DraftSaveBoundaryTests`: payload changes leave the registry date unchanged until the existing trusted header service is called |

The RR fixture starts from embedded SQL up to `2026_11_08_0100`, without altering
applied checksums or enabling out-of-order migration. This proves the new migration
against that persisted layout; it does not replace the separate frozen 3.3.0
application upgrade profiles. Retain those fixtures unchanged.

Capability acceptance entries bind these cases to their executable names. The two
generic-save/date limitations are deliberately passing characterizations of missing
guarantees, not evidence that those guarantees were implemented. Generated contract
snapshots, registry classifications and guidance must describe the same package
bytes. Regenerate the template and CLI after editing canonical Kit content, then
seal a fresh candidate; earlier certification and real-agent reports remain stale.

## Persisted upgrades

Run these profiles sequentially: they use shared loopback ports 5180–5185.

```sh
node quality/external-consumer/certify.mjs core-upgrade "$ngb_candidate"
node quality/external-consumer/certify.mjs notes-upgrade "$ngb_candidate"
node quality/external-consumer/certify.mjs attachments-upgrade "$ngb_candidate"
node quality/ai-devkit/certify.mjs upgrade "$ngb_candidate"
```

The final profile requires all three candidate-bound persisted reports. It checks
the frozen 3.3.0 source, coordinated versions, root/custom instruction preservation,
both custom-skill adapters, conflict/failure recovery and continued application
operation. A successful file upgrade alone does not prove database preservation.

## Real agents and independent acceptance

Read [evaluation-fixtures/README.md](evaluation-fixtures/README.md) before invoking
the frozen client runners. [agent-evaluations.json](agent-evaluations.json) defines
the six tasks and both working directories for Codex and Claude. Each client family
has a versioned profile and bounded single attempt. Prepare fresh package-only
fixtures before inference. Do not supply acceptance feedback or repair an agent's
output after observing a failed test.

The report fixture adds a category prerequisite to the packaged example. Its three
shared source overrides must preserve the current base application after removing
only those category additions. `report-oracle.test.mjs` verifies that invariant in
the early tooling checks, before model inference. It catches stale composition
copies that drop unrelated services; see the
[report-seed menu correction](findings/report-seed-menu-2026-11-13.md).

Codex discovery and development profiles use `ngb.config.json` as an additional
native project-root marker for fresh applications without Git. The discovery
sandbox permits the system OpenSSL configuration needed by Node.js; it still
denies application writes and shell network. Discovery retains its 300-second
and 100,000-token limits. Generated root instructions specify root-relative paths
and a focused installed-version query instead of dumping the ownership manifest.

Development profiles restore exact dependencies before inference and provide an
explicit offline build command. The Claude runner uses a short, private temporary
directory so its native Bash sandbox can compile the complete solution after the
last edit. The profile denies local socket binding, which VSTest and Vitest need.
Their test suites run independently after source review, together with the HTTP,
provider and browser checks. Codex also builds the tests offline. The independent
runner executes them with the local listener required by VSTest. Source review
uses the command record for scope and test selection. A sandbox startup failure
leaves the test outcome pending until that runner records the results.

Review produced source before executing independent acceptance. The reviewed tree
hash and changed-test selection bind the exact files. For document and permission
tasks include `reviewedFrontendFiles`; their complete frontend unit/coverage and
E2E suites are mandatory. Keep process failures, account limits and time/token
budget failures even when an independent artifact check passes.

TRX execution IDs identify individual runs; parameterized xUnit cases can share a
test definition ID. Require unique execution IDs and case names, complete counters
and passing outcomes. Retain frozen source artifacts as their original bytes so
the recorded source hashes match the files consumed by the evidence verifier.

`release.mjs certify <candidate>` now orchestrates the complete matrix automatically.
Its early preflight checks the pinned installed Codex/Claude versions, subscription
authentication and saved checkpoint integrity. After deterministic certification it
runs the 24 client/context/task combinations, reviews generated source, executes
independent acceptance and calls `verify-agents.mjs`. No manual inventory assembly
or model command loop is needed. The internal live-agent stand remains macOS arm64;
this restriction does not apply to applications using the Kit.

Client discovery checks the pinned Codex cache, PATH, `~/.local/bin` and the matching
Claude Desktop installation. `NGB_AI_CODEX_BINARY` and `NGB_AI_CLAUDE_BINARY` can
select exact installed binaries. Authentication uses the existing subscription
login; credentials are neither copied into fixtures nor exported. Frozen client
versions, budgets, isolation and task prompts remain enforced.

The automatic source reviewer is a separate tool-free Claude invocation bounded
by [source-review-profile.json](source-review-profile.json). Application code and
new files are supplied in full. Existing test changes are complete unified diffs
against the hash-verified trusted baseline, with 12 context lines and original/final
hashes. Additions, deletions, whitespace changes and final-newline changes are
preserved; tests replay these patches and compare the resulting bytes. Unchanged
test bodies are not duplicated. The reviewer also receives a path/class inventory
of integration tests and sanitized command evidence. It cannot repair code or send
feedback to the evaluated client, and must approve every changed source before
compilation. Its verdict selects changed integration tests; all authored unit
tests and required frontend suites still execute. The raw review, prompt, exact
tree, profile, test selection and independent reports are hash-bound evidence.
Selection is bounded by the application's actual class inventory, not a fixed
32-class ceiling. Shared helpers can require the whole integration suite; unknown
or duplicate classes and omissions of changed test classes remain invalid.
For document and permission tasks, frontend installation/build and reviewed
component tests with coverage run before the independent HTTP harness. Component
failures retain the JSON report and readable log, and the terminal error names
the failed tests before any HTTP/E2E hosts are started.
The independent HTTP host validates catalog and document physical schemas against
the installed metadata immediately after migrations and Setup. A type mismatch
therefore fails at host startup, before authored regression tests and E2E. The
maintenance-job regression still checks all six platform schema validators.
The [text-type failure analysis](findings/document-schema-2026-11-13.md) records
the generated migration mismatch and its independent negative control.
After the independent HTTP harness, the runner executes authored unit tests, then
newly added integration test classes, then the remaining selected classes. Each
batch retains its TRX. All batches share one 45-minute integration deadline.
Required browser acceptance follows. Both integration test assemblies
reuse PostgreSQL and Keycloak servers within each test process. Each HTTP host
has its own database and realm.
A model approval never substitutes for the deterministic or behavioral oracles.
The input ceiling is 256 KiB and the estimated-cost ceiling remains $2 per review.
The report records actual input bytes and the ceiling. Oversized input is saved
with a failed report before starting the client; it is never silently truncated.
Native client failures record their result subtype, reason and reported estimated
cost in `review.json` and the console. Both stdout and stderr are retained even
when the client exits unsuccessfully. An exhausted review budget is a failed
evaluation, not a subscription quota error or a successful verdict.

Durable state and immutable attempt directories live under
`artifacts/ai-devkit/evaluation-runs/<candidate-manifest-sha256>/`. Each scenario has
separate inference, review and acceptance checkpoints. Restarting `certify` reuses
successful stages only after checking their hashes. Completed native receipts
are recovered even if the parent stopped before checkpointing. Interrupted stages
retain their previous output; an unfinished inference starts with a fresh fixture
and no previous feedback. Failed tests, negative reviews and time/token-budget
failures are retained and stop the run, rather than triggering model retries.
Native provider quota/rate-limit errors instead save `waiting-for-access` and end
the invocation. After access returns, the next `certify` resumes with a fresh
attempt while retaining the unavailable attempt. Model-written claims about limits
and shell output cannot activate this path; no polling or repeated model calls
occur within the failed invocation.
Generated applications and trusted fixture inputs are saved with the checkpoint,
so clearing temporary directories does not destroy completed inference results.
An identical candidate sealed in a new directory shares these agent checkpoints.
Changing candidate source, packages, profiles or evaluator inputs invalidates them.

For a later independent scenario, run `node quality/ai-devkit/run-agent-evaluations.mjs <candidate> --scenario codex/root/customized-kit-upgrade` using the certification toolchain.
The selector accepts one declared client, working directory and task. It uses the
same lock, source review and acceptance checkpoints as the matrix. A completed
scenario is reused for that candidate. Only the complete matrix exports release
evidence.

`verify-agents.mjs` remains a verification-only command. An explicitly supplied
`NGB_AI_EVALUATION_EVIDENCE` selects a complete matching inventory instead of live
inference in a local certification run. GitHub Actions does not run agent evaluations
or import this evidence. Missing or stale evidence fails the early check;
it never silently falls back to another matrix. An incomplete matrix cannot pass
the agent gate. Diagnostic controls are not real-agent results.

## Read the result

Each certification profile records its candidate manifest hash, check outcomes and
child report hashes under `certification/ai-kit/`. Raw logs, TRX, coverage and
browser reports establish what actually executed. Required failures/skips and
missing source/test discovery cannot be averaged against successful checks.

Kit coverage includes all applicable helper, starter and reference-application
sources. Full platform coverage belongs to the external release process and must
remain separate. Native provider regressions also record the actual model identity;
available model access does not imply correct answers. Neither an SDK limitation
nor a provider failure authorizes changing platform business behavior in this task.

After the final source/package verification, report the assessed candidate and
passed, failed and unexecuted gates explicitly. Publication and registry smoke
require separate release authorization. Do not infer release readiness from a
single passing starter, high coverage or completed agent process.

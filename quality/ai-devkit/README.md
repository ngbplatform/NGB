# AI Development Kit verification

The specification is [SPECIFICATION.md](../../ai-devkit/SPECIFICATION.md).
Application users start with the [developer guide](../../docs/guides/ai-development-kit.md)
and [candidate release notes](../../docs/guides/ai-development-kit-release-3-4.md).
Historical investigation and implementation evidence is in [implementation.md](implementation.md);
an earlier successful run does not qualify a changed candidate.

## Inputs and execution

Use Node 24.19.0 and .NET SDK 10.0.401, a local Docker engine and the pinned tools
from [toolchain.json](../external-consumer/toolchain.json). Generate and review
template assets before packing. Seal a new candidate after any source change,
including documentation. Keep source and package bytes fixed during acceptance.
The [release runbook](../external-consumer/README.md) describes package preparation,
source identity, checkpoints and full-platform gates.

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
capability report.

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
provider and browser checks. The Codex runner also supplies its direct offline
unit-test command. Provisioning profiles run in independent acceptance.

Review produced source before executing independent acceptance. The reviewed tree
hash and changed-test selection bind the exact files. For document and permission
tasks include `reviewedFrontendFiles`; their complete frontend unit/coverage and
E2E suites are mandatory. Keep process failures, account limits and time/token
budget failures even when an independent artifact check passes.

TRX execution IDs identify individual runs; parameterized xUnit cases can share a
test definition ID. Require unique execution IDs and case names, complete counters
and passing outcomes. Retain frozen source artifacts as their original bytes so
the recorded source hashes match the files consumed by the evidence verifier.

`verify-agents.mjs` validates saved evidence and does not invoke a model. Supply
the complete matching evaluation inventory through `NGB_AI_EVALUATION_EVIDENCE`
when running `certify.mjs agents`. Missing Codex access does not prevent deterministic
checks or independent Claude evaluation, but an incomplete matrix cannot pass the
agent release gate. Diagnostic controls are not real-agent results.

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

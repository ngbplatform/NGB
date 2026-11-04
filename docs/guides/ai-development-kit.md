# AI Development Kit

The AI Development Kit supplies version-matched development instructions, skills,
public contract references, independent examples and verification tooling for NGB
applications. Developers and coding agents use the same application CLI and public
SDK packages. No NGB checkout is required after installing a published release.

This page describes the **3.4.0 candidate**. Complete release certification and real
agent evaluations are still pending. Registry commands below become available only
after publication; a passing starter profile does not establish full platform
release readiness.

See the [3.4.0 candidate release notes](ai-development-kit-release-3-4.md) for
the delivered scope, compatibility boundaries and checks required before release.

## Create and verify an application

After publication:

```sh
dotnet new install NGB.Platform.Templates@3.4.0
dotnet new ngb -n ExampleApplication
cd ExampleApplication
node infrastructure/ngb.mjs doctor --format json
node infrastructure/ngb.mjs validate
node infrastructure/ngb.mjs validate --profile build
node infrastructure/ngb.mjs test --profile all
node infrastructure/ngb.mjs certify --profile external-application
```

Use Node 24.19.0 and .NET SDK 10.0.401 for candidate verification. Integration and
browser profiles require local Docker/Compose; certification also requires k6
1.2.2. Test resources use private, disposable credentials and loopback ports. Tests
do not require an initialized deployment `.env`. See
[external applications](../architecture/external-app-upgrades.md#before-publication)
for creating an app from a sealed unpublished candidate.

The starter contains API, migrator, worker, unit/integration test projects, a
data-only assembly inspection helper and a Vue frontend with component/browser
tests. `ngb.config.json` belongs to the application and declares its solution,
project roles, frontend and test suites. Adding a project requires adding its role
and applicable tests; undeclared projects fail validation.

## Files and ownership

The repository's canonical authoring directory is `ai-devkit/`. A generated
application has this installed layout:

| Path | Owner and purpose |
|---|---|
| `ai/kit.json` | Installed SDK/UI/template/CLI/Kit versions, payload and capability identities |
| `ai/vendor/` | Official instructions, references, skills, examples and limitations |
| `ai/custom/instructions.md` | Application architecture, terminology and developer conventions |
| `ai/custom/skills/app-*/` | Canonical application-owned custom skills and supporting resources |
| `.agents/skills/ngb-*/`, `.claude/skills/ngb-*/` | Generated official agent adapters |
| `.agents/skills/app-*/`, `.claude/skills/app-*/` | Generated adapters to the canonical custom skills |
| `AGENTS.md`, `CLAUDE.md` | Only the marked `ngb:instructions` blocks belong to the Kit |
| `infrastructure/ngb/` | The shared CLI implementation, also shipped in `@ngbplatform/cli` |

Keep business terminology and application-specific workflows in custom content.
Official references describe generic public SDK contracts. Definitions remain
provider-neutral; Runtime uses public engine/persistence abstractions; PostgreSQL
adapters own storage; hosts compose them. Accounting, operational registers and
reference registers are peer engines.

Standard Post requires a registered handler, but the handler may intentionally
produce no movements. A workflow-only document can become Posted with empty
effects through an explicit public handler registration. If all handlers are
absent, Post instead returns `doc.posting.handler.not_configured` and preserves the
Draft. The independent equipment/inspection example tests both configurations.
Do not invent movements or directly change document status.

## Skill catalog and discovery

The installed catalog contains:

| Area | Skills |
|---|---|
| Composition and changes | `ngb-application`, `ngb-kit-upgrade` |
| Business contracts | `ngb-catalogs`, `ngb-documents`, `ngb-registers`, `ngb-accounting`, `ngb-reporting` |
| Cross-cutting services | `ngb-security`, `ngb-work-center`, `ngb-background-jobs`, `ngb-content`, `ngb-persistence` |
| Clients and verification | `ngb-frontend`, `ngb-mcp-assistant`, `ngb-testing`, `ngb-release` |

Codex uses `AGENTS.md` and `.agents/skills`; Claude Code uses `CLAUDE.md`, which
imports `AGENTS.md`, and `.claude/skills`. Skills link to the same official
references. Custom adapters point back to the application's canonical skill file;
resolve its relative resources from that source directory. These files are usable
from a terminal regardless of the editor used to develop the application.

For a freshly generated application without Git, start Codex with an application
root marker so it finds ancestor instructions from a project subdirectory:

```sh
codex -c 'project_root_markers=[".git", "ngb.config.json"]'
```

This uses Codex's native [project-root discovery setting](https://learn.chatgpt.com/docs/config-file/config-advanced).
The generated instructions resolve paths relative to that root and give a focused
Node command for installed versions; the complete ownership manifest is not
needed for ordinary development tasks.

These discovery contracts follow the
[Codex instructions](https://learn.chatgpt.com/docs/agent-configuration/agents-md),
[Codex skills](https://learn.chatgpt.com/docs/build-skills),
[Claude memory](https://code.claude.com/docs/en/memory) and
[Claude skills](https://code.claude.com/docs/en/skills) documentation. Structural
adapter checks pass independently of an agent account. Actual client/model/OS
compatibility is established only by recorded evaluations; the candidate's real
Codex/Claude evaluation matrix has not passed.

## Command contracts

Use `--app /absolute/application/path` when running outside the application root.
Use `--format json` on inspection, sync, upgrade, recovery and verification commands
for schema-versioned diagnostics. `--help` lists the accepted options; unsupported
flags and profiles fail instead of being ignored.

| Command | Execution and required evidence |
|---|---|
| `doctor` | Inspect declarations and installed tool versions; report missing profile prerequisites |
| `validate` | Inspect versions, Kit integrity, package sources, project/layer declarations and test inventory |
| `validate --profile build` | Restore locked packages, build backend/frontend, inspect the resolved dependency graph and managed metadata |
| `test --profile unit` | Backend unit and frontend component suites with discovery/coverage reports; default for `test` |
| `test --profile integration` | Declared backend suites against disposable services |
| `test --profile e2e` | Real browser workflows against an owned Docker deployment |
| `test --profile all` | Unit, integration, frontend and browser suites; complete combined source coverage |
| `certify --profile external-application` | All suites plus the versioned starter HTTP load profile |
| `ai status` | Compare installed official/custom assets and their baseline identities |
| `ai sync` | Preview the version-matched reconciliation plan |
| `ai sync --apply` | Apply the reviewed plan; `--resolutions file.json` supplies explicit conflict decisions |
| `recover` | Inspect an interrupted file transaction; add `--apply` to recover |
| `recover --resources` | Inspect abandoned owned test resources; add `--apply` to clean them |

Inspection does not execute application assemblies, package lifecycle scripts or
build targets. Build/test/certification execute trusted project code. Use a
disposable CI environment when evaluating an untrusted repository. Instructions
are guidance, not permissions to call deployment or production write APIs.

Exit codes are 0 for success, 1 for legacy command/input errors, 2 for failed or
unexecuted required checks, and 4 for unresolved Kit conflicts. Reports use
`passed`, `failed`, `not-run` and, for verified optional absence, `not-applicable`.
Missing Docker, browser binaries, k6, tests or coverage files never count as passes.

## Coverage, load and evidence

Reports live under `.ngb/reports/<run-id>/`. They bind the source snapshot, SDK/Kit,
dependency inputs, executed checks, discovered test counts and report hashes.
Changing inputs during execution invalidates the result.

The starter requires 100% per-file backend lines, branches and methods and 100%
frontend lines, branches, functions and statements. Every compiled/application
source must be discovered; only the documented declaration/generated-code
exclusions apply. The metadata helper reads managed files without loading or
initializing application assemblies.

`starter-http-v1` measures role and menu reads as a disposable administrator. Each
of three repetitions uses 20 warmup iterations and four users with 50 measured
iterations each. Each route must complete exactly 200 measured requests per
repetition with successful JSON and zero failures, p95 below 500 ms and p99 below
1000 ms. `performance-plan.json` records the frozen workload, hashes and runner
hardware; each repetition retains its actual measurements. These are initial
acceptance budgets, not a historical performance comparison.

This profile verifies starter behavior. Applications need their own acceptance,
query budgets and load scenarios for business operations and realistic data
volumes. A green starter report cannot certify untested reports, posting effects
or background workflows. Platform release certification separately requires the
complete capability registry, independent complex application, persisted upgrade,
agent evaluations and full repository regression gates.

## Updates and conflicts

Installing a new template never rewrites an existing app. For the initial
3.3.0 → 3.4.0 transition, acquire the exact target CLI after publication:

```sh
npm exec --ignore-scripts --package=@ngbplatform/cli@3.4.0 -- ngb upgrade --app . --to 3.4.0
npm exec --ignore-scripts --package=@ngbplatform/cli@3.4.0 -- ngb upgrade --app . --to 3.4.0 --apply
```

The application receives the same CLI implementation as the npm package and
template. No second tooling template is involved. Preview may acquire verified
artifacts into a private external cache but does not write application files.
Apply stages dependency, CLI/helper and Kit changes together, validates isolated
builds and commits one recoverable file transaction. Existing apps must integrate
their own test foundation/configuration; the upgrade does not overwrite business
code to turn them into a new starter.

Official sync uses baseline/current/incoming content. User instructions outside
managed blocks and custom sources are preserved. A changed official file or
deleted managed asset requires a reviewed decision. Resolution files bind the
exact plan and conflict identity; an intervening edit makes the decision stale.
Do not delete `ai/kit.json` or the bundled baseline to suppress a conflict.

File recovery verifies backups and refuses subsequent conflicting edits. Test
resource recovery checks the original host/application, exited owner/provisioner
processes and matching Docker labels before deletion. Private receipts/staging
remain in `.ngb/test-runs/` after an abrupt kill. If provisioning died before its
PID was recorded, or the PID has been reused by a live process, inspect the state
manually; the tool conservatively refuses automatic cleanup.

Database migrations and deployment remain separate operations with the existing
backup contract. See [external application upgrades](../architecture/external-app-upgrades.md)
for deployment sequencing and persistent-state acceptance.

## Troubleshooting

If an agent does not find guidance, check its working directory, root instruction
file, installed adapter directory and client settings. Run `ai status`; a damaged
managed block or baseline must be repaired before sync. Real agent evaluation
requires configured provider access, but deterministic Kit checks do not.

An account usage limit or unavailable model is an evaluation prerequisite failure,
not evidence that the installed instructions are corrupt. Keep the client error
and attempt identity, restore access, and follow the declared evaluation protocol.
Do not change a failed result to passed or raise its budget after seeing the output.

For mixed SDK/UI/Kit versions, use the exact registered upgrade path and target
CLI; do not replace the version only in one package file. A stale local candidate
must be repacked/resealed rather than editing its manifest or evidence. For test
failures, inspect the required check and its log in the current run directory.
Preserve recovery receipts until cleanup is verified.

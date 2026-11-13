# Verify an application with AI Development Kit

The application CLI checks project declarations, installed Kit content, builds,
tests, coverage and a starter HTTP load profile. Use these checks after changing
application code, adding projects or upgrading the SDK and Kit.

Start with the [AI Development Kit guide](ai-development-kit.md) for installation,
agent setup and file ownership. Run the commands below from your application root.
When calling the CLI from elsewhere, pass `--app /absolute/path/to/application`.

## Prerequisites

| Check | Requirements |
| --- | --- |
| `validate`, `ai status`, `ai sync` preview | Node.js 24.x and the application files |
| `doctor` | Node.js, npm and .NET 10; Docker availability is reported separately |
| Build and unit tests | .NET 10 SDK, npm dependencies and Chromium for frontend component tests |
| Integration tests | Build prerequisites and a running local Docker engine |
| Browser tests | Build prerequisites, Docker Compose and Chromium |
| Application certification | All test prerequisites and the supported k6 executable |

The CLI installs the declared Chromium version for unit/browser profiles. Package
restore and browser installation can require network access. The current release
verification toolchain uses Node 24.19.0, .NET SDK 10.0.401 and k6 1.2.2. Keep the
application's SDK and lockfiles aligned with its installed release.

Test profiles provision disposable resources with generated credentials and
loopback endpoints. They do not need a deployment `.env`. Build, test and
certification commands execute project code and tooling; inspect an unfamiliar
application before running them in an environment with valuable credentials.

## Choose a check

| Command | What it checks |
| --- | --- |
| `doctor --format json` | Application declarations, installed versions and available tools |
| `validate` | Kit integrity, versions, package sources, project roles, layers and test inventory |
| `validate --profile build` | Locked restore, backend/frontend builds, resolved dependencies and managed metadata |
| `test --profile unit` | Declared backend unit tests and frontend component tests with discovery and coverage reports |
| `test --profile integration` | Declared backend integration suites against disposable services |
| `test --profile e2e` | Browser workflows against an isolated application deployment |
| `test --profile all` | All three test profiles and combined backend source coverage |
| `certify --profile external-application` | All test profiles plus the starter HTTP load checks |

`test` defaults to the `unit` profile. Each execution profile validates and builds
the application before running its tests. A failed prerequisite is recorded in
the result, and dependent checks cannot count as successful.

For an initial check, run:

```sh
node infrastructure/ngb.mjs doctor --format json
node infrastructure/ngb.mjs validate
node infrastructure/ngb.mjs validate --profile build
node infrastructure/ngb.mjs test --profile unit
```

Choose the affected integration/browser profiles during development. When you
need the complete application acceptance result, run this single command:

```sh
node infrastructure/ngb.mjs certify --profile external-application --format json
```

It already includes the unit, integration and browser profiles. Running all of
them immediately beforehand would repeat that work.

Static inspection reads project data without executing application assemblies,
build targets or package lifecycle scripts. The metadata helper used after a build
inspects managed files without loading or initializing application assemblies.

## Register projects and tests

`ngb.config.json` is application-owned. It identifies the solution, every project
and its role, the frontend path, backend unit/integration suites and the browser
configuration. Update it when adding a project or moving a test suite.

The CLI checks the declared inventory against the application. Missing tests,
undeclared projects and absent discovery reports fail the applicable checks.
Keep integration tests independent of deployment data, and include both successful
operations and expected refusal cases such as invalid payloads and denied access.

## Coverage and load checks

The complete test/certification profile requires 100% backend lines, branches and
methods for each applicable source file. Frontend coverage requires 100% lines,
branches, functions and statements. Backend measurements are combined from unit
and integration suites. Only the CLI's recognized declaration/generated-code
exclusions apply; source discovery is checked alongside the percentages.

Coverage tells you which code executed. Add behavioral assertions for business
rules, persistence, permissions, concurrency and errors so that the tests also
establish the behavior your application requires.

The `starter-http-v1` load profile measures role and menu reads as a disposable
administrator. It runs three repetitions. Each repetition uses 20 warmup
iterations followed by four users with 50 measured iterations each. For each
route, acceptance requires exactly 200 measured requests, successful JSON,
zero failures, p95 below 500 ms and p99 below 1000 ms.

`performance-plan.json` records the workload, input identities and runner hardware;
the run retains each repetition's measurements. These budgets apply to the starter
routes. Define additional load scenarios for your own posting workflows, reports,
background work and realistic data volumes.

## Read the result

Build/test/certification reports are saved under `.ngb/reports/<run-id>/`. They
identify the source snapshot, dependency inputs, SDK/Kit versions, executed checks,
discovered tests and child report hashes. Changing inputs during a run invalidates
its result. Keep the current report and logs when diagnosing a failure.

Add `--format json` to receive structured CLI output. Checks use `passed`,
`failed`, `not-run` or `not-applicable`. Optional absence must be verified before it
can be `not-applicable`. A missing required tool, failed test or missing coverage
report cannot count as a pass.

| Exit code | Meaning |
| --- | --- |
| `0` | Successful command |
| `1` | Command/input error reported through the legacy text path |
| `2` | Failed or unexecuted required check, including structured input errors |
| `4` | Unresolved Kit ownership conflict |

Interrupted upgrades return `130` for SIGINT or `143` for SIGTERM.

Start with the first failed prerequisite in the JSON report. For a test failure,
open its saved TRX, frontend JSON or browser report and the corresponding log.
Skipped required tests and empty test discovery need investigation even if a test
runner exited successfully.

## Recover an interrupted run

For an interrupted file update:

```sh
node infrastructure/ngb.mjs recover
node infrastructure/ngb.mjs recover --apply
```

Inspect the preview before applying recovery. It validates the transaction's
backups and preserves conflicting edits made after the interruption.

For abandoned test resources:

```sh
node infrastructure/ngb.mjs recover --resources
node infrastructure/ngb.mjs recover --resources --apply
```

Resource recovery checks the original host/application, exited owner and
provisioner processes, and matching Docker ownership labels. Private receipts
remain under `.ngb/test-runs/`. If the recorded owner is still active, a PID has
been reused or provisioning ownership is incomplete, inspect the run manually
before removing resources. Keep recovery receipts until cleanup is complete.

## Application checks and platform release checks

`infrastructure/ngb.mjs certify` verifies the generated application's declared
tests and load profile. It does not launch Codex or Claude, publish packages or
deploy the application.

NGB maintainers separately run FULL Certification from the platform repository.
That process checks package compatibility, template creation, public SDK consumers,
persisted upgrades, Kit tooling/examples and real Codex/Claude scenarios. Full
platform Backend/Frontend tests and full coverage are run independently by the
maintainer. GitHub Actions builds and publishes packages without repeating those
local suites or agent evaluations.

An application developer only needs the checks appropriate to their application.
The maintainer's macOS arm64 agent evaluation harness is not required to use the
Kit on another operating system.

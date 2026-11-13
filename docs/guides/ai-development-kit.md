# AI Development Kit

The AI Development Kit helps developers and coding agents build independent NGB
applications using the public SDK. It supplies development instructions, task
skills, API references, a working example and an application CLI. The guidance
ships with the application and matches its installed platform version.

Use it to add catalogs, documents, actions, register effects, reports, permissions
and frontend features while following the platform's composition rules. The same
CLI checks work whether a developer or an agent writes the code. A published
installation requires no NGB source checkout.

The Kit supports development with Codex and Claude Code. The runtime
[MCP Server](mcp-server.md) and [AI Assistant](ai-assistant.md) are separate
application features with their own configuration and permissions.

## Create an application

Install Node.js 24.x, the .NET 10 SDK and Docker with Compose v2. Docker is needed
for integration tests and running the application. The installed application's
project files, package lockfiles and `doctor` output describe its tool requirements.

Choose an exact published platform version. These commands use `3.4.0` as the
example version. For an unpublished build, follow
[local package installation](../architecture/external-app-upgrades.md#before-publication).

```sh
dotnet new install NGB.Platform.Templates@3.4.0
dotnet new ngb -n ExampleApplication
cd ExampleApplication
node infrastructure/ngb.mjs doctor --format json
node infrastructure/ngb.mjs validate
```

The starter includes API, Migrator and Background Jobs hosts, backend unit and
integration tests, a Vue frontend with component and browser tests, and a metadata
inspection helper. `ngb.config.json` declares the solution, project roles, frontend
and test suites. Register new projects and their tests there as the application grows.

To start the development stack, initialize its configuration once and then start it:

```sh
node infrastructure/configure.mjs administrator@example.com
node infrastructure/ngb.mjs start
```

Open `http://localhost:5182` and sign in with the configured email and the generated
`BOOTSTRAP_PASSWORD` in the application's private `.env`. The initializer refuses
to overwrite an existing `.env`. See
[external applications](../architecture/external-app-upgrades.md#create-an-application)
for service addresses and startup troubleshooting.

## Work with a coding agent

Open the generated application as the agent's workspace. The Kit provides these
native discovery files:

| Client | Instructions | Skills |
| --- | --- | --- |
| Codex | `AGENTS.md` | `.agents/skills/` |
| Claude Code | `CLAUDE.md`, importing `AGENTS.md` | `.claude/skills/` |

Both clients read `ai/vendor/instructions.md` and `ai/custom/instructions.md`.
Skills link to the same public contract references and examples. Use your own
authenticated agent installation; ordinary Kit commands do not require an agent
subscription or model access.

For a newly generated application without Git, this Codex setting also lets a
session started in a project subdirectory find the application root:

```sh
codex -c 'project_root_markers=[".git", "ngb.config.json"]'
```

Check the installed versions from the application root:

```sh
node -p 'JSON.stringify(require("./ai/kit.json").versions)'
```

Give the agent a concrete business change, its rules and its expected results.
For example:

```text
Add an Equipment Category catalog with a unique code and a required display name.
Use the installed ngb-catalogs and ngb-testing skills and public SDK contracts.
Register its storage, migration, permissions and navigation.
Test create/read/update, duplicate codes and access denial.
Run the applicable application checks and report their results.
```

Review the resulting definitions, migrations, permission rules and tests before
accepting the change. When an installed public contract cannot express a requested
behavior, resolve that design decision explicitly before implementing it.

### Skill catalog

| Area | Skills |
| --- | --- |
| Composition and updates | `ngb-application`, `ngb-kit-upgrade` |
| Business contracts | `ngb-catalogs`, `ngb-documents`, `ngb-registers`, `ngb-accounting`, `ngb-reporting` |
| Shared services | `ngb-security`, `ngb-work-center`, `ngb-background-jobs`, `ngb-content`, `ngb-persistence` |
| Clients and verification | `ngb-frontend`, `ngb-mcp-assistant`, `ngb-testing`, `ngb-release` |

The generated instructions are ordinary project files and can be used on macOS,
Windows and Linux with the required development tools. The platform maintainer's
automated live-agent evaluation harness has a declared macOS arm64 environment.
That harness records a specific client/model combination; it is not part of an
application's development or deployment prerequisites.

## Files and ownership

The NGB repository authors the Kit in `ai-devkit/`. A generated application receives
the following layout:

| Path | Purpose and ownership |
| --- | --- |
| `ai/kit.json` | Installed versions, managed file identities and ownership metadata |
| `ai/vendor/` | Official instructions, references, skills, examples and known limitations |
| `ai/custom/instructions.md` | Your application's architecture, terminology and conventions |
| `ai/custom/skills/app-*/` | Your canonical custom skills and supporting files |
| `.agents/skills/ngb-*/`, `.claude/skills/ngb-*/` | Generated adapters for official skills |
| `.agents/skills/app-*/`, `.claude/skills/app-*/` | Generated adapters for your custom skills |
| `AGENTS.md`, `CLAUDE.md` | Application files containing Kit-owned `ngb:instructions` blocks |
| `infrastructure/ngb/` | Application CLI, also distributed as `@ngbplatform/cli` |

Put business terminology, application rules and team conventions in
`ai/custom/instructions.md`. Add reusable application skills under
`ai/custom/skills/app-<name>/SKILL.md`. Synchronization generates their client
adapters. Edit the canonical custom source and resolve its relative resources
from that directory.

Keep official content in `ai/vendor/` intact. The CLI detects edits and deletions
there as ownership conflicts. Content outside the managed blocks in root
instruction files belongs to the application and survives synchronization.

## Develop and verify a change

Start with the inexpensive inspection commands, then build and run the relevant
tests:

```sh
node infrastructure/ngb.mjs ai status
node infrastructure/ngb.mjs validate
node infrastructure/ngb.mjs validate --profile build
node infrastructure/ngb.mjs test --profile unit
```

Add `test --profile integration` for storage and HTTP behavior and
`test --profile e2e` for browser workflows. For complete application acceptance,
use `certify --profile external-application`, which includes all test profiles
and the starter HTTP load checks.

The [verification guide](ai-development-kit-verification.md) explains prerequisites,
coverage, report locations, exit codes and recovery. These commands execute the
tests declared by your application. Keep that inventory current as you add features.

### Use the reference application

`ai/vendor/examples/equipment-inspection/` contains a complete public SDK example.
It demonstrates catalogs, document lifecycle, accounting and register effects,
reports, permissions, Work Center, background jobs, attachments and notes, and
optional MCP/assistant composition. Its integration tests exercise real HTTP,
storage and authorization behavior.

Read its `README.md` for the available workflows and exact execution commands.
Copy the example outside `ai/vendor/` before building or editing it so that build
outputs and experiments do not modify installed official assets. Follow its
package setup instructions when using unpublished packages.

Definitions describe provider-neutral business contracts. Runtime composes public
engine and persistence abstractions. PostgreSQL adapters own physical storage,
and application hosts register the components. Accounting, operational registers
and reference registers are peer engines selected for the business behavior needed.

A document can post with no movements when its registered handler intentionally
returns empty effects. With no registered posting handler, standard Post returns
`doc.posting.handler.not_configured`. The example covers both configurations.
Read `ai/vendor/known-limitations.json` and the relevant skill before depending on
behavior beyond the installed SDK's contract.

## Updates and conflicts

### Synchronize installed guidance

Use synchronization after adding custom skills or when reconciling managed files
with the installed version:

```sh
node infrastructure/ngb.mjs ai sync --format json
node infrastructure/ngb.mjs ai sync --apply
node infrastructure/ngb.mjs ai status
```

Preview shows the plan without changing application files. Apply preserves custom
sources and application-owned root instructions. It stops at unresolved conflicts.
Inspect the JSON plan before providing explicit resolutions.

Create `reviewed-resolutions.json` using `plan.planSha256` and the affected entry's
`id` from that preview. For example, to restore the official incoming content:

```json
{
  "schemaVersion": 1,
  "planSha256": "<plan.planSha256 from the preview>",
  "decisions": [
    {
      "id": "<conflicting entry id from the preview>",
      "choice": "incoming"
    }
  ]
}
```

`incoming` selects official content, `local` preserves the current content, and
`replacement` supplies reviewed merged text in an additional `content` field.
Then apply the reviewed decisions:

```sh
node infrastructure/ngb.mjs ai sync --apply --resolutions reviewed-resolutions.json
```

Resolution files bind the exact plan and conflict hashes. An intervening edit makes
the decision stale. Preserving modified official content also requires reviewing
its conformance; it does not automatically produce a clean official integrity check.
Keep `ai/kit.json` and the bundled baseline so the CLI can compare ownership safely.

### Upgrade the platform and Kit together

Installing a newer template affects newly created applications. Update an existing
application through a registered upgrade transition using the exact target CLI.
For the supported `3.3.0` to `3.4.0` transition:

```sh
npm exec --ignore-scripts --package=@ngbplatform/cli@3.4.0 -- ngb upgrade --app . --to 3.4.0
npm exec --ignore-scripts --package=@ngbplatform/cli@3.4.0 -- ngb upgrade --app . --to 3.4.0 --apply
```

Registry commands require published target packages. Use the
[local upgrade procedure](../architecture/external-app-upgrades.md#migrate-3-3-0-to-3-4-0)
when evaluating an unpublished target.

Preview may download verified artifacts into an external cache. Apply stages
dependencies, CLI/helper files and Kit content together, validates isolated builds
and commits a recoverable file transaction. Your business source and custom
instructions remain application-owned. Existing applications must declare their
own project and test inventory; upgrading does not replace them with a new starter.

Database migration and deployment are separate steps. Follow the
[upgrade and deployment guide](../architecture/external-app-upgrades.md) for
backups, migration ordering and service startup.

## Troubleshooting

| Symptom | Next step |
| --- | --- |
| The agent does not find NGB guidance | Check the workspace root, `AGENTS.md`/`CLAUDE.md` and native skill directories. Start from the root or configure root discovery. |
| `ai status` reports changed files | Inspect `ai sync --format json`, review the affected ownership and resolve the exact conflict. |
| SDK, UI and Kit versions differ | Use the registered coordinated upgrade and exact target CLI. |
| Validation reports an undeclared project | Add the project role and applicable test suite to `ngb.config.json`. |
| A required check fails or does not run | Read its saved report and log using the [verification guide](ai-development-kit-verification.md#read-the-result). |
| An update or test run was interrupted | Inspect the corresponding [recovery procedure](ai-development-kit-verification.md#recover-an-interrupted-run). |

If an agent account has reached its usage limit, restore access through that
provider. You can continue developing and running application checks directly.

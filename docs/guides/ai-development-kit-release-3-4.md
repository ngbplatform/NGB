# AI Development Kit 3.4.0 candidate release notes

Version 3.4.0 introduces the AI Development Kit for independent NGB applications.
This is a candidate description, not a publication announcement or a claim that
all release gates have passed. Registry installation becomes available after the
matching packages are published and their registry checks succeed.

## Delivered scope

- `NGB.Platform.Templates` installs the shared CLI, versioned official guidance,
  public contract references, neutral examples and Codex/Claude skill adapters.
- `@ngbplatform/cli` supplies the same implementation for source-free creation and
  target-version upgrades. There is no separate tooling template.
- `doctor`, `validate`, `test`, `certify`, `ai status`, `ai sync` and recovery
  commands provide explicit inspection/execution boundaries and JSON evidence.
- The starter includes backend unit/integration tests, frontend component and
  browser tests, a metadata inspection helper and application-owned configuration.
- Official assets use `ai/vendor/`; application instructions and custom skills use
  `ai/custom/`. Only bounded blocks in root instruction files belong to the Kit.
- The equipment/inspection reference application demonstrates public SDK/UI
  composition, documents, catalogs, peer effect engines, reports, permissions,
  Work Center, background work, content and optional MCP/assistant integration.

The audited registry describes 224 capabilities and classifies 1716 public
contracts for this candidate. Classification counts identify the reviewed surface;
functional acceptance additionally requires the corresponding executable checks.
Neither the Kit nor its registry depends on a particular installed business product.

## Versions and supported execution

SDK, UI, CLI, template and Kit versions are coordinated at 3.4.0. Candidate
verification uses Node 24.19.0, .NET SDK 10.0.401, Docker/Compose and the declared
browser/load tools. No globally installed NGB source checkout is required by a
generated application. Use the exact candidate archives before publication.

The real-agent protocol evaluates Codex and Claude Code from both the application
root and a subdirectory. It covers discovery, a new catalog, an existing-document
change, permissions, reports and customized upgrades. Every attempt records its
client/model, input identities, budget, produced source and independent acceptance.
The declared terminal environment is macOS arm64. Structural adapter checks or
results on that environment do not certify other clients, operating systems or
editor integrations. The application files remain editor-independent.

Protocol v4 explicitly permits document changes to update TypeScript test sources
under `web/tests/unit` and `web/tests/e2e`, while preserving frontend application
code and build configuration. Document and permission tasks must pass their own
frontend suites as well as independent acceptance. Earlier protocol results retain
their original identities and cannot substitute for changed evaluation inputs.

## Upgrade and application ownership

The registered transition is 3.3.0 → 3.4.0. Acquire the exact target CLI, preview
the update, resolve ownership conflicts and apply the coordinated file/dependency
transaction. Installing a template alone never updates an existing application.
Existing business code and application-owned root instructions are preserved.
Custom skills remain canonical in the application and render adapters for both
agents. Modified official assets require a hash-bound resolution; stale decisions
and interrupted writes are detected.

Existing applications integrate their own `ngb.config.json` and test foundation;
the upgrade does not replace their source with the new starter. Database migrations,
backups, service startup and deployment are separate operations. Follow the
[upgrade guide](../architecture/external-app-upgrades.md#migrate-3-3-0-to-3-4-0)
and the [ownership and recovery procedure](ai-development-kit.md#updates-and-conflicts).

## Boundaries and release acceptance

The Kit documents existing public capabilities; it does not add business behavior
to satisfy an example. A registered handler may post a document without movements.
Omitting every handler is a different configuration and is rejected by the existing
SDK. Runtime MCP authorization and its read-only business surface are unchanged.

Application tests, builds and certification execute trusted application code.
Inspection commands do not execute application assemblies. Integration resources
and credentials belong to disposable runs; deployment data is not a test fixture.
Load results establish the declared workload and bounds, not arbitrary production
capacity or the performance of an untested application.

Candidate readiness requires matching package identities, complete deterministic
Kit checks, the starter, the complex reference application, persisted upgrades,
required coverage, performance/security checks and valid real-agent evidence.
The external release process additionally owns full platform regression gates.
Missing access, failed assertions, skipped required tests and stale reports remain
visible failures or incomplete checks; they are not waived by coverage percentages.

Publishing, registry-only installation and post-publication identity verification
are later release operations. A local candidate report does not establish them.
Start with the [developer guide](ai-development-kit.md) for commands, file layout,
test profiles, troubleshooting and conflict recovery.

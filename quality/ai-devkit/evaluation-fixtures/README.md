# Independent development evaluation fixtures

These fixtures are release verification inputs, outside the evaluated application's
writable tree. They are not distributed as application test code or supplied as
answers to an agent. The corresponding `*.contract.json` contains the observable
task requirements and exact matrix prompt. Freeze these inputs before inference.

The document-change, catalog, report and permission oracles share a version-bound HTTP host,
disposable PostgreSQL and Keycloak fixtures, and public request helpers. Each oracle
declares the exact source paths and byte hashes it accepts. The runner copies those
sources into a separate test project and references the produced application's
public entry points. It never accepts agent-written test results as its oracle.

`prepare-development-fixture.mjs <task> <candidate> <new-external-directory>`
materializes the packaged example and installs the candidate's actual template
guidance, both agent adapters and CLI. The report seed adds its already implemented
category prerequisite. The permission seed introduces two declared application
configuration mistakes. Their hash-bound overlays are applied before capturing
the trusted initial inventory. Neither fixture depends on a previous agent attempt.
The resulting directory contains `application/`, a candidate NuGet feed, UI archive,
task contract and input receipt. Preparation alone does not invoke an agent.

`run-development-oracle.mjs` takes a task, sealed candidate, external application,
trusted seed inventory, new evidence directory and reviewed output-tree SHA-256:

```sh
node quality/ai-devkit/run-development-oracle.mjs existing-document-change \
  /path/to/sealed-candidate /path/to/application /path/to/before.json \
  /path/to/new-evidence <reviewed-tree-sha256> /path/to/reviewed-test-selection.json
```

The trusted evaluator captures `before.json` before the agent runs and reviews its
source changes before invoking the runner. The hash argument identifies that exact
reviewed tree. It is not permission to execute unreviewed source. Test-specific
source guards preserve SDK/build inputs, installed guidance and existing migrations.
Review additionally verifies provider boundaries and application-owned behavior.

Release acceptance also requires a reviewed test selection. Its
`producedTreeSha256` identifies the same source tree, `reviewedTestFiles` lists every
changed application test source in sorted order, and `integrationClasses` contains
the fully qualified HTTP test classes to execute. The runner executes all unit
tests and the selected integration classes after the independent harness passes.
An empty test discovery, omitted changed test file or failing authored test prevents
acceptance. Omitting this selection is useful for isolated oracle controls, but
cannot satisfy the real development evaluation gate.

For document and permission tasks, `reviewedFrontendFiles` must list every changed
TypeScript test source under `web/tests/`, in sorted order; use an empty array when
none changed. Both tasks install the exact candidate UI package and execute the
application's complete frontend unit/coverage and E2E suites. Acceptance requires
all test files to be discovered, no failures/skips/retries and complete per-source
coverage. The runner preserves available raw JSON reports even on failure.
Document changes may update TypeScript test sources under `web/tests/unit/` and
`web/tests/e2e/` so existing successful Post workflows supply the newly required
field. Frontend application code and test/build configuration remain unchanged.
Permission tasks additionally retain their independent three-case browser oracle.

Protocol `terminal-macos-arm64-v4` adds these checks and the explicit document-test
scope before new inference. Earlier attempts retain their original protocol and
outcomes. Diagnostic replay of a saved output can validate the revised runner;
it cannot turn an earlier failed attempt into a successful v4 evaluation.

The runner uses Node 24 and .NET SDK 10.0.401, an isolated home/package cache and a
fresh source-only copy. It restores the complete application solution against the
candidate's exact NuGet artifacts, builds every project, then restores/builds/runs
the independent harness. Cached application assemblies cannot satisfy acceptance.
The produced application and tested source copy must remain unchanged. A missing,
failed or incomplete independent test report cannot pass. The output retains frozen
harness/oracle/runner/contract sources, logs, TRX, input inventories and their hashes.
The release verifier copies and checks these raw artifacts, the actual client
configuration and frozen runner/profile sources. SDK, UI, template, CLI, Kit,
task oracle, fixture preparer and acceptance implementation identities must match
the declared current inputs. A previously successful process or edited summary
alone cannot substitute for this evidence.

Catalog migration replay invokes the actual Migrator from its own build output as
a separate bounded process. It does not reuse the API/test host's loaded assemblies
or its runtime configuration. No manual SQL is used by this evaluation workflow.

Permission acceptance also builds the produced frontend and runs three independent
Playwright cases against its production build. The browser host starts from its own
output directory so ASP.NET's generated `MvcTestingAppManifest.json` is discoverable;
see the [WebApplicationFactory implementation](https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Mvc/Mvc.Testing/src/WebApplicationFactory.cs).
Test credentials exist only for that disposable realm. The oracle requires exact
successful browser case identities as well as API results.

`prepare-upgrade-evaluation.mjs <candidate> <new-external-directory>` prepares the
verified frozen 3.3.0 application and a declared ownership collision. It supplies
the exact target CLI outside the application, captures an independent read-only
preview and leaves the application at 3.3.0. The agent must preserve the collided
instructions under `ai/custom/legacy-instructions.md` and resolve the incoming
official file through the hash-bound CLI plan. Resolution files stay outside the
application so the preservation contract has no arbitrary executable additions.

`run-upgrade-oracle.mjs <candidate> <application> <trusted-fixture-directory>
<new-output> <reviewed-tree-sha256>` independently compares application preservation,
official bytes, versions, dependency lock identities and both custom adapters. It
then runs clean locked backend/helper/frontend builds and installed `ai status`.
Its trusted preview proves CLI behavior; the actual agent transcript must separately
prove that the agent previewed, resolved and applied the upgrade without migration
or deployment. Artifact acceptance alone does not establish those agent actions.

Positive and deliberately faulty control implementations validate the oracles
before inference. Those controls are not real-agent results. Completed process
controls and the full client/context development matrix are still required before
release readiness can be claimed.

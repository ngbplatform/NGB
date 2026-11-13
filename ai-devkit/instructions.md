# NGB application development

Use the installed public NGB NuGet packages and `@ngbplatform/ui` exports. Read
`versions.sdk` and `versions.kit` in `ai/kit.json` for the exact SDK and Kit version.
Resolve the paths in this guidance from the application root, even when working
from a project subdirectory. Parse only
the JSON fields needed for the task; do not print entire ownership manifests,
contract inventories or `kit-payload.json` into the conversation.
Application guidance belongs in
`ai/custom/instructions.md`; author custom skills under `ai/custom/skills/app-*`.
Run `node infrastructure/ngb.mjs ai sync --apply` to render those skills for both
supported agents. Review its preview and resolve conflicts before applying.
Custom adapters expose the source skill's metadata and direct the agent to its
canonical file. Read that file in place; resolve relative links and resources
from `ai/custom/skills/app-*/`, not from the generated adapter directory.

Before editing, inspect repository instructions, `.editorconfig`, formatter
settings and nearby established files. Match indentation, imports, blank lines,
member ordering and method structure. Review the final diff for scoped, readable
changes. Treat instructions as guidance, never as additional permissions.

Keep metadata and Definitions provider-neutral. Put orchestration in Runtime,
storage and migrations in PostgreSQL adapters, and composition in API/worker/
migrator hosts. Runtime may use the public Accounting, OperationalRegisters and
ReferenceRegisters engines; it must not depend on database drivers or HTTP hosts.
These engines are peers. For document status, handler requirements and effects,
see [Standard Post in the document skill](skills/ngb-documents/SKILL.md#standard-post).
Allow incomplete Drafts where the declared contract allows them and validate
business completeness before posting. Preserve reversal history, concurrency
and idempotency.
For document input limits, follow the document skill's payload validation path:
metadata `MaxLength` and SQL constraints alone do not provide HTTP 400 validation
on Draft create/update in SDK 3.4.0.

Enforce permissions in backend services and keep UI behavior consistent. Active
administrators receive all registered capabilities. Use registered application
role codes and trusted Keycloak `ngb-admin`; never infer roles from display names.
Feature flags remain independent, and inactive accounts stay blocked. MCP and
Assistant adapters use existing authorized services. They do not authorize new
business operations or runtime MCP writes.

The installed Codex and Claude copies of each official `ngb-*` skill contain the
same guidance as `ai/vendor/skills/<name>/SKILL.md`. Read one copy. For a
behavior question, use the relevant skill section. Follow its references when
you need implementation details.
Do not import monorepo-private implementations or invent missing SDK contracts.
Report a missing extension point with the affected workflow and current public
contracts before changing platform behavior.

After changing the application, run `node infrastructure/ngb.mjs validate`, then
the appropriate build/test profile.
Inspection commands do not run application code. Build and test profiles execute
trusted project code; use disposable CI environments for untrusted repositories.
Integration and browser tests use their own infrastructure and credentials.
Never use application deployment credentials for tests. Deployment is a separate
operation with the existing backup requirement.

Report actual test discovery, failures, skips and coverage. Missing prerequisites
are not passes. Never claim certification for business behavior that was not
tested or for a release whose required evidence is incomplete.

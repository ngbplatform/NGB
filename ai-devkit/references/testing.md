# Verification profiles and evidence

Use the application's own `node infrastructure/ngb.mjs` entry point. `doctor`,
plain `validate`, `ai status` and sync preview inspect configuration/data without
executing application builds or tests. `validate --profile build` executes trusted
project tooling and inspects resolved dependencies and managed metadata.

`test` defaults to unit tests. `test --profile integration` runs backend integration
suites; `test --profile e2e` runs browser workflows; `test --profile all` requires
unit, integration and browser suites. Required missing/skipped checks fail.
Unit tests include frontend browser-component prerequisites where configured.
The CLI collects backend TRX/Cobertura and frontend/browser discovery evidence.

For the official starter, the combined backend gate requires every compiled
production source to appear and achieve 100% lines, branches and methods. The
frontend requires 100% lines, branches, functions and statements per file. Only
audited declaration/generated-code exclusions apply. Do not remove discovered
files, suppress a reachable branch or accept an empty report to reach a threshold.

Use `.ngb/reports/<run-id>/report.json` and its referenced report hashes. Source
inputs must remain unchanged during a run. Record actual suite counts and failures;
passing a build does not prove startup, data migration or business correctness.

Child tools receive an isolated environment with explicit runtime paths and
caches. Standard `HTTP_PROXY`, `HTTPS_PROXY`, `ALL_PROXY`, `NO_PROXY` and lowercase
equivalents are retained for network transport. Proxy URLs must be bounded origins
without paths, queries or fragments; HTTP(S) is supported, and `ALL_PROXY` also
accepts SOCKS5/SOCKS5H. Embedded proxy authentication is accepted only for the
loopback hosts `localhost`, `127.0.0.1` and `[::1]`, for native sandbox transport.
Proxy user/password values are redacted from subprocess diagnostics. `NODE_USE_ENV_PROXY=1` is retained only
when a nonempty proxy is present. Separate API tokens, deployment connections
and package-registry overrides are not inherited. Proxy settings do not replace
the evaluator's native network restrictions or authorize access to a deployment.
An agent sandbox may protect `.agents` even when ordinary application files are
writable. Installing/updating managed skills needs permission for the application
skill directories. Codex can use a scoped write grant for those directories.
Claude Code's native Bash sandbox protects `.claude/skills` even when `allowWrite`
covers it; that mode cannot commit a coordinated Kit upgrade. Run the reviewed
target CLI command from your own terminal when the client blocks this operation.
Do not disable the sandbox or grant access to unrelated configuration just to
install skills. A failed upgrade must retain its error and transaction/recovery
evidence. See the clients' [filesystem permissions](https://learn.chatgpt.com/docs/permissions)
and [protected paths](https://code.claude.com/docs/en/sandboxing#protected-paths).

`certify --profile external-application` also requires its performance evidence;
an incomplete certification profile must report NOT READY.

Kit capability acceptance binds a declared source to its actual test runner and
raw report hash. Backend cases use the full TRX test name. Browser cases use a
canonical JSON array of the Playwright project name, any nested describe titles,
and the test title; the source path is checked separately against the packaged
example. An unnamed project uses an empty first string. Aggregate browser counts
alone cannot certify a named capability. Missing cases, another project, an
unpackaged file, changed reports, retries or skips cause refusal. A failing
independent example does not receive a passing capability certification.

The versioned `starter-http-v1` workload uses k6 1.2.2 against the owned test API.
Each of three repetitions warms up 20 iterations, then runs four virtual users
with 50 iterations each. Each iteration reads roles and the main menu as a test
administrator. Both routes must complete exactly 200 measured requests per
repetition, return successful JSON, have no failures and meet p95 below 500 ms and
p99 below 1000 ms. These initial budgets precede measurements; they are not a
historical comparison. `performance-plan.json` records the workload, hashes and
runner hardware before execution; per-repetition evidence records measured values.
This profile covers starter HTTP reads. Add separate acceptance and load workloads
for application business behavior, data volumes and query budgets.

Integration/E2E use test-only credentials and disposable services. The CLI owns
the browser environment and cleans it after success, failure or handled
cancellation. It verifies ownership before removing containers, networks or
volumes. Never attach an existing application database or use deployment `.env`.
Before provisioning, the CLI writes a private ownership receipt under
`.ngb/test-runs/<run-id>/`. After an abrupt controller kill, inspect
`node infrastructure/ngb.mjs recover --resources`, then use the same command with
`--apply` to remove that application's owned resources and staging files. Recovery
requires the original host and application path, and refuses live owner/provisioner
processes or mismatched Docker ownership. If the controller died before recording
the provisioner PID, inspect that process manually; recovery must not guess that
provisioning has stopped. A reused live PID also causes a conservative refusal.
Receipts contain no credentials; the private staging directory contains only
disposable test credentials and is removed with its resources.

Define business outcomes independently of implementation: known totals, version
conflicts, retries, rollback and unauthorized calls. Verify real API and provider
behavior before making persistence or security claims. Freeze performance workloads,
runner identity and tolerances before measuring them; separate tooling timings,
query counts, volume tests and live load results.

When a document gains a Post requirement, update the positive test data as well
as its new validation tests. The example's `InspectionRequests.CompleteInspectionPayload`
is the shared valid input for lifecycle, batch posting, reporting and typed-storage
guard tests; `CreateCompleteInspectionAsync` uses it too. Keep the generic
`Payload`, `PostAsync` and action helpers transparent: they must not silently add
fields that would conceal invalid input. Explicit incomplete Drafts, validation
cases and frozen migration-baseline records retain their own inputs.
Search all successful Post paths, including tests outside the document's named
test class, and run the affected regression classes. A guard or report test that
fails while creating its setup has not exercised the invariant it claims to test.

Compare JSON responses by values, not serialized text. An idempotent action replay
may read its stored result from PostgreSQL `jsonb`, which can reorder object
properties. Use `InspectionRequests.AssertEquivalentJsonAsync` or deserialize and
compare typed fields; `JsonElement.DeepEquals` ignores object property order while
still checking values, missing members and array order. Keep explicit assertions
for status, version and side effects. Do not require byte-for-byte response equality
unless the public contract explicitly promises it.

Create test identities and initial role links before their first request. To test
changing an existing user's access, use the public user/role management API: it
updates access versions as well as assignments. Direct repository setup does not
reproduce that invalidation contract after permissions have been cached. Prefer
separate fresh identities for independent positive/negative administrator cases,
and keep role codes unique within the test database. Administrator behavior must
still be based on registered codes, including after a display-name change.
`InspectionHost.LinkUserAsync` is initial setup, not a role-change API. The
report example uses different identities for a reader, a display-name-only
administrator and a registered administrator, then verifies deactivation on the
same administrator. Preserve that separation when adapting its tests.

Keep negative HTTP cases individually identifiable through parameterized tests or
an assertion message containing a synthetic case name. Derive expected statuses
from the installed public behavior rather than assuming all invalid inputs map
to 400. Record unsupported error mappings explicitly, as for
[composable report date parameters](reporting.md#parameters-and-authorization-test-setup).
Such a characterization test documents current behavior; it does not establish
that an unmet application requirement has been implemented.

Vitest Browser Mode uses native ESM caching. `vi.resetModules()` does not make a
second import of the same startup module execute its side effects again. For
bootstrap tests, import `startApplication` from `src/bootstrap.ts` and call/await
it for each case, including parameterized cases. The example and starter keep
`src/main.ts` as the one-time browser entry point and test that entry separately.
Use a pending promise only when proving an intermediate state such as waiting
for access loading; await completion before the test ends. Reset mock state per
test. Do not repair a missing second startup by increasing waits or by adding
arbitrary query strings to imports.

Give browser-created lookup records distinct names for each repeated scenario.
For a view-only editor, wait for the saved field value and assert that Save is
disabled. When checking API metadata during sign-in, register the response wait
before the triggering action and read the body before the next full-page
navigation. Chromium may discard the previous page's response body after
`page.goto()`. Pair a wait for an editor GET with the navigation that triggers it.
Keep exact-match selectors and assert both the displayed numeric value and the
saved API payload. If input is intermittent, retain the failing report and record
the affected field's input events; a successful repetition alone does not identify
the cause or turn the original failed candidate into a passing one.

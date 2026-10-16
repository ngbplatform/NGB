# MCP verification

The checks use the official MCP SDK, Kestrel, PostgreSQL 16 and Keycloak 26.5.6.
The external fixture restores public packages into a fresh directory and cache
outside the repository. It rejects any NGB project-reference resolution.

```sh
node quality/mcp/verify-contract.mjs
node quality/mcp/verify-external.mjs /absolute/path/to/candidate/nuget-release
node quality/mcp/verify-verticals.mjs
```

The contract verifier runs `NGB.Mcp.Tests` (unit and schema contracts) and
`NGB.Mcp.AspNetCore.IntegrationTests` (real Kestrel HTTP/SDK integration), with
separate TRX reports. Direct filter, handler and configuration tests stay in
the unit project. Shared service stubs are linked from `McpTestServices.cs`;
neither test project references the other.

Application-scope checks verify that documentation is absent from the MCP
assembly, discovery and capabilities. Direct requests for the removed
documentation tools fail for ordinary users and administrators, including
through the official SDK and the external package-only consumer.

The backend coverage runner merges OpenCover inputs to preserve complementary
branch paths exercised by separate test projects. It still publishes the usual
combined Cobertura report for the coverage gate.

The external fixture covers an empty host and a custom installed catalog/tool,
real user authorization-code/PKCE authentication, bootstrap administrators,
linked users, revocation with the same token, inactive accounts and persistent
state snapshots around reads. The custom catalog is measured at 101 and 10,001
additional records; cursor traversal checks every identity exactly once at each
size. Pages of 1, 10 and 100 records must have a constant database command count.

The four-host fixture uses each actual API host, migrations and demo data.
It compares records, current Effects and report execution with REST, checks
flow and audit, and repeats with Notes/Attachments enabled and disabled.
Restricted users cannot read parent-protected data. Snapshot comparisons cover
all public-schema tables, including audit, outbox and posting data.

## Performance evidence

`external/performance-budgets.json` contains the SQL budgets established by
measuring the real package-only fixture. Warm catalog list, detail, search and
custom current-user reads used 3, 5, 3 and 2 commands respectively, including
authorization. These maxima are enforced for every sample. Increasing the page
size must not increase round trips. The probe propagates W3C trace context over
HTTP and counts Npgsql commands belonging to each individual request; it never
records SQL text, parameters, connection strings or tokens.

The verifier records 100 samples per operation at concurrency 1 and 4, then
reports median, p95, p99 and min/max query counts in `performance.json`. Raw
samples have a unique run directory. Latencies are diagnostics for the recorded
runner and data shape; they are not an SLA or a universal cross-machine limit.
The independently tested transport deadline, concurrency, response and body
limits remain enforced. Broader report volume tests use the existing platform
performance suite.

The four-host verifier also measures catalog list/detail, document flow/Effects,
two installed vertical reports and Trial Balance where installed. For each
operation it alternates 100 REST and 100 MCP requests at concurrency 1 and 4,
after warming both paths. `vertical-performance-budgets.json` fixes a maximum
four additional SQL commands, a p95 ratio of four to the adjacent REST baseline,
and a 1 MiB structured-response ceiling. This compares the adapter with the
same existing business path on the same host, instead of asserting a universal
millisecond SLA. The initial four-host run observed at most two additional SQL
commands and a maximum p95 ratio of 2.41. Changes to these budgets require an
explicit review of the measured baseline.

The per-host comparison artifacts include median/p95/p99, SQL maxima, payload
bytes and total managed allocations for both workloads together. Allocation
counts are diagnostic process totals, not per-request retained memory. Raw
samples and comparison files use a unique directory for every verifier run;
failed or stale samples are never imported into a later summary. The regular
backend suite exercises the functional matrix; `verify-verticals.mjs` always
enables these additional load measurements for candidate certification.

## Upgrade and release evidence

The three upgrade profiles run the existing preservation/continuation matrix
before explicitly opting the upgraded external API into MCP. The opt-in adds
the public package and host calls, uses a dedicated OAuth client for the same
existing user, then reads preserved custom data and enabled content through
MCP and checks that application tables remain unchanged. The frozen 3.2 source
and ordinary upgrade behavior stay unchanged; upgrading alone does not enable
MCP.

Unsealed local runs are diagnostic evidence. The release runner reruns these
gates against a sealed candidate and binds results to its package hashes.
Published-registry checks belong to the post-publication workflow.

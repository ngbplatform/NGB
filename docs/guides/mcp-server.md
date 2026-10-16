---
title: MCP Server
description: Enable NGB MCP, connect with an existing user access token, and use authorized business read tools.
---

# NGB MCP Server

NGB Platform 3.3.0 exposes existing authorized reads through the official MCP C#
SDK 2.2.0. It runs inside the application API host. It does not require an LLM,
another application account, a vector database, or another service container.

## Quick start: local Property Management

For the existing PM Compose stack, set these values in `.env.pm`:

```dotenv
FEATURE_MCP=true
MCP_ALLOW_LOCALHOST_HTTP=true
```

Recreate the API container so it receives the new environment. For a direct
Rider/dotnet launch, `.env.pm` is not loaded automatically: set
`FeatureManagement__Mcp=true` in the API process environment and select its HTTPS
launch profile. The checked-in Development settings already permit localhost HTTP
for the local Keycloak issuer and set the API base URL to `https://localhost:7071`.

Connect a Streamable HTTP MCP client to `https://localhost:7071/mcp` and send the
existing user's API access token in the `Authorization: Bearer <access_token>` header.
Use the actual configured port if you changed `PM_API_HTTPS_PORT` or the launch
profile. Trust the development HTTPS certificate on the machine running the client.
Start with `ngb_get_capabilities` using `{}` and `ngb_list_metadata` using
`{ "limit": 10 }`.

The detailed [token instructions](#authentication-and-client-connection) and
[troubleshooting table](#troubleshooting) explain authentication and connection errors.

## Architecture

The solid arrows show the read execution path. Dashed arrows show module
contributions registered by the host. This is a runtime flow, not a project
reference graph.

<script setup>
const flowchart = String.raw`flowchart TB
    Client["MCP client"]
    Modules["Installed application modules"]

    subgraph Host["Existing application API host"]
        Transport["NGB.Mcp.AspNetCore<br/>Streamable HTTP endpoint"]
        Identity["Existing API bearer authentication<br/>Current user and active-account checks"]
        Dispatch["NGB.Mcp<br/>Tool registry, validation and dispatch"]
        Handler["Scoped read-tool handler"]
        Reads["Permission-aware application reads"]
        Runtime["Existing business services<br/>Records, reports, search, flow and Effects"]
        Persistence["Persistence contracts"]
        Provider["NGB.PostgreSql"]
    end

    Database[("PostgreSQL")]

    Client -->|Read request| Transport
    Transport --> Identity
    Identity --> Dispatch
    Dispatch --> Handler
    Handler --> Reads
    Reads --> Runtime
    Runtime --> Persistence
    Persistence --> Provider
    Provider --> Database

    Modules -.->|Definitions and reports| Runtime
    Modules -.->|Optional custom read tools| Dispatch`
</script>

<MermaidDiagram :chart="flowchart" />

The endpoint runs in the same process as the API. Its handlers call application
services directly. Database-backed reads reach the provider through existing
contracts; metadata and capability discovery can resolve from registered definitions
without a database read. Results return through the handler and MCP transport.

### Project boundaries

| Component | Responsibility |
| --- | --- |
| `NGB.Mcp` | Tool registration, typed request/result contracts, input validation, current-user dispatch and adapters over existing authorized reads. Published as `NGB.Platform.Mcp`. |
| `NGB.Mcp.AspNetCore` | Official MCP SDK integration, HTTP endpoint, shared API authentication, resource discovery, transport limits and protocol error mapping. Published as `NGB.Platform.Mcp.AspNetCore`. |
| Application abstractions and Runtime | Shared authorization boundaries, business queries, report calculations, search, document flow and Effects. |
| Persistence contracts and PostgreSQL provider | Existing read sessions, storage queries and database access. |
| Application host | Module registration, dependency injection, authentication configuration, feature flags and public endpoint settings. |

`NGB.Mcp` has no dependency on ASP.NET Core, the PostgreSQL provider or vertical
API projects. `NGB.Mcp.AspNetCore` contains transport integration; business query
implementations stay in the application services. Runtime does not depend on MCP.

## Enable an existing host

Reference `NGB.Platform.Mcp.AspNetCore` at the same exact version as the other
platform packages. The API must already compose its modules, PostgreSQL provider,
`AddInfrastructure`, `AddNgbRuntime`, `AddNgbRuntimeAuthorization` and the existing
Notes/Attachments feature configuration.

```csharp
using NGB.Mcp.AspNetCore;

builder.Services.AddNgbMcp(builder.Configuration);

var app = builder.Build();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapNgbMcp("/mcp");
app.Run();
```

PM, Trade, Agency Billing, CRM and the external starter include this opt-in
composition. `Mcp` is a registered deployment feature, disabled by default.
With no `FeatureManagement:Mcp=true`, the endpoint, OAuth metadata and MCP server
infrastructure are not registered. `/api/features` reports its startup state.
The flag is independent of user permissions; administrators cannot bypass it.
Changes require recreating/restarting the API with the updated configuration.
The former `Mcp:Enabled` key is rejected with a migration message; remove it and
use the single registered feature flag.

Example production configuration:

```json
{
  "Ngb": {
    "Hosting": {
      "PublicApiUrl": "https://api.example.com"
    }
  },
  "FeatureManagement": {
    "Mcp": true
  },
  "Mcp": {
    "AllowedOrigins": ["https://app.example.com"],
    "PublicUiOrigin": "https://app.example.com",
    "UiRoutes": {
      "catalog": "/catalogs/{typeCode}/{id}",
      "document": "/documents/{typeCode}/{id}",
      "report": "/reports/{typeCode}"
    }
  }
}
```

`Ngb:Hosting:PublicApiUrl` is the shared canonical external API base URL. MCP runs
on the same API server and appends the literal route passed to `MapNgbMcp`
(`/mcp` by default). The example therefore publishes `https://api.example.com/mcp`.
This setting does not open a listener or configure Kestrel. Never substitute an
internal container URL, wildcard bind address or unvalidated request Host header.

`Mcp:Resource` is an optional full endpoint override for an independently exposed
MCP address. It takes precedence over the shared API URL and must have exactly
the path passed to `MapNgbMcp`, preserving existing explicit configurations.
When MCP is enabled, a valid shared URL or explicit override is required at
startup. Missing settings are not inferred from requests. Call `MapNgbMcp`
before starting the host or resolving MCP authentication options.

`Mcp:AuthenticationScheme` names the host's registered API JWT bearer scheme and
defaults to `Bearer`. MCP forwards authentication to that existing handler,
including its issuer, signing keys, audience/client validation, claim mapping,
token handlers and events (including scoped `EventsType`). There is no second
MCP audience or token validator. `Mcp:Audience` is obsolete and rejected with
migration guidance; remove it. `Resource` identifies the public MCP endpoint
for discovery and transport validation, not a separate JWT audience.

Startup rejects missing/non-JWT schemes, disabled issuer/audience/signature or
lifetime checks, unsigned/non-expiring-token configurations and authentication
forwarding from the selected bearer handler. MCP accepts bearer headers and
rejects query-string access tokens. The API's existing Keycloak audience
validator also recognizes its explicitly configured clients through `azp` or
`client_id`; MCP inherits this application trust policy. Those claims are client
identifiers, not a new MCP-specific audience guarantee. Any future tightening
of that policy belongs in the shared API authentication configuration.

`Mcp:AllowedOrigins` is an exact allowlist of browser origins, not an OAuth client
list. The endpoint's own origin is allowed automatically; other origins must be
configured without a trailing slash or path. A desktop client that sends no
`Origin` header does not require an entry. `Mcp:PublicUiOrigin` only builds business
links and does not add a browser origin to the allowlist.

For local development only, `AllowLocalhostHttp=true` permits HTTP on loopback
addresses, `localhost` and subdomains such as `pm-keycloak.localhost` for the API,
Keycloak issuer and UI origin. These checks use the reserved name boundary,
without DNS resolution; names such as `localhost.example.com` are rejected.
Production uses HTTPS with this override left false.

The four local Compose stacks already derive the shared API URL from their
published HTTPS port. Their `.env` files explicitly set `FEATURE_MCP=true` and
`MCP_ALLOW_LOCALHOST_HTTP=true` for local use. The Compose defaults are false
when these keys are absent. Rebuild/recreate the API service after changing them.
PM uses `https://localhost:7071/mcp`;
`PM_API_PUBLIC_URL` optionally overrides its shared API base URL. The equivalents
are `TRADE_API_PUBLIC_URL`, `AB_API_PUBLIC_URL` and `CRM_API_PUBLIC_URL`. For a
direct Rider/dotnet launch, the Development appsettings contain the HTTPS profile's
base URL; set `FeatureManagement:Mcp=true`. Select the matching HTTPS launch profile, or
override the public URL for an HTTP-only launch. MCP stays disabled by default.

Behind a proxy, run ASP.NET Core forwarded-header middleware **before** HTTPS
redirection and authentication, accepting headers only from configured
`KnownProxies`/`KnownIPNetworks` and approved `AllowedHosts`. The MCP address and API token-validation policy
remain fixed by configuration; forwarded headers only restore the
request's external scheme and host for validation. Configure the proxy to
overwrite client-supplied forwarded headers. See the
[ASP.NET Core proxy configuration guide](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0).

A base URL with a path prefix, such as `https://api.example.com/ngb`, resolves
to `/ngb/mcp`. Preserve that prefix as `Request.PathBase` (for example through
`UsePathBase("/ngb")` before routing). Route the public OAuth metadata path
`/.well-known/oauth-protected-resource/ngb/mcp` to the same API without stripping
its well-known path. The endpoint validates the effective request path as well
as its host. The shared public address does not automatically trust a proxy or
rewrite incoming paths.

UI routes are optional. Configure only routes that the hosting UI implements.
PM, Trade and Agency Billing also support
`generalJournalEntry: /accounting/general-journal-entries/{id}`. An empty host
can omit all UI configuration; its source URLs are `null`. The resolver never
uses the request Host header to construct a link.

## Authentication and client connection

Use the application's existing realm, client and users. The access token's subject
resolves through the normal NGB current-user and permission services.
Reuse a normal interactive user access token
accepted by this API, subject to the user-token requirements below. No new Keycloak
user, client, scope or MCP audience mapper is required to reuse that token.

The client connects using:

- **Transport:** Streamable HTTP.
- **Endpoint:** the configured public API URL with the mapped MCP route.
- **Authentication:** `Authorization: Bearer <access_token>` on protected requests.

Use an access token from the existing interactive login flow. ID tokens, refresh
tokens and service tokens are not accepted. The client obtains or refreshes tokens
through the authorization server; MCP does not issue or refresh them.

The token must contain its user subject, Keycloak's `typ=Bearer` access-token
claim and positive `auth_time`. Client-credentials and service-account tokens
are rejected. Invalid credentials receive `401` with resource discovery;
authenticated non-user tokens and inactive users receive `403`.

Interactive OAuth requires an existing client registration that
allows the actual callback URI and Authorization Code with PKCE S256. Reusing
an API token does not require changing those settings. Automatic OAuth setup
in an MCP client depends on that client and the realm's registration
capabilities; discovery alone cannot make every callback or client valid.
See the [Keycloak MCP authorization guide](https://www.keycloak.org/securing-apps/mcp-authz-server).

The SDK publishes `/.well-known/oauth-protected-resource/mcp` and includes its
URI in unauthenticated bearer challenges. Resource metadata is public. Tool
discovery and execution require an authenticated active NGB user. NGB's existing
administrator semantics, including trusted `ngb-admin` without a platform-user
record, remain effective. An inactive linked platform account remains blocked.

Application role and active-state checks use the same permission snapshots and
access-version invalidation as REST. Keycloak logout or account disablement alone
does not imply immediate revocation of an already-issued JWT at this endpoint;
the shared API token-validation policy and token lifetime still apply.

## Tools

All tools advertise read-only, non-destructive operations. Authorization is
enforced by the existing secured services on every request and continuation.
The registry contains 18 built-ins, but `tools/list` is caller-dependent:
Notes/Attachments tools require their enabled feature and Read permission, and
content summary is absent when both features are disabled. Generic tools can
remain visible even when the caller has no matching readable records or reports.

| Tool | Data and authorization boundary |
| --- | --- |
| `ngb_get_capabilities` | Version, caller-visible categories and available tool names. |
| `ngb_list_metadata` | Paged authorized catalog, document/GJE and report summaries. |
| `ngb_get_metadata` | Authorized definition; document write-action descriptions are removed. |
| `ngb_search` | Shared secured search with explicit truncation and unavailable sources. |
| `ngb_list_records` | Existing paginated catalog/document/GJE reads; current filters/cursors. |
| `ngb_get_record` | Authorized canonical record with bounded detail materialization. |
| `ngb_get_document_flow` | Root View + ViewFlow; only permitted reachable nodes and edges. |
| `ngb_get_document_effects` | Root View + ViewEffects; existing current-effect computation. |
| `ngb_list_reports` | Existing View/Execute and admin-backed report visibility. |
| `ngb_get_report_definition` | The same authorized report-definition boundary as REST. |
| `ngb_execute_report` | Execute permission, shared report admission and paginated execution. |
| `ngb_get_audit_history` | Existing entity audit and field-redaction policy. |
| `ngb_get_work_items` | Current user's summary/items; no read/dismiss/approval mutation. |
| `ngb_get_content_summary` | Enabled, parent-authorized Notes/Attachments counts. |
| `ngb_list_notes` | Notes flag, Notes Read and existing parent-object access. |
| `ngb_list_attachments` | Attachments flag, Attachments Read and parent access; Ready metadata only. |
| `search` | Compatibility search returning `results` with `id`, `title`, `url`. |
| `fetch` | Reauthorized retrieval of a canonical source ID; a report hit returns its definition. |

There are no arbitrary register, SQL, URL-fetch, file-download, export-job,
posting or administrative write tools. Accounting, Operational Register and
Reference Register data are available through installed reports and document
Effects. Custom registered definitions automatically appear in generic tools.

The normal result envelope contains `schemaVersion`, typed `data`, and
`generatedAtUtc`. The compatibility tools return their specified root objects.
Both structured content and equivalent JSON text are included. Missing source
URLs remain `null`; citation-oriented clients may require a configured UI route.

### First calls

Supply these JSON objects as tool arguments after connecting. Discover codes and
IDs from the running host instead of guessing that a type/report is installed.

| Tool | Arguments | Next step |
| --- | --- | --- |
| `ngb_get_capabilities` | `{}` | Inspect `data.version`, `data.categories` and `data.tools`. |
| `ngb_list_metadata` | `{ "kind": "catalog", "limit": 10 }` | Select a returned `typeCode`; continue with `offset` when `hasMore` is true. |
| `ngb_list_records` | `{ "kind": "catalog", "typeCode": "pm.party", "limit": 10 }` | PM example: use a returned record ID or the returned cursor. |
| `ngb_list_reports` | `{ "limit": 10 }` | Select an authorized `reportCode`. |
| `ngb_get_report_definition` | `{ "reportCode": "<returned reportCode>" }` | Read the required parameters, filters and layout before execution. |
| `ngb_execute_report` | `{ "reportCode": "<returned reportCode>", "request": { "limit": 50 } }` | Add the definition's required parameters/filters; preserve them on continuation. |

`pm.party` is a PM catalog, not a universal platform type. An empty starter and a
custom application expose their own installed definitions. Replace the report
placeholder before calling; a report may require additional parameters.

An empty metadata page has this `structuredContent` shape:

```json
{
  "schemaVersion": 1,
  "data": {
    "items": [],
    "offset": 0,
    "limit": 10,
    "hasMore": false
  },
  "generatedAtUtc": "2026-10-16T12:00:00Z"
}
```

This illustrates a page with no matching permitted definitions, not a promise
that every starter has no platform metadata. Record lists use the relevant
`data.catalogs`, `data.documents` or `data.generalJournalEntries` page. Use
`generalJournalEntry` with the canonical type code returned by metadata for GJE.
Catalog/document filters reuse the existing read contract; GJE lists do not
accept a nonempty `filters` dictionary. No generic record `sort` argument exists.

## Bounds and continuation

| Operation | Default / maximum |
| --- | --- |
| Metadata, reports and record lists | 50 / 100 items; metadata and report-list offset at most 10,000. |
| Search | 20 / 50 results; 256 query characters; existing bounded type fan-out. |
| Report execution | Explicit existing request, at most 200 rows; paging required, offset zero. |
| Document flow | Depth 2 / 5; nodes 100 / 200. |
| Effects | 50 / 100 per section. |
| Notes, Attachments and audit | 50 / 100 items. |
| Work items | 30 / 100 items; `summaryOnly=true` returns the personal summary instead. |
| Record details | Default budget: 1,000 physical rows and 256 KiB of storage JSON before materialization. |
| Tool input / HTTP body | Default budgets: 65,536 / 131,072 bytes; both configurable. |
| Successful `tools/call` result | Default budget: 1 MiB including both representations, excluding the outer JSON-RPC envelope. |
| Request budget | Default: 24 concurrent requests, 120-token bucket replenished each minute, 45-second deadline per instance. |

The `Mcp:Reads` section configures validated tool and record limits. The outer
`Mcp` section configures request concurrency, rate, timeout and body size. These
are per-instance budgets; use the deployment's normal edge controls across
replicas. Reports additionally acquire the existing singleton REST report budget.

Read configuration separates deployment budgets from supported serialization
constraints. The defaults are starting budgets, not business limits or MCP
protocol requirements; tune them for the deployment's capacity and workload.

| Setting | Default | Validation and meaning |
| --- | --- | --- |
| `Mcp:Reads:MaxInputBytes` | 64 KiB | Positive UTF-8 argument budget; excludes the JSON-RPC envelope. |
| `Mcp:Reads:MaxResponseBytes` | 1 MiB | Positive successful-result budget; counts structured content and JSON text. |
| `Mcp:Reads:MaxRecordRows` | 1,000 | Positive aggregate count of physical record rows before materialization. |
| `Mcp:Reads:MaxRecordBytes` | 256 KiB | Positive aggregate storage JSON byte budget before materialization. |
| `Mcp:Reads:MaxTools` | 100 | Positive registration budget; startup rejects the actual catalog if built-ins plus contributors exceed it. |
| `Mcp:Reads:MaxJsonDepth` | 32 | 1–32 nested argument containers; the root object counts as one. The ceiling matches the shared serializer. |
| `Mcp:MaxRequestBytes` | 128 KiB | Positive whole HTTP body budget; buffered incrementally and checked for both known-length and chunked requests. |
| `Mcp:ConcurrentRequests` | 24 | 1–256 simultaneous requests per API instance; no waiting queue. |
| `Mcp:RequestsPerMinute` | 120 | 1–100,000 tokens in the per-instance bucket; replenished once per minute, with no waiting queue. |
| `Mcp:RequestTimeoutSeconds` | 45 | 1–300 seconds; cancellation propagates to the read operation. |

Positive budgets use the existing `Int32` configuration types. There is no extra
10,000-row, 4-MiB record/response or 250-tool ceiling. The depth ceiling is named
in `McpReadOptions.MaximumSupportedJsonDepth` and reused by JSON validation and
serialization; the HTTP parser allows the additional JSON-RPC wrapper levels.
JSON validation also retains named work limits of 1,024 members/items per
object/collection and 16,384 characters per string.

All applicable budgets must pass. Allow room for the JSON-RPC envelope when
configuring the HTTP body budget relative to the argument budget. A larger
argument budget never overrides the HTTP body, proxy or host limits. Storage
JSON and a tool's result DTO have different shapes, so the record and response
byte budgets are independent; neither is a byte-for-byte prediction of the
other. Serialization reserves room for both result representations and checks
the complete successful result again after escaping. A budget too small for a
result returns a bounded `result_too_large` error; error responses have their own
fixed diagnostic bounds and are not constrained by the successful-result budget.

Zero and negative budgets fail startup with the option name. The tool budget is
checked during registration before serving, independently of caller visibility
and disabled content features. Business paging, report, search and authorization
rules remain owned by the existing NGB services.

Record-size checks include the base row, typed head and tabular parts. PostgreSQL
performs the size check and the existing detail read in one read-only,
repeatable-read transaction. Oversized details return `result_too_large`; use a
record list or an appropriate paginated report. An alternative persistence
provider must implement the public `IRecordReadScopeFactory` contract; startup
fails if required services are missing.

Reuse the returned cursor with the same query. Cursors grant no permissions.
Reports preserve their existing per-page snapshot semantics; later pages can
observe newer committed data. `generatedAtUtc` is a response timestamp, not an
immutable multi-request snapshot. Configure the existing report signing key
consistently across replicas.

Effects are reconstructed **current effects**. Their identifiers are result-local.
If a section fills its limit, completeness is `unknown_at_limit`, without an
invented continuation token. Zero effects for a posted document are valid.
Existing Trade and CRM handlers may recompute `updated_at_utc` when reconstructing
reference-register effects. It describes that computation and can differ between
two reads of an unchanged document; MCP preserves the same behavior as REST.

`ngb_search` accepts optional `kind` (`catalog`, `document`, `generalJournalEntry`,
`report`) and canonical `typeCode` filters. Filtering occurs within authorized
metadata before the 50-type lookup bound, so a specific permitted type remains
searchable even when the installation exceeds that bound. `document` excludes
general journal entries; omit `kind` to search all supported kinds. Unknown and
inaccessible types both return no matches. A failing source is reported in
`unavailableSources` and sets `truncated`.
The compatibility `search` tool returns a source error instead of representing a
failed source as an empty complete search.

## Custom read contributors

Reference `NGB.Platform.Mcp` from the trusted installed module. Implement a typed
handler and contributor, register the handler as scoped and register the contributor
with `AddNgbMcpContributor<TContributor>()`. A compiling package-only example is
[`CurrentUserContributor.cs`](https://github.com/ngbplatform/NGB/blob/main/quality/mcp/external/CurrentUserContributor.cs).

```csharp
using NGB.Mcp;

builder.Services.AddScoped<CurrentUserRead>();
builder.Services.AddNgbMcpContributor<CurrentUserContributor>();
```

Custom names must match `[a-z][a-z0-9_]{2,63}` and cannot use `ngb_`, `search` or
`fetch`. Registration requires explicit existing registered NGB permission keys.
Duplicate names, untyped input schemas, missing handler dependencies and unknown
permissions fail startup. Inputs reject unknown/duplicate properties and integer
enum values. Use typed request properties with validation attributes.

The immutable registry stores descriptions, not handlers, principals or results.
Handlers resolve in the current request scope. Registration permissions control
discovery and invocation; the handler must also enforce parent/resource access
through secured application services. Feature availability can be supplied by
`IsAvailableAsync` and is rechecked for every request.

A contributor is trusted installed .NET code. A read-only annotation cannot
sandbox arbitrary code or prevent it from invoking an injected writer. Keep
handlers side-effect-free, use read-only contracts, support cancellation and
verify persistent-state postconditions. Never register a generic service locator
or dispatch arbitrary operation names from input.

## Local verification and interoperability

Connect a Streamable HTTP MCP client using the configured resource URI and the
existing user's access token. Discover tools, then call a bounded read such as
`ngb_list_metadata` with `{ "limit": 10 }`. No model or LLM key is needed.
Requests have no server session affinity; repeated reads can observe changed data.

Clients discover the available tools and their input/output schemas through MCP.
MCP serves users of the installed application. It does not package or expose platform
documentation, developer guides or a documentation search service. Business
metadata and report definitions describe the records and report parameters
available to the current user.

A Keycloak bootstrap administrator can read permitted business data without a
platform user row. The existing personal Work Center still requires a linked
platform user. MCP reports that read as unavailable until normal user
provisioning establishes the link; reading never creates it.

The protocol tests exercise the official C# SDK with `2026-07-28` discovery and
`2025-11-25` compatibility negotiation. The separate external test exercises
real Keycloak Authorization Code + PKCE, PostgreSQL, Kestrel and a custom module
restored exclusively from NuGet packages:

```sh
dotnet test NGB.Mcp.Tests/NGB.Mcp.Tests.csproj -m:1 -nr:false
dotnet test NGB.Mcp.AspNetCore.IntegrationTests/NGB.Mcp.AspNetCore.IntegrationTests.csproj -m:1 -nr:false
node quality/mcp/verify-external.mjs /absolute/path/to/candidate-feed
node quality/mcp/verify-verticals.mjs
```

`NGB.Mcp.Tests` contains unit and schema-contract tests, including direct handler,
filter and configuration checks. `NGB.Mcp.AspNetCore.IntegrationTests` starts a
real Kestrel host and exercises HTTP, authentication and official SDK clients.
`node quality/mcp/verify-contract.mjs` runs both projects as one required gate.

The external verifier creates a fresh project and package cache outside the
repository, rejects ProjectReference resolution, and records package SHA-256
hashes and TRX results under `artifacts/mcp/external`. A local feed verification
does not by itself certify a sealed candidate or a published release.

Compatibility with ChatGPT, Claude, DeepSeek-backed clients, Gemini or Grok
depends on the client application's Streamable HTTP and OAuth support. The
server has no model-specific business branches. Product-specific research and
citation compatibility must be verified separately from protocol interoperability.

## Troubleshooting

HTTP transport failures and tool failures are different. A successful HTTP exchange
can contain a `tools/call` result with `isError=true`; its text content is a bounded
JSON object with `code` and `message`. Successful results include `structuredContent`.
Unknown or removed tool names return JSON-RPC `InvalidParams` (`-32602`).

| Symptom | Check |
| --- | --- |
| `/mcp` returns `404` | Confirm `AddNgbMcp`/`MapNgbMcp` composition, `FeatureManagement:Mcp=true` at startup, the route and the API port. Recreate a Compose container after changing its environment. |
| TLS/certificate failure | Trust the local development certificate or use a correctly configured public HTTPS endpoint. `AllowLocalhostHttp` does not disable HTTPS certificate validation in the client. |
| HTTP `401` | Supply an unexpired access token from the existing application's realm/client. Inspect the bearer challenge's resource metadata URL; do not use an ID or refresh token. |
| HTTP `403` | Check the active NGB account, required user-token claims and any `Origin` header. Service accounts are rejected. Administrators cannot bypass an inactive linked account. |
| HTTP `400` | Match the configured public host, port and effective route; behind a proxy restore trusted forwarded headers and `PathBase`. Also check malformed or duplicate JSON properties. |
| HTTP `413` | Reduce the whole HTTP request body; `Mcp:MaxRequestBytes` also applies to chunked requests. |
| HTTP `429` | The per-instance rate/concurrency budget is full. Respect `Retry-After` and reduce concurrent requests. |
| HTTP `504` or connection closed after response started | The transport deadline expired. Reduce the read scope and inspect dependency latency. |
| Tool `unavailable` | The requested resource is missing or forbidden. This intentionally does not reveal whether an inaccessible record exists. |
| Tool `invalid_input` / `invalid_cursor` | Follow the advertised input schema; keep query parameters with their matching cursor. |
| Tool `result_too_large` | Request a smaller page, narrow the query or use an appropriate paginated report for an oversized record. |
| Tool `capacity_exhausted` / `timeout` | Report admission or its execution deadline was exceeded; the report budget is shared with REST. |
| Tool `internal_error` | Inspect the server's correlated log entry; the client receives no internal exception or business payload. |

The `NGB.Mcp` meter currently emits `ngb.mcp.calls` and `ngb.mcp.duration`, tagged
with `outcome` (`success`, `error`, `cancelled`), for tool calls. Transport failures
before dispatch use the host's HTTP diagnostics. Internal tool-error logs record
the exception type and trace ID, not the exception message or result data.

## Implementation anchors

| Contract | Repository source |
| --- | --- |
| Feature flag, bearer policy, CORS and endpoint composition | `NGB.Mcp.AspNetCore/DependencyInjection.cs` |
| Canonical API/resource URL and metadata address | `NGB.Mcp.AspNetCore/McpEndpointAddress.cs` |
| Transport configuration and enforcement | `NgbMcpOptions.cs`, `McpEndpointFilter.cs`, `McpRequestBudget.cs` in `NGB.Mcp.AspNetCore` |
| Tool registration, permissions and response envelope | `NGB.Mcp/McpReadContracts.cs`, `McpReadToolRegistry.cs`, `BuiltInTools.cs` |
| Input DTOs, defaults and bounds | `NGB.Mcp/Tools/ReadRequests.cs`, `NGB.Mcp/McpReadOptions.cs` |
| Error mapping, successful-result budget and diagnostics | `NGB.Mcp.AspNetCore/McpProtocolHandlers.cs` |

## Related documentation

- [Layering and Dependencies](/architecture/layering-and-dependencies)
- [Security and Permissions](/platform/security-and-permissions)
- [Report Execution Results](/architecture/report-execution-results)
- [Extend an External Application](/guides/extend-external-application)

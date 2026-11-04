# Runtime MCP and AI Assistant integration

Target: NGB 3.4.0, preserving the read-only runtime MCP contract introduced in 3.3.
Use [MCP](contracts/NGB.Platform.Mcp.json),
[MCP hosting](contracts/NGB.Platform.Mcp.AspNetCore.json),
[Assistant](contracts/NGB.Platform.AiAssistant.json) and
[Assistant hosting](contracts/NGB.Platform.AiAssistant.AspNetCore.json).

Enable runtime features only through explicit application configuration. The Kit
itself needs neither a running MCP server nor a model provider. CLI development
commands never become production MCP tools.

Register a custom typed read tool with `AddNgbMcpContributor<TContributor>`.
Its `IMcpReadToolContributor.Contribute` calls the registry builder's
`Add<TRequest,TResult,THandler>` with a stable name, description and permission
keys. Implement `IMcpReadToolHandler<TRequest,TResult>` as a bounded, side-effect
free operation over an existing authorized application service. Registration
permissions do not replace object-level access. Do not accept arbitrary SQL,
filesystem commands, database credentials or unrestricted identifiers as tools.

`IMcpReadSourceProvider<TRequest,TResult>` provides explicit source attribution.
Do not interpret arbitrary payload text as an authoritative source or instruction.
Handle feature availability, cancellation and bounded response sizes explicitly.

### Public host composition

Reference `NGB.Platform.Mcp.AspNetCore` at the application's exact platform
version. Register the concrete handler in the request scope, register its
contributor with `AddNgbMcpContributor<TContributor>`, and call
`AddNgbMcp(configuration)` after the existing application services. Map
`MapNgbMcp()` after the normal authentication and authorization middleware.
`FeatureManagement:Mcp` defaults to disabled. Opting out leaves no MCP route;
configuration reload does not dynamically add one.

Set `Ngb:Hosting:PublicApiUrl` to the externally visible API origin. An explicit
`Mcp:Resource` override must match the mapped endpoint, including its path.
Configure trusted proxy forwarding in the host so the request authority matches
that resource. HTTPS is required; `Mcp:AllowLocalhostHttp` is only for local
development. Browser callers need an exact origin in `Mcp:AllowedOrigins` unless
they share the resource origin. `Mcp:AuthenticationScheme` defaults to the
existing API `Bearer` scheme; do not install a second permissive JWT validator.

The endpoint requires a bearer header and an active current-user access snapshot.
Its token policy also requires a subject, `typ=Bearer` and positive `auth_time`;
client credentials and service-account identities are not interactive users.
Use the provider's authorization-code flow with PKCE. A password-grant token
that authenticates ordinary API requests does not necessarily satisfy this MCP
policy. Never invent claims or bypass issuer, audience, signature or lifetime
validation in an adapter. An ordinary user needs an active application record;
the existing trusted administrator path also supports an unlinked administrator.

Transport options bound the whole request: `MaxRequestBytes` defaults to 128 KiB,
`ConcurrentRequests` to 24, `RequestsPerMinute` to 120 and
`RequestTimeoutSeconds` to 45. The separate `Mcp:Reads` section defaults to
64 KiB of tool arguments, 1 MiB of serialized response, 100 registered tools,
JSON depth 32, 1,000 record rows and 256 KiB of record storage JSON. These are
deployment budgets, not permission grants. Application query limits still apply;
custom tools must use bounded reads and propagate cancellation themselves.

### Independent typed read example

The [equipment example](../examples/equipment-inspection/README.md) registers
`example_get_equipment` with a typed `{ id }` input and a catalog-view permission.
`EquipmentReadTool` delegates to `IAuthorizedCatalogReadService.GetByIdAsync`
and returns only the saved ID/name. It does not call a repository or execute SQL.
Its source provider constructs a `McpReadSource` from that authorized result.
The SDK wraps it in `McpReadResponse<T>` with schema version, generation time,
data and sources; arbitrary strings in the record never become tool instructions.
`McpReadException` represents a bounded tool error, not permission to expose
internal exception details.

The HTTP tests start the actual endpoint, PostgreSQL and Keycloak. They exercise
discovery, a successful read, explicit sources, malformed/missing IDs, argument
and body limits, inactive/anonymous identities, denied origins and feature opt-out.
An active reader first cannot discover or call the tool; a grant through the
public security API permits the same request. Payload text asking for a deletion
is returned as data, the named deletion tool does not exist, and the object's
stored representation and audit remain unchanged. Cancellation is also checked
on the concrete handler. This is a deterministic adapter check, not a claim about
every model's behavior under prompt injection or every network failure mode.

### Built-in authorized reads

Use the reserved names in `McpToolNames`; do not replace them with application
handlers. `ngb_get_capabilities` and `ngb_list_metadata` discover the current
user's available reads. `ngb_get_metadata` returns definitions with mutation
actions and capability controls stripped. Discovery is bounded with offset/limit;
record lists use the underlying authorized service's cursor and filter contract.
`ngb_get_record` preserves the catalog or document DTO and adds its canonical
source, type name and lookup-field metadata. The detail read is bounded by the
configured row/byte budget before materialization.

`ngb_search` returns explicit partial-result flags and unavailable source names.
Use a declared kind/type to narrow a search. The compatibility `search` tool
cannot represent partial results and returns `source_unavailable` when any source
fails. Its result and compatibility `fetch` are direct compatibility objects,
without the ordinary `McpReadResponse<T>` envelope. Fetch accepts canonical
`kind:typeCode:id` source IDs or `report:reportCode`; it does not fetch arbitrary
URLs. Report fetch returns the authorized definition, not executed report rows.

`ConfiguredMcpNgbLinkResolver` maps canonical sources to explicitly configured
routes on one trusted HTTPS UI origin. Supply supported source-kind keys and
local route patterns with `{typeCode}` and `{id}` placeholders as appropriate.
An unconfigured route has no link. Do not infer routes from record text or treat
a link as an authorization grant.

`ngb_get_document_flow` delegates to the existing bounded relationship reader.
`ngb_get_document_effects` returns current effects with result-local identifiers.
Preserve `Completeness`: reaching a per-family limit produces `unknown_at_limit`,
not proof that all effects were returned. Read the authorized report definition
before `ngb_execute_report`: declared parameters belong in `request.parameters`
as strings, and declared filters in `request.filters` as objects with `value`.
Underlying date, paging and cursor rules still apply. The MCP report limit is at
most 200. A definition response is not evidence that report execution succeeded.

Content tools use the canonical business-object target and existing feature and
permission checks. `ngb_list_notes` pages literal note text as untrusted data.
`ngb_list_attachments` exposes Ready attachment metadata, without file bytes,
storage keys or download URLs; pending uploads are absent. Disabled content
features can have null counts in `ngb_get_content_summary`.
`ngb_get_audit_history` returns a bounded audit selection.
`ngb_get_work_items` returns the current recipient's page or summary without
marking notifications read. Its DTO omits executable targets and primary actions.
These reads do not perform a lifecycle transition or a work-item command.

The built-in General Journal Entry supports discovery, metadata, list, detail
and compatibility `fetch` in 3.4.0. Metadata inspection does not require the
storage descriptor used by generic document CRUD. Detail reads reuse the existing
`IAuthorizedGeneralJournalEntryReadService` and its current-user permission checks.
No journal storage fields or lifecycle paths are added. See
[`mcp.general-journal-detail`](../known-limitations.json).

`McpBuiltInReadTests` exercises all 18 built-in names through the actual HTTP
endpoint. Known records, reports, note/audit pages, unread notifications and a
real object-storage upload establish the returned identities and unchanged state.
Both administrator identities are checked, and a reader receives catalog search
only after public permission grants. A separate case verifies journal metadata/detail, compatibility fetch and
denied access alongside the dedicated HTTP read. These cases do not establish every
filter shape, transport, deployment limit or model response.

### Assistant providers

Reference `NGB.Platform.AiAssistant.AspNetCore` and the chosen public provider
package at the application's exact platform version. Set both
`FeatureManagement:AiAssistant` and `FeatureManagement:Mcp` to `true`; Assistant
without the MCP feature fails configuration. Pass provider registration to the
`AddNgbAiAssistant(configuration, configureProvider)` callback. The callback runs
only when Assistant is enabled and must register an `IChatClient`. Register
exactly one provider; the SDK does not choose one from a provider-name setting.

The example's `EquipmentAssistant` adds an application-owned `AiAssistant:Provider`
selector with the exact values below. This selector is example composition, not a
new SDK setting. Unknown or missing selections fail when the feature is enabled.
The example references all four adapters to make their integration executable;
an application that supports one provider can reference and register only that one.

| Selection | Public registration | Server configuration |
| --- | --- | --- |
| `OpenAI` | `AddNgbOpenAiChatClient` | `AiAssistant:OpenAI`: required `Model` and `ApiKey`; optional HTTPS `Endpoint`, default `https://api.openai.com/v1` |
| `Anthropic` | `AddNgbAnthropicChatClient` | `AiAssistant:Anthropic`: required `Model` and `ApiKey` |
| `Bedrock` | `AddNgbBedrockChatClient` | `AiAssistant:Bedrock`: required known AWS `Region` and `Model`/inference profile; credentials use the AWS SDK chain |
| `Ollama` | `AddNgbOllamaChatClient` | `AiAssistant:Ollama`: local `Model`, `Endpoint` and bounded `ContextLength`; remote HTTP requires explicit `AllowInsecureHttp` |

Bind the selected section to its public options type. Validation runs at host
startup. Cloud endpoint URLs must not contain user information, queries or
fragments. The Ollama adapter rejects cloud models; loopback HTTP is allowed,
while remote HTTP is an explicit trusted-network choice. Prefer a workload role
for Bedrock. Supply cloud keys through server secret configuration, never browser
runtime configuration, committed application settings or CLI evidence.

Map `MapNgbAiAssistant()` after `MapNgbMcp()` and normal authentication/
authorization middleware. The mapped MCP resource must be reachable from the API.
The Assistant opens its own request-owned connection with the caller's bearer
identity; it neither creates an administrator identity nor grants permissions.
Its GET settings and POST chat endpoints are `/api/ai-assistant` and
`/api/ai-assistant/chat`. Chat streams `AiAssistantEvent` as SSE and uses
`AiAssistantRequest` with an alternating user/assistant message history ending in
a user message. Page context and historical source references remain untrusted.
Use the public shared UI integration instead of interpreting source references as
arbitrary links or rendering provider output as HTML.

`AiAssistant` also configures shared input/context/output budgets, maximum tool
calls, concurrency, rate and timeout. `InitialOutputTokens` is a per-round starting
budget bounded by `MaxOutputTokens`, not a total billing budget for an entire
conversation. `AiAssistantHttpOptions` supplies report page size and optional
localhost transport settings. Development certificate trust requires Development,
a canonical localhost endpoint and the exact existing Kestrel certificate; do
not disable TLS verification for production. Disabling the feature leaves no
Assistant endpoints and does not resolve provider credentials.

### Independent Assistant acceptance

`AssistantHttpTests` runs the actual API, Keycloak, PostgreSQL, MCP loopback,
Assistant runner and all four published provider adapters. Only vendor responses
are controlled: public named HTTP clients provide SSE/NDJSON fixtures, and Bedrock
uses an anonymous test runtime supplying native Converse stream events. No live
model service, personal AWS credential chain or paid provider request is involved.

The tests verify a real authorized catalog read, explicit sources, unchanged data
and audit, provider token/context budgets, public settings without credentials,
and generic failure responses without provider diagnostics. A reader cannot see
or invoke the equipment tool until granted view permission through the public
security API; anonymous and inactive accounts remain blocked. Oversized or invalid
requests do not reach the provider. Concurrent work is bounded, and both a timeout
and client disconnect cancel the pending provider transport. Configuration tests
verify invalid selected options and cancellation on every native adapter.

These are deterministic integration checks, not live vendor compatibility probes,
model-answer quality evaluations or proof that prompt injection is impossible.
The independent real-agent development evaluations have their own evidence.

Verify opt-out, absent dependency, invalid settings, anonymous/unauthorized access,
disabled capability, malicious content, cancellation and response bounds.
An unsupported business request remains unsupported: do not add platform business
operations, relax permissions or make MCP writable to satisfy an assistant task.

---
title: AI Assistant
description: Enable the shared business assistant with native Ollama or a replaceable cloud provider, using the current user's MCP permissions.
---

# AI Assistant

AI Assistant is the right-hand conversation panel in the shared application shell.
It finds business records, reads details and runs reports through
[NGB MCP Server](/guides/mcp-server). PM, Trade, Agency Billing, CRM and the starter
application use the same implementation. It is disabled by default.

The developer selects one provider and model in the API composition. Users do not
enter provider keys or configure connections. There is no conversation database.
The panel, loading indicator, resizing and conversation behavior belong to
`@ngbplatform/ui`; verticals supply page context and existing business capabilities.
They do not maintain separate assistant interfaces.

## Enable native Ollama

Install Ollama natively on the host. On Apple Silicon it uses Metal. There is no
Ollama Compose service, Docker profile or Docker Model Runner configuration.
The default model is `qwen3.5:9b`, with a 32,768-token context. The provider keeps
the model's default thinking mode and preserves reasoning between tool calls;
reasoning is never shown in the chat.
The Ollama adapter defaults to temperature `0` for more stable tool arguments;
an explicit `ChatOptions.Temperature` supplied by the application takes precedence.

Start the server in one terminal:

```sh
OLLAMA_NO_CLOUD=1 ollama serve
```

Download the model in another terminal:

```sh
ollama pull qwen3.5:9b
```

If the native application already manages the server, configure that instance
instead of starting another listener. Keep the endpoint private. The browser
never connects directly to Ollama and needs no Ollama CORS configuration.

For Property Management, set these entries in `.env.pm`:

```dotenv
FEATURE_MCP=true
FEATURE_AI_ASSISTANT=true
MCP_ALLOW_LOCALHOST_HTTP=true
AI_OLLAMA_ENDPOINT=http://host.docker.internal:11434
AI_OLLAMA_ALLOW_INSECURE_HTTP=true
AI_OLLAMA_MODEL=qwen3.5:9b
AI_OLLAMA_CONTEXT_LENGTH=32768
```

Rebuild/recreate the API and use the updated UI:

```sh
docker compose --env-file .env.pm -f docker-compose.pm.yml up -d --build ngb.pm.api
```

The corresponding keys exist in `.env.trade`, `.env.ab` and `.env.crm`. All four
keep `FEATURE_AI_ASSISTANT=false` in the repository. Docker Desktop reaches the
native Mac listener at `host.docker.internal`; an API running directly on the host
uses `http://localhost:11434`. Other environments must provide an explicit reachable
endpoint; a container's `localhost` is the container itself.

`AiAssistant:Ollama:AllowInsecureHttp` defaults to `false`. HTTPS and loopback HTTP
endpoints work without this opt-in. Any other HTTP endpoint, including
`host.docker.internal`, requires explicit permission. The local Compose `.env`
files set `AI_OLLAMA_ALLOW_INSECURE_HTTP=true` for the API-to-host connection.
Use HTTPS and leave this option disabled for production network connections.
The opt-in permits plaintext HTTP to the configured endpoint; it does not permit
credentials, query strings or fragments in endpoint URLs.

For an IDE/dotnet API process, Compose `.env` files are not loaded automatically.
Set `FeatureManagement__Mcp=true` and `FeatureManagement__AiAssistant=true` in its
environment. The default Ollama endpoint already targets native localhost. Use
the existing HTTPS API profile and trusted development certificate.

Reload the application after changing flags. Open **AI Assistant** with the **?**
button in the top bar and ask “What reports can I run?” In PM, you can also ask
“Find properties in Hoboken.” The model discovers the installed vertical through
MCP metadata. For CRM or another package consumer, first prepare the matching
[UI and API packages](#update-ui-and-api-together).

## Request flow and permissions

<script setup>
const flowchart = String.raw`flowchart LR
    User["Current application user"] --> Panel["Shared right sidebar"]
    Panel -->|"Bearer token + question"| Assistant["API: bounded assistant turn"]
    Assistant <-->|"Messages, tool schemas and results"| Model["Configured IChatClient provider"]
    Assistant -->|"MCP SDK + same user token"| Mcp["Existing NGB MCP endpoint"]
    Mcp --> Reads["Existing authorized business reads"]
    Reads --> Mcp
    Mcp --> Assistant
    Assistant -->|"Streaming answer + verified sources"| Panel`
</script>

<MermaidDiagram :chart="flowchart" />

The API uses the official MCP client over Streamable HTTP. It discovers the
current user's read tools and executes requested tools sequentially. Business
logic and permission checks remain in the existing application services called by MCP.
For counts, balances and totals, the assistant resolves the required report
filters and executes the report. Search results identify records; their length
is not a business total. Unspecified optional parameters use the report's own
defaults. A date embedded in a record name does not select the reporting date.
The provider never receives the user's bearer token, database credentials or a
browser-supplied MCP URL. The MCP address comes from the host's existing canonical
endpoint configuration; redirects are not followed.

Assistant requires the same interactive-user policy as MCP and an active account.
Administrator permissions retain the existing NGB semantics; no extra grants or
Keycloak clients are introduced. Each MCP call performs normal authorization
checks. Previous answers are context, not authorization to read data again.

Model instructions and tool results are not security boundaries. Only registered
read tools are exposed. Reports perform business calculations. The assistant has
no SQL access, arbitrary register access, write tools or documentation RAG service.

Vertical extensions can expose existing authorized reads through
[MCP contributors](/guides/mcp-server#custom-read-contributors). The assistant
discovers these tools through the same registry. An extension does not create
missing business capabilities: a scope, filter, balance or subtotal unsupported
by the existing services remains unavailable. Supporting it in the business layer
is a separate application change.

## Feature flags and conversation behavior

`FeatureManagement:AiAssistant` requires `FeatureManagement:Mcp=true`.
An invalid combination fails startup. MCP can remain enabled without Assistant.
With Assistant disabled, its provider callback is not run and its endpoints are
not mapped. The shared UI hides the button. Flags are deployment settings,
independent of permissions, and are frozen until API restart.

The **?** button toggles the panel open and closed. The desktop panel leaves the
application interactive; drag its left edge to resize it, or focus that separator
and use the arrow keys, Home or End. On smaller screens it uses the shared
full-screen modal drawer. Three dots indicate activity; animation respects the
browser's reduced-motion preference. **Stop** cancels generation, **Retry** repeats
a failed turn, and **New chat** clears the conversation. Closing the panel or
navigating preserves the current conversation.

Conversation state exists only in browser memory. NGB does not save it in a
database, local storage or session storage. Reload, sign-out, identity change or
disabling the feature clears it. Only bounded recent completed text exchanges
are sent on the next turn; tool results are fetched again. Cloud-provider retention
is governed separately by the provider and the deployment's account settings.

**Focus on …** supplies kind/type/record identifiers and, for reports, the selected
parameters and filters. The API validates these as untrusted request context; it
does not upload the screen or all loaded rows. The user's explicit question takes
precedence over page context. The browser supplies its time zone; the server clock
provides the current date for questions such as “today” or “this month”.

Answers support Markdown tables. UTC/offset timestamps are displayed in the
browser's time zone as `MM/DD/YYYY, hh:mm:ss AM/PM`; date-only values retain their
calendar date. Links requested in an answer use verified MCP sources and the
application's navigation configuration. Discovered sources are not appended as
buttons. Raw HTML, remote images and model-invented links are not rendered as
active content.

## API composition and providers

The common contract is `Microsoft.Extensions.AI.IChatClient`. Reference the same
platform package version as the application and register one provider:

```csharp
using NGB.AiAssistant.AspNetCore;
using NGB.AiAssistant.Ollama;
using NGB.Mcp.AspNetCore;

builder.Services.AddNgbMcp(builder.Configuration);
builder.Services.AddNgbAiAssistant(builder.Configuration, services =>
    services.AddNgbOllamaChatClient(
        builder.Configuration.GetSection(OllamaAssistantOptions.SectionName).Bind));

// After the host's existing CORS, authentication and authorization middleware:
app.MapNgbMcp();
app.MapNgbAiAssistant();
```

| Package | Registration | Configuration section |
| --- | --- | --- |
| `NGB.Platform.AiAssistant.Ollama` | `AddNgbOllamaChatClient` | `AiAssistant:Ollama`: `Endpoint`, `AllowInsecureHttp`, `Model`, `ContextLength` |
| `NGB.Platform.AiAssistant.OpenAI` | `AddNgbOpenAiChatClient` | `AiAssistant:OpenAI`: `Endpoint`, `Model`, `ApiKey` |
| `NGB.Platform.AiAssistant.Anthropic` | `AddNgbAnthropicChatClient` | `AiAssistant:Anthropic`: `Model`, `ApiKey` |
| `NGB.Platform.AiAssistant.Bedrock` | `AddNgbBedrockChatClient` | `AiAssistant:Bedrock`: `Region`, `Model` |

For example, replace the Ollama registration with:

```csharp
using NGB.AiAssistant.OpenAI;

builder.Services.AddNgbAiAssistant(builder.Configuration, services =>
    services.AddNgbOpenAiChatClient(
        builder.Configuration.GetSection(OpenAiAssistantOptions.SectionName).Bind));
```

The OpenAI adapter uses Chat Completions. Other compatible endpoints require
streaming and function calling; verify the actual endpoint/model with the adapter.
Anthropic uses its native SDK. Cloud keys stay in server secret configuration.
There is no automatic cross-provider fallback that sends data elsewhere.

### Amazon Bedrock

Reference `NGB.Platform.AiAssistant.Bedrock` in a package-consuming API, or
`NGB.AiAssistant.Bedrock` as a project reference in a source-based host. Replace
the Ollama callback with:

```csharp
using NGB.AiAssistant.Bedrock;

builder.Services.AddNgbAiAssistant(builder.Configuration, services =>
    services.AddNgbBedrockChatClient(
        builder.Configuration.GetSection(BedrockAssistantOptions.SectionName).Bind));
```

Set `AiAssistant__Bedrock__Region` and `AiAssistant__Bedrock__Model` in the API
environment. `Model` identifies the model or inference profile available to your
deployment. Bedrock uses Converse streaming and the AWS SDK credential chain,
including workload roles. Make the credentials available to the API process;
host credentials are not automatically available inside a container. Do not put
AWS credentials in browser configuration or commit them to the repository.

All four checked-in API hosts and the starter currently register Ollama. Adding
Bedrock environment variables alone does not change that registration. When
changing providers, update the API dependency and composition, include any new
project references in the Docker restore stage, pass the provider settings into
the API container and rebuild that API. The Compose files currently forward the
Ollama settings. The shared assistant UI requires no provider-specific changes.

### Custom provider and HTTP contract

A different implementation can register a scoped `IChatClient` in the callback.
It must preserve function-call identifiers/results, report a terminal finish reason,
support streaming and cancellation, and honor the output budget. Provider implementations belong in
infrastructure. There is no database migration or provider-selection UI.

`NGB.AiAssistant` contains the application runner and contracts.
`NGB.AiAssistant.AspNetCore` contains HTTP, admission and MCP client integration.
Provider packages contain only model integration. The UI calls authenticated
`GET /api/ai-assistant` for public model information and limits, then
`POST /api/ai-assistant/chat` for SSE. The POST accepts user/assistant text and
optional page context and time zone. Callers cannot submit system/tool messages, schemas,
tool results, credentials or provider endpoints.

## Limits and deployment

These settings belong to `AiAssistant` and are validated at startup:

| Setting | Default | Purpose |
| --- | --- | --- |
| `MaxInputBytes` | 32 KiB | Actual HTTP request size, including chunked bodies |
| `MaxMessages` / `MaxMessageCharacters` | 20 / 8,000 | Submitted conversation |
| `MaxContextBytes` | 64 KiB | Conversation and tool schemas before each model round |
| `MaxToolResultBytes` | 16 KiB | Oversized results are explicitly omitted; the model must narrow the read |
| `MaxToolCalls` / `MaxTools` | 12 / 40 | Calls per turn / discovered tool catalog |
| `InitialOutputTokens` | 2,048 | Initial per-round budget, capped by `MaxOutputTokens` |
| `MaxOutputTokens` | 8,192 | Hard per-round ceiling for final answers and output-limit recovery |
| `MaxOutputCharacters` | 16,384 | Maximum visible answer size |
| `ReportPageSize` | 10 | Initial detailed report page size; valid range is 1 through the shared MCP report maximum (200) |
| `RequestTimeoutSeconds` | 180 | Entire request, including MCP and model calls |
| `ConcurrentRequests` / `RequestsPerMinute` | 4 / 30 | Per-instance admission; one active request per user per instance |

The byte budget is not a tokenizer. Configure a model context large enough for
the selected limits and validate representative questions. Keep report/MCP limits
in place; Assistant does not bypass them. Rate limits are per API process. Shared
cost budgets across replicas also require gateway/provider enforcement.

The token budgets are deployment defaults, not model capabilities or business
constants. Set them for the selected model, answer sizes and cost budget. An
explicitly configured `MaxOutputTokens` remains the hard ceiling, including when
it is smaller than `InitialOutputTokens`. When the runner finalizes from retained
evidence, it uses the full ceiling. A provider output-limit stop discards the
partial draft and permits one recovery with that ceiling; reasoning can also be
disabled for this recovery if it consumed the response. An unchanged exhausted
ceiling is not retried. Exhaustion emits `response_limit`, never a partial answer
or `done`. All attempts remain inside the original request deadline and respond
to cancellation. Logs record budgets and finish reasons without response text.

Detailed report pages start at `ReportPageSize` and shrink against the actual
tool-result byte budget. Page reductions consume the same `MaxToolCalls` budget.
The assistant's `resultView=totals` selects only existing report total/subtotal
rows and starts at the MCP report maximum of 200 when no limit is supplied.
It is an assistant projection option, not an extra argument to the public MCP
tool or business service. The model follows continuations within the turn's
budgets when needed to obtain the requested evidence.

Report continuation handles retain the selection within the current turn. The
model supplies `reportCode` and `request.cursor`, optionally reducing the page
size; changing criteria requires a new read without a cursor. External MCP
clients use the [public continuation contract](/guides/mcp-server#bounds-and-continuation).
These controls do not change report calculations, filters or application paging
limits. A detailed Aging result can contain all charge rows and a grand total
without containing monetary subtotals for each bucket. The assistant must state
that limitation instead of inventing or calculating the missing subtotals. If
the read remains incomplete, it must not present a sample as a complete total.

HTTPS validation remains enabled. Local Compose maps the public localhost MCP
port to Kestrel's internal port with `AiAssistant:McpLoopbackPort=443`, preserving
the canonical Host and TLS name. `TrustDevelopmentServerCertificate=true` permits
only the API's configured, unexpired Kestrel certificate with a matching server
name, and only in Development with a canonical localhost MCP address. It does not
trust arbitrary certificates. These options are false/unset by default and are
not production settings. Production uses its reachable canonical MCP HTTPS URL
and trusted certificate chain; configure forwarding as in the MCP guide.

Configure proxies to stream `text/event-stream` without buffering and allow the
request timeout. Heartbeats continue while the model is busy. Disconnect and Stop
cancel downstream work. Failed streams emit `error`, never a successful `done`;
partial text is marked incomplete. Before streaming, malformed requests receive
`400`, oversized bodies `413`, invalid media types `415`, capacity exhaustion
`429`, and dependency failures `503`.

The `NGB.AiAssistant` meter exposes `ngb.ai_assistant.requests` by outcome and
`ngb.ai_assistant.duration` in seconds. Failure logs contain exception type and
trace identifier, not prompts, answers, tool data or keys. Do not enable SDK
payload or proxy body logging for business conversations.

## Update UI and API together

PM, Agency Billing and Trade use platform project/workspace references. CRM
consumes the packaged platform through NuGet and the local npm archive. A new
shared UI build does not update the backend packages used by CRM, and a Compose
image rebuild does not recreate those packages.

For unpublished platform changes, run the repository's complete
[local package preparation](/start-here/run-locally#prepare-local-platform-packages)
from the same checkout: npm packing first, then
`bash packaging/nuget/pack-platform.sh`. The latter refreshes the local NuGet
feed, invalidates replaced versions in the repository-local cache and restores
package consumers. Then rebuild/recreate the CRM services and reload the browser.
For released applications, upgrade the UI and API using the matching published
platform package set and regenerated lockfiles.

Do not use a displayed version alone to establish that unpublished packages are
current: local archives with the same version can contain different code. Compare
the actual artifacts when diagnosing a mismatch. The chat endpoint rejects
unknown JSON members. For example, a UI that sends `timeZone` to an older API
contract without that field receives HTTP `400` before a model request is made.
Update the package set instead of removing request validation or changing the UI
to hide the mismatch.

## Troubleshooting

Start with the failing URL, status and response body, then correlate its request
with API logs. A successful `GET /api/ai-assistant` confirms the settings route;
it does not verify the POST contract or connectivity to MCP and the model.
Before SSE starts, validation failures return an error JSON object with `type`,
`text` and `code`. After SSE starts, errors arrive as stream events even though
the HTTP status is already `200`.

| Symptom | Check |
| --- | --- |
| Panel absent or endpoint `404` | Enable both flags, verify `AddNgbAiAssistant`/`MapNgbAiAssistant`, recreate the API after environment changes and reload the UI. |
| Different panel behavior across verticals | Compare the shared UI builds/packages actually loaded by each web host. Rebuild source consumers or repack/reinstall the npm package for package consumers. |
| Immediate POST `400`, possibly shown only as `HTTP 400` | Check malformed JSON, unknown fields, message roles, page context and time zone. If this follows an upgrade, verify the UI/API package set before investigating model settings. |
| HTTP `401` / `403` | Check the current user's API access token, interactive-user policy and active account. Apply the existing [MCP authorization rules](/guides/mcp-server#authentication-and-client-connection). |
| HTTP `413` / `415` | Reduce the request body or use `Content-Type: application/json`, respectively. |
| HTTP `429` | Respect `Retry-After`; there is at most one active turn per user per API instance, in addition to rate and concurrency limits. |
| `timeout` / HTTP `504` before streaming | The request deadline expired. Check MCP/model latency and narrow the request. |
| `unavailable` / HTTP `503` before streaming | Check the correlated API log, canonical MCP URL, TLS trust and provider configuration. Secrets and raw business payloads are intentionally absent from assistant failure logs. |
| `response_limit` or an interrupted answer | Check the configured output budgets and provider finish reason. The runner makes at most one output-limit recovery; request fewer rows/columns if the complete answer cannot fit. |
| Partial report or unavailable total | Check the existing report definition, scope, continuation and read budgets. A missing business filter or subtotal is not resolved by increasing model output tokens. |

Keep the browser request and provider failures distinct. The HTTP status alone
does not establish a Bedrock, Ollama or other provider error.

## Verification

`NGB.AiAssistant.Tests` covers orchestration, message boundaries, budgets and
cancellation. `NGB.AiAssistant.AspNetCore.IntegrationTests` exercises authenticated
HTTP/SSE and the real MCP SDK. `NGB.AiAssistant.Providers.Tests` verifies native
streaming/tool-result mappings and cancellation with SDK transport fixtures.
Shared UI tests cover transient state, rendering, sources, desktop/mobile behavior
and interrupted streams.

Run the automated backend checks without a live model:

```sh
dotnet test NGB.AiAssistant.Tests/NGB.AiAssistant.Tests.csproj -m:1 -nr:false
dotnet test NGB.AiAssistant.AspNetCore.IntegrationTests/NGB.AiAssistant.AspNetCore.IntegrationTests.csproj -m:1 -nr:false
dotnet test NGB.AiAssistant.Providers.Tests/NGB.AiAssistant.Providers.Tests.csproj -m:1 -nr:false
```

The explicit local model checks use synthetic data, require native Ollama and
do not contact an application's business database. They cover current-user
identity, verified record links and report execution with and without page context:

```sh
NGB_RUN_OLLAMA_TESTS=1 dotnet test NGB.AiAssistant.AspNetCore.IntegrationTests \
  --filter FullyQualifiedName~NativeOllama
```

Cloud contract tests do not certify credentials, quotas or model availability in
a deployment account. Exercise the chosen service with representative questions
when changing provider, model, endpoint or context limits.

## Implementation anchors

| Contract | Repository source |
| --- | --- |
| Runner, evidence handling and output recovery | `NGB.AiAssistant/AiAssistantRunner.cs`, `AiAssistantValues.cs`, `AiAssistantNumericEvidence.cs` |
| Turn limits and browser request contract | `NGB.AiAssistant/AiAssistantOptions.cs`, `AiAssistantContracts.cs` |
| Feature composition, canonical MCP client and local TLS options | `NGB.AiAssistant.AspNetCore/DependencyInjection.cs`, `AiAssistantHttpOptions.cs` |
| HTTP/SSE, validation, admission and metrics | `NGB.AiAssistant.AspNetCore/AiAssistantHttpEndpoint.cs`, `AiAssistantAdmission.cs` |
| Tool projection and report continuation | `NGB.AiAssistant.AspNetCore/McpToolSessionFactory.cs`, `AssistantReportContinuations.cs` |
| Provider registration and configuration | `DependencyInjection.cs` and option classes in `NGB.AiAssistant.Ollama`, `.OpenAI`, `.Anthropic` and `.Bedrock` |
| Shared UI and in-memory conversation | `ui/ngb-ui-framework/src/ngb/ai-assistant/` |
| Shared panel composition and toggle | `ui/ngb-ui-framework/src/ngb/site/NgbSiteShell.vue`, `NgbTopBar.vue` |

## Related documentation

- [MCP Server](/guides/mcp-server)
- [Feature Flags](/architecture/feature-flags)
- [Configuration Reference](/reference/configuration-reference#ai-assistant)
- [Prepare Local Platform Packages](/start-here/run-locally#prepare-local-platform-packages)

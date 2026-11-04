# Public application composition

The official template supplies API, Migrator, BackgroundJobs and Vue hosts.
Applications add Definitions, Runtime and PostgreSQL projects when needed. Declare
roles explicitly in `ngb.config.json`; project suffixes are not an authority.

`IDefinitionsContributor.Contribute(DefinitionsBuilder)` registers catalog and
document metadata. `DefinitionsBuilder.Build()` produces the immutable registry.
Use `NGB.Platform.Definitions`, `NGB.Platform.Metadata` and the public core types.
Provider adapters reference Definitions and persistence abstractions. Runtime can
use provider-neutral engines, but never provider implementations or host APIs.

Definition contributors execute in their DI registration order. Register a type's
`AddCatalog` or `AddDocument` contribution before any contributor that extends it.
When replacing a contributor in a test host, preserve its position in the service
collection; removing it and appending its replacement can move the base definition
after a runtime extension. Resolve the complete definition registry in the test
host before attributing a startup failure to storage or migrations.

API composition uses the public `AddNgbRuntime`, `AddNgbRuntimeAuthorization` and
`AddNgbPostgres` extensions. Startup validation executes application registrations
and belongs to integration/build workflows as appropriate, not static inspection.
Use the template's existing error handling, identity and health wiring.
Read [application errors and HTTP boundaries](errors.md) for stable codes,
field validation, cancellation and provider-specific failure mapping.

Standard runtime registrations supply concrete services, resolvers, built-in
contributors and report executors. Applications extend their public abstractions
and declared definitions; a concrete sealed default is not an additional
subclassing contract. Keep trusted runtime services separate from current-user
authorization wrappers. The default non-request actor and report-variant contexts
supply no active identity; use the template's request-bound host composition.

UI imports must resolve through `@ngbplatform/ui` package exports: the main entry,
`contracts`, `editor`, `layout`, `lazy`, `navigation`, `work-center`, `styles`,
`vite-public-assets` and `tailwind-preset`. Inspect the resolved version's actual
exports and declarations for component contracts; private `src/` imports are not
extension points. Tests and build output must not require the NGB checkout.

## Server navigation

Register application `IMainMenuContributor` implementations through DI. Return
`MainMenuGroupDto`/`MainMenuItemDto` with registered resource kinds/codes, real UI
routes and stable ordering. The runtime merges trimmed group labels without case
sensitivity and de-duplicates items by kind/code; the lowest ordinal wins. Blank
groups are discarded. These contributions describe navigation, not permission grants.

Serve `/api/main-menu` through an authorized `AdminControllerBase` whose constructor
receives `PermissionAwareAdminService`. Its current-user filtering removes denied
items and empty groups; permission changes refresh the cached menu. The trusted
`IAdminService` registration is unsuitable as an interactive authorization boundary.
Active administrators receive registered entries, while inactive users receive no
menu. Keep API authorization on the destination independently of menu visibility.

## Bounded installed-definition search

Inject the public `INgbSearchService` into an authenticated application endpoint.
The example's `EquipmentSearchController` forwards query, limit, optional kind/type
and cancellation to that service. The service accepts 1–256 nonblank query characters,
a limit from 1 through 50, supported `NgbSearchKind` values and canonical type codes
of at most 128 characters. Type-code matching is case-sensitive. Standard MVC query
binding treats a blank optional type-code string as an omitted filter.

Catalog/document searches first discover viewable metadata and then invoke authorized
lookups. Those records require both `view` and `lookup`; viewing metadata alone is
insufficient. Search does not execute reports. Results contain canonical kind/type/id
identities, and report hits have no record id. Preserve `Truncated` and
`UnavailableSources` in the application's presentation. A capped or partially failed
search is not an exhaustive list or a pagination API. The generic profile limits
catalog/document matches to at most six per type, even with a larger overall limit.
Catalog lookups exclude deletion-marked records.

The registered 3.4.0 search service reads General Journal Entry through
`IAuthorizedGeneralJournalEntryReadService`, after selecting currently authorized
metadata. Ordinary documents use the generic lookup reader. `Document` excludes
the journal kind; `GeneralJournalEntry` selects it. Journal searches include
all trash states, use the same six-per-type bound and preserve truncation.
When constructing `NgbSearchService` manually, use the overload accepting the
specialized authorized journal reader; the older constructor remains available
for existing generic-reader integrations. Do not add a `display` storage column
or bypass authorization. See [`search.general-journal-metadata`](../known-limitations.json).

For an application-specific search presentation, the public `NgbSearchPipeline`
accepts scoped delegates, a maximum limit and cancellation. Supply authorized readers.
It executes sources sequentially because scoped readers may share a connection,
records failed source names and preserves caller cancellation. Its blank-query/default
limit behavior differs from the stricter `INgbSearchService` input contract.
`CommandPaletteSearchScope.Parse` maps `:`, `@` and `#` (and their named forms) to
document, catalog and report scopes; unknown syntax selects no source.

## Watchdog host

Reference `NGB.Platform.Watchdog` in an application-owned monitoring host. Configure
its Keycloak administrative-host settings and the `WebClient` URL, then compose
`AddNgbWatchdog`, `UseNgbWatchdog` and `MapNgbWatchdog`. The string-title overload
registers the web-client check and packaged `dashboard.css`; the options overload
lets the application select paths/styles and explicitly add its health checks.

The health endpoint reports dependency availability and is distinct from the
protected dashboard and its data endpoint. Keep `RequireAuthorization` enabled for
an exposed dashboard: the existing administrative policy requires `ngb-admin`.
Do not replace that policy or infer authorization from the page title. Health-check
network exposure, production HTTPS and the identity-provider login flow remain host
deployment concerns. A green health result alone does not certify a deployment.

`WatchdogOptions` normalizes route paths and titles and rejects blank required
values. Custom stylesheet files must exist at the resolved absolute path, application
base directory or content root. Missing assets fail mapping before startup. The
default stylesheet is supplied by the package; verify its presence in build/publish
output when using that overload.

The [Watchdog example checks](../examples/equipment-inspection/README.md#watchdog-host)
use real loopback HTTP servers to verify a web dependency's failure and recovery,
custom routes, packaged styling and dashboard/data authorization. Test-only cookie
sign-in supplies synthetic principals while preserving the host's authorization
policy. This verifies the consumer contract without claiming external OIDC login,
production TLS or load behavior.

## A public declaration is not an implemented endpoint

The public assembly snapshot also contains declarations without a consumer in
the current standard API/runtime. In particular, the `CommandPalette*Dto` family
does not establish a command-palette server endpoint. The documented search path
uses `INgbSearchService` and its `NgbSearchResult` through an application-owned
authorized adapter.

The empty `ExecuteActionRequestDto` is likewise not the document action endpoint's
request type. That endpoint accepts `ExecuteDocumentActionRequestDto` and retains
its version/idempotency contract. Follow the active controller/service signature
and the verified workflow instead of guessing from a similarly named DTO.

## Supporting values, cursors and diagnostics

Shared helpers retain distinct contracts. `EnsureUtc` checks `DateTime.Kind`; it
does not convert a local timestamp. `EnsureMonthStart` validates the first day of
a month. Use `TimeProvider` for injectable UTC time. GUID parsing and reference
objects do not resolve an entity or grant access. Persist enum codes rather than
editable display labels, and choose the DTO's actual JSON serializer settings.

`CodeNormalizer` supplies trimmed invariant lowercase business keys. Physical SQL
identifiers use the stricter `IdentifierNormalization` rules and provider-selected
length limits. Its deterministic shortening suffix is not a security hash or a
uniqueness guarantee; retain storage collision checks and parameterize values.

`OpaqueCursorCodec` produces a versioned base64url envelope with a query-kind
fingerprint. It does not sign, encrypt or authorize that state. Validate decoded
values and bounds at the consuming boundary. The report HMAC continuation token
has a separate contract; do not infer its guarantees for every platform cursor.
`HttpEndpointUri` likewise performs syntactic checks without DNS resolution and
does not establish network reachability or a complete outbound-access policy.

Preserve typed platform error codes and safe context through the standard HTTP
mapper. Permission failures, protected administrator roles, missing security
entities, invalid workflow state, in-progress posting and period-order conflicts
require different responses; converting them to empty success loses meaning.
Configuration failures require correcting definitions or DI. Identity-provider
errors must not include secrets or be reported as completed provisioning.

`IRecordReadScopeFactory` checks storage row/JSON-byte budgets before materializing
an already authorized record. Both limits are positive and include typed head
and parts; they do not measure the final HTTP response. Dispose the returned
scope and preserve `RecordReadLimitExceededException`. The scope does not replace
the initial record-type permission check.

## Host adapters and deployment configuration

The inherited catalog, document, security and Work Center controllers remain thin
adapters. Application controllers provide routing/authentication attributes and
the standard permission-aware service composition. Register API controllers and
JSON enum serialization through `AddControllersApi`. The old document URL helpers
still dispatch actions; they differ from the unsupported legacy string-action
method on `IDocumentService`.

Feature discovery and `NgbFeatureAttribute` do not replace permission checks. The
attribute allows at least one named enabled feature before constructing the
controller, which is useful for the combined content-summary endpoint. Disabled
deployment features remain disabled for administrators.

`KeycloakSettings.Issuer` is the trusted public token issuer and browser authority.
An optional private `MetadataAddress` changes discovery reachability, not token
issuer trust. `NgbHostingOptions.PublicApiUrl` is a configured public API base URL,
including a proxy prefix when needed; it does not configure listeners and must
not come from an untrusted request header. Dashboard links and branding/theme
assets are presentation settings, not destination permissions.

Keycloak administration settings bound concurrency, queue size, lookup cache,
response bytes and deadlines. Retain startup validation and keep client secrets
out of diagnostic dumps. A successful discovery health check does not certify
OIDC login or user permissions.

The existing hosting convenience API explicitly offers allow-any CORS; do not
describe that policy as restrictive. Its DEBUG health HTTP handler also bypasses
certificate validation. Use Release artifacts and review the application's actual
deployment policy. These are existing SDK behaviors, not changes made by the Kit.

Work Center SignalR registration and outbox-processing ownership are separate.
The hub joins authenticated active platform users to per-user groups and emits
change-version hints. Query actual state through the authorized service. Bounded
polling/maintenance options do not certify multi-replica processing or throughput.

PostgreSQL error mapping and Dapper date handling belong in provider/hosting
composition. A provider cursor stream requires an active transaction and trusted
SQL; batching does not establish permission or total export bounds. Application
hosts should use `SchemaInitMode.None` in production and run the dedicated
migrator. Do not enable schema repair automatically to make startup succeed.

## Host identity implementation boundaries

The published host composition validates the configured issuer and audience/client
claims before mapping trusted realm/client roles. Application authorization still
checks the active account and registered permission codes. An authentication
ticket, identity-provider lookup cache or feature registry does not replace those
checks. Configure forwarded-header trust and canonical public origins for the
actual proxy deployment; do not infer a secure proxy policy from a helper's name.

`MemoryCacheTicketStore` stores bounded authentication tickets in this host's RAM.
It is not distributed session storage. Account for restart and replica routing
when deploying administrative consoles. `SecurityKeyParameters` is a public
declaration, but current registration does not invoke its private signing-key
helper; setting it does not activate an alternative JWT key mechanism.

The Keycloak administration components bound concurrent requests, queueing and
response sizes, coalesce lookups and refresh client credentials. They are host
implementation details beneath user administration. Keep credentials and raw
identity-provider failure bodies out of diagnostic artifacts. Local cache expiry
and access-version checks are not evidence of cross-replica invalidation.

Definition, feature, permission and Work Center preference registries are startup
snapshots. They validate identities and configuration, not current actor access.
Feature flags remain independent of Administrator permissions; an observed
telemetry counter or health gauge is not an authorization or performance result.

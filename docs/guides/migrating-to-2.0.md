# Migrating from NGB Platform 1.3.1 to 2.0.0

NGB Platform 2.0 replaces the legacy document UI-effects and derivation surfaces with
metadata-driven Document Actions and adds the durable Work Center. This is an intentional major
release: do not update a production vertical by changing package versions without completing the
source, database, and deployment steps below.

## Compatibility boundary

Update all `NGB.Platform.*` references in one change. A mixed 1.3.1/2.0.0 NuGet graph is unsupported.
Update `@ngbplatform/ui` and each web application to 2.0.0 in the same release train.

The following 1.3.1 contracts are removed:

- `IDocumentUiEffectsContributor`;
- `DocumentUiEffectsDto`, `DocumentUiActionReasonDto`, and `DocumentUiActionContributionDto`;
- `DocumentDerivationActionDto` and `IDocumentService.GetDerivationActionsAsync`;
- `getDocumentDerivationActions`, `deriveDocument`, `DocumentUiEffects`, and
  `resolveDocumentActions` from the shared UI package;
- the generic `GET .../derive-actions` and `POST /api/documents/{targetType}/derive` routes.

The canonical action endpoint now requires an idempotency key and expected document version, and
returns an execution result rather than a bare document.

## Update NuGet consumers

Set every platform dependency to 2.0.0:

```xml
<PackageReference Include="NGB.Platform.Api" Version="2.0.0" />
<PackageReference Include="NGB.Platform.Application.Abstractions" Version="2.0.0" />
<PackageReference Include="NGB.Platform.Contracts" Version="2.0.0" />
<PackageReference Include="NGB.Platform.PostgreSql" Version="2.0.0" />
<PackageReference Include="NGB.Platform.Runtime" Version="2.0.0" />
```

Use the same version for any other `NGB.Platform.*` packages in the vertical. Delete `bin` and `obj`,
restore, and rebuild the entire solution; do not reuse assemblies compiled against 1.3.1.

Custom document controllers must pass the new services to the base constructor:

```csharp
public sealed class DocumentController(
    IDocumentService documents,
    IDocumentActionQueryService actionQueries,
    IDocumentActionDispatcher actionDispatcher)
    : DocumentControllerBase(documents, actionQueries, actionDispatcher);
```

Custom `IDocumentPostingService` implementations must implement the transaction-management
overloads introduced in 2.0. Custom `IDocumentRepository` implementations should implement
`IncrementVersionAsync`; the default member throws because action execution requires stable
optimistic concurrency.

## Replace UI-effects contributors

Replace each `IDocumentUiEffectsContributor` with:

1. an `IDocumentActionDefinitionsContributor` that declares stable action metadata;
2. an `IDocumentActionContextEnricher` for bounded database facts;
3. an `IDocumentActionAvailabilityEvaluator` for domain availability;
4. an optional `IDocumentActionAuthorizationEvaluator` when default permission mapping is
   insufficient;
5. an `IDocumentActionHandler` for a vertical command, or an existing lifecycle/derivation binding.

The effects endpoint continues to return accounting and register effects, but no longer returns UI
action booleans. Load `/editor-state` and render its `actions` collection instead.

## Replace derivation calls

| 1.3.1 | 2.0.0 |
| --- | --- |
| `GET /api/documents/{type}/{id}/derive-actions` | `GET /api/documents/{type}/{id}/editor-state` |
| `POST /api/documents/{targetType}/derive` | `POST /api/documents/{sourceType}/{sourceId}/actions/{actionCode}` |
| `DocumentDerivationActionDto` | `DocumentActionDto` with `executionKind = Derivation` |
| bare `DocumentDto` action response | `ExecuteDocumentActionResultDto.document` and optional `createdDocument` |

Send a unique `Idempotency-Key` header and the latest `ExpectedVersion` from editor state:

```http
POST /api/documents/acme.order/019.../actions/acme.create_invoice
Idempotency-Key: 019...
Content-Type: application/json

{
  "expectedVersion": 7
}
```

The standard lifecycle routes remain as transitional adapters, but new clients should use the
canonical action endpoint.

## Update UI consumers

Install the 2.0 package:

```bash
npm install --save-exact @ngbplatform/ui@2.0.0
```

Replace:

- `getDocumentDerivationActions` with `getDocumentEditorState`;
- `deriveDocument` with `executeDocumentAction`;
- local action resolution and `resolveDocumentActions` with server-provided action metadata;
- `DocumentUiEffects` checks with each action's `isAllowed` and `disabledReasons`;
- route-specific action branching with `resolveDocumentActionTarget` for semantic targets.

Treat SignalR Work Center messages as invalidations only. Reload summary/feed data over authenticated
HTTP; do not trust or render privileged payload data from hub messages.

## Database and deployment order

1. Back up every vertical database and validate the restore/rollback procedure.
2. Build and stage one coherent 2.0.0 artifact set: all `NGB.Platform.*` packages, the vertical
   binaries, migrator, API, background jobs, watchdog, `@ngbplatform/ui`, and the web application.
3. Drain traffic and stop the 1.3.1 API/background hosts so that no document work is created during
   the contract cutover. Do not run 1.3.1 and 2.0 action clients concurrently.
4. For each vertical database, run the **2.0 vertical migrator** before starting any 2.0 runtime
   host. Select the vertical module (`pm`, `crm`, `agency-billing`, or `trade`); its migration pack
   declares `platform` as a dependency, so the migrator deterministically applies the platform
   migration before the vertical pack. Do not execute embedded SQL files manually.
5. Before the mutating run, use that exact migrator artifact with `--list-modules` and then
   `--modules <vertical-module> --dry-run --info --show-scripts`. Review the planned dependency order
   and scripts. Apply with `--modules <vertical-module> --repair`, retain the successful migration
   log, and fail the deployment if the migrator exits non-zero.
6. Confirm that `V2026_07_26_0100__ngb_platform_document_actions_work_center.sql` was applied in
   every vertical database. CRM must additionally apply
   `V2026_07_31_1600__crm_document_draft_post_validation.sql` through its `crm` pack.
7. Deploy the 2.0 API, background jobs, and watchdog. Each API must explicitly register both
   `AddNgbWorkCenterRealtime()` and `AddNgbWorkCenterOutboxProcessing(configuration)`; Background
   Jobs hosts must not register the outbox polling loop.
8. Deploy the matching 2.0 web application and restore traffic only after readiness succeeds.
9. Verify `/health`, including `PostgreSQL Server`, `Keycloak`, and `Work Center outbox`; then run
   document-action idempotency/concurrency and Work Center delivery smoke tests.
10. Monitor outbox pending age, failed deliveries, open tasks, overdue tasks, API errors, and
    SignalR reconnects throughout the rollout.

When deploying without a distributed SignalR backplane, keep each vertical at one steady-state API
replica, enable WebSocket proxying at the TLS edge, and configure long ingress read/send timeouts.
Do not run the Work Center outbox polling loop in Background Jobs hosts. The API is the single
owner, but outbox processing and realtime are separate explicit registrations in that host.

The migration is additive at the storage layer, but application rollback after users execute 2.0
actions requires operational review. Do not run 1.3.1 and 2.0 action clients concurrently.

## Validation

From the repository root:

```bash
dotnet test NGB.sln
dotnet build NGB.sln -c Release --no-restore
bash packaging/nuget/pack-platform.sh
bash packaging/nuget/verify-platform-packages.sh
npm --prefix ui run test:all
NGB_COVERAGE_BASE_REF=HEAD ./quality/coverage/run-document-actions-work-center-backend-coverage.sh
NGB_COVERAGE_BASE_REF=HEAD ./quality/coverage/run-document-actions-work-center-frontend-coverage.sh
```

For a custom vertical, add compile-time package-consumer tests that reference the packed 2.0.0
artifacts rather than platform source projects.

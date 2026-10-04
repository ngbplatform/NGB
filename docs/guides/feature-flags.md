# Feature flags

NGB supports deployment-wide feature flags backed by `Microsoft.FeatureManagement`. The platform owns feature codes and discovery; permissions continue to authorize individual users. Neither a trusted Keycloak administrator nor an application Administrator role bypasses a disabled feature. [Attachments and Notes](/guides/attachments-and-notes) are independent features built on this mechanism.

## Configuration and rollout

Feature configuration is read once at API startup and frozen for that process. Apply changes through the existing deployment configuration and restart all API replicas. Reloading a JSON file does not change running feature state. Flags are managed through deployment configuration; there is no flag-management API or administration screen, percentage rollout, user targeting or external configuration service.

For Docker Compose, changing `.env.pm`, `.env.trade`, `.env.ab` or `.env.crm` requires recreating the API container with the updated environment. `docker compose restart` restarts the existing environment. For example, after editing `.env.pm`, run from the repository root:

```sh
docker compose --env-file .env.pm -f docker-compose.pm.yml up -d ngb.pm.api
```

When running from an IDE, supply `FeatureManagement__Attachments` and `FeatureManagement__Notes` as process environment variables, or use the equivalent .NET configuration keys. ASP.NET Core does not automatically read Compose `.env` files. Reload the browser after all API replicas use the new configuration.

All registered features default to `false`. Unknown feature codes, incorrect casing, nonboolean values and nested filter/variant configuration fail startup with an actionable configuration error. This prevents a typo from silently changing the intended deployment state.

```json
{
  "FeatureManagement": {
    "Attachments": false,
    "Notes": true
  },
  "Attachments": {
    "UploadExpirationEnabled": false
  }
}
```

Equivalent environment keys:

```text
FeatureManagement__Attachments=false
FeatureManagement__Notes=true
Attachments__UploadExpirationEnabled=false
```

The four local Compose stacks map these keys from `FEATURE_ATTACHMENTS`, `FEATURE_NOTES` and `ATTACHMENTS_UPLOAD_EXPIRATION_ENABLED` in their respective `.env` files. Those local environments explicitly enable both features and pending upload expiration. API defaults remain off when these values are absent. These flags control API capabilities, not Compose services: the provided full stacks still start MinIO and require its initialization to succeed even when Attachments is disabled.

| Configuration | User operations | Storage and upload expiration |
| --- | --- | --- |
| Both flags absent or false, upload expiration false | No attachments or notes | No MinIO registration, validation or attachment worker |
| Notes true, attachments false, upload expiration false | Notes only | No MinIO dependency |
| Attachments true | Attachments; notes follow their own flag | Configured storage is required; pending upload expiry runs |
| Attachments false, upload expiration true | Attachments blocked; notes follow their own flag | No MinIO registration or validation; pending upload expiry continues using only the database |

Use `Attachments:UploadExpirationEnabled=true` to keep expiring abandoned pending uploads when user access to Attachments is disabled. This setting is operational, not a user feature or permission. With Attachments enabled, the worker always runs; with both settings disabled it stops. Expiration needs only the database and standard AuditLog, with no MinIO or Outbox dependency. Keep stored objects indefinitely and disable bucket expiry rules. See [history and retention](/guides/attachments-and-notes#history-in-the-parent-auditlog).

Turning a feature off does not delete its business data or revoke existing role grants. Notes and ready attachment metadata remain available when it is enabled again. The expiration worker may still mark abandoned pending uploads as deleted, while retaining all stored files. Disabling the flag does not revoke previously issued MinIO bearer URLs: upload URLs remain usable until expiry, and download URLs remain usable until expiry. Requests already admitted by an old replica may finish during deployment, so drain and restart all replicas before treating a disable as fully applied.

## Host composition

Use the storage callback so MinIO is registered and validated only when Attachments is enabled:

```csharp
builder.Services.AddNgbAttachmentsNotesApi(builder.Configuration, services =>
    services.AddNgbMinioAttachments(builder.Configuration.GetSection("Attachments:MinIO").Bind));
```

The callback runs only when Attachments is enabled. A missing storage provider or invalid storage configuration then fails startup. Startup does not require a successful network request to MinIO. API operations that contact unavailable storage return a sanitized `503`; direct browser uploads and downloads fail at the storage endpoint. Notes and existing application operations remain available. Metadata-only operations and local URL signing do not establish storage availability.

For a runtime consumer without API hosting, register `AddNgbFeatureManagement(configuration)` before `AddNgbAttachmentsAndNotes()`. The latter defaults features to disabled if no feature management is registered. Existing attachment/note service interfaces remain unchanged; their registered implementations enforce feature checks before constructing storage-dependent services.

Apply the normal additive database migrations before enabling either feature. Feature flags do not replace schema migration, permissions, storage provisioning or backups. Upgrading packages with flags absent keeps new content services inactive; migrated installations retain existing application behavior.

## Extending the registry

Register extra deployment-wide definitions before registering modules:

```csharp
builder.Services.AddNgbFeatureManagement(builder.Configuration,
[
    new NgbFeatureDefinition("CrmCampaigns", "Campaigns", "CRM")
]);
```

Codes must contain only ASCII letters, digits, underscores or hyphens, and must match their registered casing in configuration and queries. Duplicate codes, including codes differing only by case, are rejected. Each definition also requires a nonblank display name and group. Codes are stable configuration contracts; define constants for reuse, as the platform does in `NgbFeatures`.

Supply all additional definitions in the first registration. A later call may reuse the same flag configuration without additional definitions, as `AddNgbAttachmentsNotesApi` does internally. Passing `additionalFeatures` again or changing flag values in a later registration is rejected. Registering a definition only makes it discoverable and configurable; it does not automatically guard endpoints, add permissions or create UI actions.

Inject `INgbFeatureService` into runtime services and call `RequireAsync` before performing feature-specific work. Use `IsEnabledAsync` for optional composition and `GetAllAsync` for discovery. Unknown queried codes are disabled. Use `[NgbFeature(code)]` on controllers/actions to reject disabled HTTP operations before controller construction. Multiple codes on one attribute mean at least one must be enabled; individual service operations still enforce their own feature and permissions.

## API and UI

Authenticated `GET /api/features` returns registered feature codes, display names, groups and enabled states with `Cache-Control: no-store`. It contains no storage configuration or secrets and grants no permissions. Disabled operations return `404` with error code `feature.disabled`; existing authentication and permission checks remain in effect.

For the Notes-only configuration above, the response includes:

```json
[
  {
    "code": "Attachments",
    "displayName": "Attachments",
    "group": "Attachments & Notes",
    "enabled": false
  },
  {
    "code": "Notes",
    "displayName": "Notes",
    "group": "Attachments & Notes",
    "enabled": true
  }
]
```

This endpoint reports deployment availability, not a user's effective access. `enabled: true` does not mean the current user has the corresponding permission. Content events in the parent's AuditLog are also filtered by the feature and read permission; turning a feature off hides those events without deleting them and blocks new audit-download URLs.

The UI package exports `useFeatureStore`, `NGB_FEATURES` and `FeatureState`. The store deduplicates concurrent discovery requests and caches successful results for up to 30 seconds when loaded again; it does not poll. `load(true)` explicitly refreshes and `reset()` clears its state. Failed discovery closes access to feature-specific UI, and an old server's missing discovery endpoint means no enabled features. Reload the browser after deploying new flag values.

Attachments and Notes appear independently in **More → Attachments & Notes** on saved objects when their features are enabled; a missing read permission disables the corresponding menu item. Administrator roles have all content permissions, while deployment feature flags still control availability. Their effective permissions are protected in [Role Management](/platform/security-and-permissions#administrators); enabling a feature requires no manual grants for administrators. A disabled feature makes no list or mutation requests. The shared content summary is requested when at least one feature is enabled; the API returns `null` for counts whose feature is disabled or read permission is missing. Disabling a capability in the UI state closes its drawer and cancels outstanding requests; the backend remains authoritative for every operation.

## Upgrade verification

Upgrade verification covers startup with both flags absent and no MinIO settings, Notes without MinIO, authenticated direct requests to disabled endpoints, persistence across disable/enable, continued upload expiration with user features off and no MinIO configuration, and a storage outage while Notes and existing endpoints remain available. These checks supplement package compatibility validation; introducing feature flags alone is not proof that every application can upgrade unchanged.

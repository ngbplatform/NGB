# Attachments & Notes

Attachments and plain-text notes belong to a stable business object identity, independently of the object's payload, document version, workflow and posting state. The editor header exposes enabled features under **More → Attachments & Notes**, with paperclip/note icons, positive-count badges and on-demand right drawers. In documents, this group follows Output and History & share. Catalog page and drawer headers use the same group. Missing read permission disables the corresponding menu item. New, unsaved objects have no content actions. Catalogs, ordinary documents and General Journal Entries use the same implementation in Property Management, Trade, Agency Billing and CRM.

## Identity and access

`BusinessObjectRef` contains `kind`, canonical `typeCode` and `id`. Supported kinds are `CatalogItem`, `Document` and `GeneralJournalEntry`. GJE must use `general_journal_entry` with its dedicated kind; the ordinary Document alias is rejected. Type aliases, arbitrary tables, chart-of-accounts items and registers are unsupported.

Every operation checks the feature, resource permission and parent access through existing platform services. Knowing a resource ID grants no access. A summary returns `null` for a disabled feature or missing read permission. Soft-deleted and posted parents retain content as long as normal parent read access permits it. Hard-deleted parents cannot be resolved, so their content becomes inaccessible; do not hard-delete business objects without a separate retention policy.

Administrator roles receive all registered permissions automatically. Grant the following permissions explicitly to other roles:

| Resource | Actions |
| --- | --- |
| `system.attachments` | `read`, `create`, `delete` |
| `system.notes` | `read`, `create`, `update`, `delete` |

The trusted Keycloak role `ngb-admin` grants full permissions with or without a pre-existing platform user record. Application administrator roles registered through `NgbAdministratorOptions.ApplicationRoleCodes` receive every registered permission; PM registers `pm-administrator` and CRM registers `crm.administrator`. Administrator identity is determined by trusted role codes, never by editable display names. Inactive accounts remain blocked, and administrator permissions do not enable a disabled deployment feature. Mutations require an active authenticated actor. Audit events record the parent target, attachment/note ID, actor and timestamp. Note text before and after each change is stored in the standard audit field changes. Storage keys, credentials and signed URLs are never included.

## Package and host composition

`NGB.Platform.Attachments` and `NGB.Platform.Notes` define separate capabilities; Runtime orchestrates them; PostgreSql persists metadata; `NGB.Platform.Attachments.MinIO` alone references the MinIO SDK. Contracts are provider neutral. Notes do not depend on storage. CRM consumes released platform NuGet and npm packages, with no production platform ProjectReference.

Both features default to disabled. See [Feature Flags](/guides/feature-flags) for independent enablement, startup behavior and pending upload expiration. API hosts configure storage through the conditional registration callback:

```csharp
builder.Services.AddNgbAttachmentsNotesApi(builder.Configuration, services =>
    services.AddNgbMinioAttachments(builder.Configuration.GetSection("Attachments:MinIO").Bind));
```

The host registers the platform runtime, PostgreSQL, audit and actor services. Storage is registered and validated only when Attachments is enabled. `AttachmentUploadExpirationService` uses only database metadata and the standard AuditLog; it has no object-storage or Outbox dependency. Its hosted worker runs immediately, then waits 30 seconds between cycles, using scoped services and cancellation. Multiple replicas coordinate through PostgreSQL row locks with `SKIP LOCKED`. Enable `Attachments:UploadExpirationEnabled` to continue expiring abandoned uploads while the Attachments feature is disabled; this requires no MinIO configuration.

## API and lifecycle

| Method and route | Result |
| --- | --- |
| `GET /api/business-objects/content-summary` | Counts for enabled, authorized capabilities in one provider query; other counts are `null` |
| `GET /api/attachments` | Ready, nondeleted metadata page |
| `POST /api/attachments/uploads` | Pending metadata and short-lived PUT target |
| `POST /api/attachments/{id}/complete` | Verified ready attachment; idempotent |
| `POST /api/attachments/{id}/download` | Short-lived native download URL |
| `DELETE /api/attachments/{id}` | Mark for deletion; idempotent, retains stored bytes |
| `POST /api/attachments/{id}/audit-download` | Download retained completed attachment with parent, attachment-read and audit permissions |
| `GET /api/notes` | Nondeleted notes page |
| `POST /api/notes` | Plain-text note, version 1 |
| `PUT /api/notes/{id}` | Text and expected note version |
| `DELETE /api/notes/{id}?version=…` | Expected version for the first logical deletion; repeated deletion is idempotent |

Authenticated `GET /api/features` exposes feature availability. Disabled attachment/note operations return `404` with `feature.disabled`; the summary endpoint does the same when both features are disabled. Authentication and permissions remain separate requirements.

Lists/summary accept `kind`, `typeCode`, `objectId`; lists also accept `limit` (1–100, default 50) and an optional resource-ID `cursor`. Pagination uses descending immutable UUIDv7 IDs and indexes scoped to the parent. Lists fetch one extra row; author names are joined in the same query. File bytes never pass through the NGB API.

Upload creation takes `{ target, fileName, contentType, sizeBytes }`. The client PUTs the file using exactly the returned storage headers, without NGB Authorization or cookies. Completion HEADs the staging object, verifies declared size and media type, then conditionally copies by ETag from `uploads/{id}` to `attachments/{id}`. A changed staging object cannot pass that copy. Ready objects cannot be changed by replaying the PUT URL. Completion can be safely retried after a lost response. Failed transfers remain pending until deletion or expiry; clients may start a fresh attempt.

Filename normalization strips paths, control characters and Unicode format controls; storage keys never contain filenames. Media type is informational, not proof of safe file contents. Downloads force attachment disposition, UTF-8 filename, octet-stream and no-store. No previews or HTML/Markdown rendering are supported.

Pending uploads reserve the count quota. A transaction-scoped advisory lock serializes reservations per target; parallel uploads cannot exceed it. Pending uploads do not appear in lists or badges. Completion and logical deletion lock the metadata row. None of these writes touches parent version, workflow, posting, accounting or register effects. Notes have their own version and reject stale edits with `notes.version_conflict` (409).

Marking for deletion commits metadata and audit in the same database transaction. Deleted attachments and notes immediately disappear from normal lists and counts. The standard attachment download endpoint rejects deleted attachments; AuditLog can issue a short-lived download URL for a completed retained file after checking current permissions. Previously issued URLs remain valid until expiry.

NGB does not delete objects from MinIO, including staging uploads. The upload expiration worker marks abandoned pending uploads as deleted, releases their reserved quota and records the expiry in the parent audit. It does not create or process Outbox jobs. The application MinIO policy allows reading and writing but does not grant `s3:DeleteObject`. No automatic expiry rule should apply to the attachment bucket, including `uploads/` and `attachments/`.

## History in the parent AuditLog

Open the **Audit log** action on the Catalog, Document or General Journal Entry. Its existing timeline contains attachment upload/completion/deletion events and note creation/edit/deletion events alongside the parent's own changes. Each event shows who performed the action and when. The standard Original Value / New Value table preserves the full plain text of notes, including line breaks; marking a note for deletion keeps its last text visible in that event. Attachment events include the original filename, content type, size and status. Completed attachment events offer **Download attachment**, including after logical deletion.

The timeline uses seek pagination; **Load older events** loads the next page without discarding already loaded history. It does not stop at the first 100 events. Audit access alone does not grant access to content: the relevant feature must be enabled and the reader must have the parent's normal read access, audit access and `system.attachments.read` or `system.notes.read`. Administrators receive these permissions automatically; inactive users remain blocked. Download authorization is checked again when the user clicks the button.

Deleted content is visible only through AuditLog. There is no recycle bin, separate version store, restoration or revert action. History uses the existing audit tables and append-only protections. This history change requires no new database migration and does not backfill older attachment/note events. Parent payload, document version, workflow and ledger effects remain unchanged; only the parent's audit timeline gains the content events.

Back up both PostgreSQL (including audit records) and MinIO. Restoring the database requires compatible retained object data. Monitor storage capacity because retention is indefinite.

## Configuration

Use environment variables or a secret manager for credentials. MinIO settings are required only when Attachments is enabled. Notes and pending upload expiration require no storage configuration. Examples below are configuration keys, not production defaults for secrets.

| Key | Default / requirement |
| --- | --- |
| `FeatureManagement__Attachments` | false; explicit opt-in, applied at API startup |
| `FeatureManagement__Notes` | false; independent of attachments and MinIO |
| `Attachments__UploadExpirationEnabled` | false; keep pending upload expiration active when Attachments is disabled; no MinIO dependency |
| `Attachments__MaxSizeBytes` | 52428800 (50 MiB); configurable limit from 1 byte to 5 GiB; zero-byte files are allowed |
| `Attachments__MaxActivePerObject` | 100; allowed 1–1000, includes pending reservations |
| `Attachments__UploadLifetime` | `00:10:00`; allowed 1 minute–1 hour |
| `Attachments__DownloadLifetime` | `00:03:00`; allowed 30 seconds–15 minutes |
| `Attachments__PendingStaleAge` | `1.00:00:00`; at least PUT lifetime + 5 minutes, at most 7 days |
| `Attachments__ExpirationBatchSize` | 25 stale rows per transaction, allowed 1–100 |
| `Notes__MaxTextLength` | 10000; allowed 1–100000 |
| `Attachments__MinIO__InternalEndpoint` | Required absolute service endpoint |
| `Attachments__MinIO__PublicEndpoint` | Required browser-reachable signing endpoint |
| `Attachments__MinIO__Bucket` | `ngb-attachments`; private, dedicated |
| `Attachments__MinIO__Region` | `us-east-1`; must match deployment |
| `Attachments__MinIO__AccessKey`, `SecretKey` | Required application credentials, not root credentials |
| `Attachments__MinIO__RequestTimeoutSeconds` | 30; allowed 1–120 |
| `Attachments__MinIO__AllowInsecureHttp` | false; enable only for local development |

TLS is required by default. Endpoints have no path prefix, userinfo, query or fragment. Preserve the signed host through reverse proxies: URLs must never be rewritten after signing. Public DNS/TLS must work from the user's browser; Docker service DNS belongs only in InternalEndpoint. Configure CORS for the exact UI origins and PUT with Content-Type; do not put authentication cookies on the storage origin. Keep clocks synchronized. PUT signing cannot prevent a client from sending more bytes than declared; enforce storage quotas and network request-size controls in addition to completion validation. NGB does not scan file contents; if scanning is required, add a scanning/quarantine gate before Ready.

### Local MinIO

The local `docker-compose.{pm,trade,ab,crm}.yml` files follow the PostgreSQL and Keycloak pattern: a thin Dockerfile extends a prebuilt image, and a separate `ngb.<vertical>.minio.init` service provisions storage after the server becomes healthy. `docker/minio/Dockerfile` only adds `docker/minio/init.sh`; it does not compile MinIO or download a separate client. The selected server image must include `minio`, `mc` and a POSIX shell. Readiness uses the bundled `mc ready` command; no `curl` installation is needed.

Each `.env.{pm,trade,ab,crm}` contains the image repository/tag/digest, endpoints, ports, bucket and local credentials. Development and integration tests use the third-party image `ghcr.io/coollabsio/minio:RELEASE.2025-10-15T17-29-55Z`, pinned to digest `sha256:69b55a1c1c5dc285ce04db96689f5b2102317fc77a50680a1874ca6efd1c87f9`. The pin is configured in the vertical `.env` files, `docker/minio/Dockerfile` and `quality/integration/MinioIntegrationFixture.cs`; keep them aligned when updating the local/test image.

To use a verified internal mirror in Compose, set all three `MINIO_IMAGE_REPOSITORY`, `MINIO_IMAGE_TAG` and `MINIO_IMAGE_DIGEST` values together; the digest must identify that image. Do not use a mutable `latest` tag. Select the integration-test image separately with `NGB_TEST_MINIO_IMAGE` as described under Validation.

Each vertical has its own server, network and persistent volume. The default host ports are bound to loopback:

| Vertical | S3 API port | Console port |
| --- | --- | --- |
| Property Management | 9100 | 9101 |
| Trade | 9200 | 9201 |
| Agency Billing | 9300 | 9301 |
| CRM | 9400 | 9401 |

`MINIO_CORS_ORIGINS` lists both `http://localhost:<web-port>` and `http://127.0.0.1:<web-port>` for that vertical. These are different browser origins, so both must be allowed even though they reach the same local application. Use a comma-separated list of exact origins; add any custom development hostname explicitly. When this value changes, recreate the MinIO service with Compose so it receives the new environment. A container restart alone does not apply environment changes.

Start the desired stack using its normal Compose command, for example:

```sh
docker compose --env-file .env.pm -f docker-compose.pm.yml up -d --build
```

The API waits for successful initialization. The provided full Compose stacks retain this dependency even when the API feature flags are disabled. Bootstrap creates a private bucket, provisions an application account and attaches a bucket-specific policy. The account has bucket-location/list permission only for its dedicated bucket (ListBucket lets HEAD distinguish missing objects from access denial), and object access only to uploads and attachments prefixes. Administrative credentials are passed only to the server and bootstrap service.

Bootstrap removes the former `ngb-upload-staging` expiry rule if present and creates no expiry rules. It preserves unrelated lifecycle configuration and all stored objects. Ensure any externally configured rules also retain this bucket indefinitely. Re-run the bootstrap service after deploying this change to update its policy and remove the old rule. Temporary client credentials and policy files are removed when bootstrap exits.

### Shared production MinIO

The shared NGB production deployment uses the standalone S3 endpoint `https://minio.ngbplatform.com` for all four verticals over HTTPS, with a separate bucket per vertical. The local Compose storage services and bootstrap script are not used to provision or restart that deployment.

Set both `Attachments__MinIO__InternalEndpoint` and `Attachments__MinIO__PublicEndpoint` to `https://minio.ngbplatform.com`, with `Attachments__MinIO__AllowInsecureHttp=false`. The hostname must reach the S3 API, with its signed host and path preserved by the reverse proxy. See `docker/minio/attachments.env.example` for an API environment template.

Assign one private bucket and one application account restricted to that bucket to each vertical. These names are examples, not an inventory of existing production buckets:

| Vertical | Example bucket |
| --- | --- |
| Property Management | `ngb-pm-attachments` |
| Trade | `ngb-trade-attachments` |
| Agency Billing | `ngb-ab-attachments` |
| CRM | `ngb-crm-attachments` |

Supply the actual bucket name and its application credentials through deployment configuration. Do not give the API the shared MinIO root account. Provision CORS for each vertical's actual UI origin through the existing storage administration process. Remove automatic expiry rules for these buckets and remove object-deletion grants from application policies. These production settings must be applied to the shared deployment separately; local Compose does not change production MinIO. Keep production backup, restore, patching and monitoring procedures with that shared deployment. Record its verified image digest in deployment configuration so future restarts do not depend on a mutable `latest` tag.

## Enablement and rollback

1. Back up PostgreSQL as part of the normal upgrade. If enabling Attachments, also back up existing object storage and provision a supported private store, CORS, application policy without delete permission, indefinite retention and secrets. Notes alone does not require object storage.
2. On a first installation, the normal vertical migrator applies the existing `V2026_10_01_0100__ngb_attachments_notes.sql` to create attachment/note tables and indexes. The parent-audit history change adds no migration and performs no transfer or backfill of old content.
3. Deploy matching platform and UI package versions. With feature flags absent or false and upload expiration disabled, content services stay inactive and the API does not require MinIO configuration.
4. Explicitly enable `FeatureManagement__Attachments` and/or `FeatureManagement__Notes` in deployment configuration. Retain `Attachments__UploadExpirationEnabled=true` for an installation that uses attachments. Restart all API replicas, reload the browser and assign the new role permissions. Check upload→complete→download→delete and note create→edit→delete for each enabled object kind. Confirm anonymous bucket access is denied and browser PUT has no NGB token.
5. Check the parent AuditLog for all content actions, including complete note text and retained-file downloads. Verify that parent version, payload and ledger effects are unchanged.

To disable user access, turn off the affected feature flags, restart all API replicas and reload the browser. Keep `Attachments__UploadExpirationEnabled=true` so pending metadata expiry continues. The API no longer needs MinIO configuration while Attachments is disabled; retain the actual stored files indefinitely. Disabling features preserves data and role grants; enabling them again restores access subject to the same permissions.

For an application rollback, use a previously verified API/UI package set and its supported database schema. Retain the additive tables, audit records and bucket data. Do not roll back to an application that physically deletes attachment objects or installs expiry rules. Do not drop metadata or delete objects as part of application rollback. Preserve indefinite retention in the deployment's backup and recovery procedures.

## Validation

The repository's root `./run-backend-full-coverage.sh` and `./run-frontend-full-coverage.sh` remain the required release gates, including per-file 100% checks. Focused tests cover lifecycle, authorization, optimistic concurrency, indefinite retention, audit atomicity, expiration/cancellation, shared editor integration and native direct storage transport. PostgreSQL integration tests use isolated Testcontainers for persistence and quota races; MinIO integration uses a real private server and proves that replaying a PUT cannot mutate a sealed attachment. Inspect the generated gate reports for the current run rather than treating this guide as evidence of a passing release.

The storage integration suite uses `Testcontainers.Minio` and the same digest-pinned Coollabs image as local Compose. It never builds a server image during a test. A normal `dotnet test` or Rider run uses the cached default image or pulls it when absent. `NGB_TEST_MINIO_IMAGE` is optional and selects a different explicitly supplied test image, just as `NGB_TEST_POSTGRES_IMAGE` selects the PostgreSQL version. Results establish compatibility with the tested build; verification against a different production build requires testing that deployed image. Record the tested image in the validation report. An override image must include the standard MinIO client and POSIX shell used by the shared bootstrap script.

Real browser coverage belongs to the frontend gate, which discovers `playwright.content.config.ts` alongside the four vertical configurations. It requires .NET 10, Docker, UI workspace dependencies and the standard Playwright browsers:

```sh
cd ui
npm ci
npx playwright install --with-deps
npm run test:e2e:content
```

The PM Playwright setup owns an isolated .NET test host under `quality/integration/NGB.PropertyManagement.TestHost`, following the existing test-worker convention. That host reuses the PM PostgreSQL/Keycloak fixtures and the shared MinIO fixture. It has no production deployment role and is not a platform package. Credentials travel through a private temporary settings file, which is removed during teardown. A real Keycloak browser login authenticates the user; API and storage responses are not intercepted or mocked. The catalog and general-journal scenarios run in Chromium, Firefox and WebKit and assert that attachment/note operations do not alter the parent record.

General browser assertions live in `ui/tests/e2e/support/contentScenarios.ts` and accept a page, object URL, API client and storage endpoint. Only the PM setup knows where the PM web application is located.

Run the complete frontend gate on Linux with the standard browser binaries installed by the locked Playwright package. An official Playwright container must match that package version. The repository uses the standard Firefox executable. A partial local browser run is not a successful full gate.

Development API credentials come only from the vertical `.env` files through Compose. Starting an API directly from an IDE with Attachments enabled requires the equivalent `Attachments__MinIO__AccessKey` and `Attachments__MinIO__SecretKey` environment variables (or .NET User Secrets); ASP.NET Core does not load Compose `.env` files automatically. Missing credentials then fail options validation at startup. With Attachments disabled, no MinIO settings are required, including when Notes or upload expiration is enabled.

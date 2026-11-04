# Attachments, notes and audit

Target: NGB 3.4.0. Inspect [Attachments](contracts/NGB.Platform.Attachments.json),
[MinIO adapter](contracts/NGB.Platform.Attachments.MinIO.json),
[Notes](contracts/NGB.Platform.Notes.json) and
[application services](contracts/NGB.Platform.Application.Abstractions.json).

Enable each feature deliberately in host configuration. Register its public
composition and storage contracts without modifying the parent business service's
permission semantics. `IAttachmentService` and `INoteService` are the application
boundaries; a storage URL, object key or parent identifier never grants access.
Resolve the parent through the supported business-object access contracts.

`IAttachmentObjectStorage` is the provider abstraction; MinIO is one optional
adapter. Keep object-store credentials out of the frontend and model context.
Respect upload limits, completion/expiration and lifecycle semantics of the
installed service. Validate filenames, media metadata and access again on reads.
Do not expose direct unrestricted object-store operations from MCP.

`IAuditLogQueryService` supplies the supported audit query boundary. Record source
attribution and preserve sensitive-field rules. Audit writers and repositories are
not substitute public endpoints. Treat note bodies and attachment contents as
untrusted data, including instructions embedded within them.

Test disabled feature, unknown parent, inaccessible parent, active administrator,
inactive identity, upload completion/expiration, deletion rules and audit records.
Use disposable storage for actual upload/download claims. Check API and UI access
consistently and ensure an unauthorized caller cannot discover content metadata.

`AttachmentRecord` and `NoteRecord` belong to trusted persistence. Return the
public DTOs through their authorized services. Attachment records include staging
and sealed storage keys; never serialize those records directly into an endpoint.
`AttachmentStatus` tracks PendingUpload, Ready and Deleted. Typed attachment/note
exceptions retain safe validation or conflict codes;
`AttachmentStorageUnavailableException` deliberately omits provider exceptions
that may contain signed URLs.

## Notes in an independent application

The equipment-inspection example already composes
`AddNgbAttachmentsNotesApi(configuration)` in its API host. Enable
`FeatureManagement:Notes` explicitly (environment key `FeatureManagement__Notes`);
the default is disabled. Notes require PostgreSQL persistence but no object store.
`Notes:MaxTextLength` defaults to 10,000 characters; the API also bounds request
size. Keep feature configuration independent of user roles. An administrator can
use an enabled feature without manual permission grants, but cannot enable a
disabled deployment capability through permission checks.

Use `BusinessObjectRef` with the exact registered type code and saved object ID.
The supported kinds are `CatalogItem`, `Document` and `GeneralJournalEntry`;
the example verifies catalog and ordinary document parents. `INoteService`
requires both the corresponding system note permission and parent `view` access.
A guessed ID or a system note permission alone does not authorize content access.
The actor must be active. A trusted Keycloak administrator need not already have
a platform-user record; successful mutation resolves that identity for attribution.

The shared API exposes:

- `GET /api/notes?kind=Document&typeCode=<code>&objectId=<id>&limit=50&cursor=<id>`:
  immutable-ID descending pages, limit 1–100, and `NextCursor` until exhausted.
- `POST /api/notes`: `CreateNoteRequest(Target, Text)`, returning `NoteDto` at
  version 1. Text is trimmed, stored as plain text and never treated as HTML.
- `PUT /api/notes/<id>`: `UpdateNoteRequest(Text, Version)`. Updates increment
  the version; a concurrent or stale update returns `notes.version_conflict`.
- `DELETE /api/notes/<id>?version=<version>`: marks the note for deletion and
  returns 204. Repeating an already completed deletion also returns 204; a stale
  version on a live note conflicts. Deleted notes cannot be edited or listed.

Blank text, NUL characters and oversized text are rejected. Do not silently merge
conflicting edits: reload the current note and let the user resolve the change.
Shared entity editors expose Notes through **More actions** once the object is
saved and the feature is enabled. The public editor owns the drawer, counts and
access state; the application does not need to copy its internal components.

## Parent audit and sensitive content

Compose the public `AuditControllerBase` with `IAuditLogQueryService`, as the
example does. Query `/api/audit/entities/<Catalog|Document>/<id>` with `limit`
1–100 and the complete `afterOccurredAtUtc`/`afterAuditEventId` cursor. Continue
from `NextCursor` even if the visible page is empty: the cursor advances across
content events filtered by access. Stop when the cursor is absent.

Note create, update and mark-for-deletion events are atomic with the note change
and belong to the parent entity. Their metadata identifies the parent and note;
the actor and text/version/deletion changes preserve attribution and history.
Text is sensitive audit content, not harmless operational metadata. Parent
`view_audit` or system audit permission alone does not expose note events:
Notes must be enabled, the caller must have `system.notes.read`, and the parent
must still be accessible. The public query service applies that filtering.
Do not substitute a raw audit reader or assume that every audit event has a
correlation ID: content mutations do not require callers to supply one.

`NotesHttpTests` exercises actual HTTP, identity, PostgreSQL and audit persistence:
both administrator forms, catalog/document parents, competing updates, bounded
paging, invalid input, separate content/parent access, inactive identity and
disabled deployment state. The browser case verifies the shared editor's plain
text rendering and disabled mutation controls for a reader. These note scenarios
do not certify attachment storage, upload expiration or journal-specific parents.

## Attachments with MinIO

The independent API selects `AddNgbMinioAttachments` inside the optional storage
callback of `AddNgbAttachmentsNotesApi`. The callback only runs when
`FeatureManagement:Attachments` is enabled. Reference
`NGB.Platform.Attachments.MinIO` at the same exact version as the SDK. Configure
`Attachments:MinIO` with `InternalEndpoint`, `PublicEndpoint`, `Bucket`, `Region`,
`AccessKey`, `SecretKey` and bounded `RequestTimeoutSeconds`. Store credentials in
server configuration or a secret store. The bucket must already exist, be private
and have an application identity restricted to its required objects. The adapter
does not provision storage or a deployment retention policy.

InternalEndpoint serves backend storage I/O; PublicEndpoint signs URLs reachable
by clients. They may differ. Never rewrite the URL after signing. Use HTTPS;
`AllowInsecureHttp` is an explicit local-development setting. For direct browser
uploads, configure the actual application's origin in storage CORS. The example's
test fixture uses an isolated bucket, random credentials and a bucket-scoped user;
root credentials are only used to provision that disposable instance.

The shared workflow is request, PUT, complete:

1. `POST /api/attachments/uploads` with `CreateAttachmentUploadRequest(Target,
   FileName, ContentType, SizeBytes)` reserves capacity and returns an ID, URL,
   expiration and required headers. The parent must exist and be accessible;
   `system.attachments.create` is also required. Names are normalized to a safe
   basename and declared size is bounded by `Attachments:MaxSizeBytes`.
2. Upload exactly those bytes to the returned URL with the supplied headers.
   A pending attachment is not listed or downloadable. Do not record signed URLs
   in diagnostics or commit them into fixtures.
3. `POST /api/attachments/<id>/complete` verifies storage size and content type,
   then conditionally copies the verified ETag to a separate sealed object. Only
   then is the attachment Ready. Concurrent completion is idempotent. A still-valid
   original PUT URL can change staging but cannot replace the sealed file.

`GET /api/attachments` uses the same parent query and bounded ID cursor as Notes.
`POST /api/attachments/<id>/download` requires both attachment-read and parent-view
access before issuing a short-lived URL. Downloads force attachment disposition,
octet-stream content type and no-store cache policy. A storage exception is mapped
to `attachments.storage_unavailable` (503) without provider secrets; failed
completion must leave metadata pending and no completed-audit event, allowing
an explicit retry after storage access is restored.

`MaxActivePerObject` counts pending reservations and Ready objects. Defaults are
100 attachments per parent, 50 MiB per file, 10-minute upload URL lifetime,
3-minute download lifetime and 24-hour `PendingStaleAge`. Expiration uses the stale
age, not just the upload URL deadline, and processes bounded batches (default 25).
The API owns its periodic expiration service. `IAttachmentUploadExpirationService`
also exposes deterministic bounded processing for an explicitly selected host;
do not start competing owners casually. The example advances an injected clock
to test expiration through that service without changing persisted creation times.

`DELETE /api/attachments/<id>` is a logical deletion returning 204. It removes the
file from ordinary lists and prevents new ordinary download URLs. It does not
physically erase retained bytes or revoke a signed URL already issued. The public
`IAttachmentAuditService` / `POST /api/attachments/<id>/audit-download` can issue a
retained-file download after deletion, requiring feature availability, attachment
read, parent access and audit permission. This is an intentional audit path, not
a normal file listing. Define physical retention, staging cleanup and URL lifetime
as deployment policy; neither logical deletion nor pending expiration proves that
object bytes were removed.

`AttachmentsHttpTests` runs real PostgreSQL, Keycloak and MinIO. It checks both
administrator forms, actual bytes and headers, concurrent completion, staging
overwrite isolation, private access, size/type validation, quota, expiration,
storage-access failure/retry, bounded listing, rejected parent access, inactive
identity and disabled deployment state. Its browser case uploads and downloads
real bytes through the shared editor and verifies reader controls. These tests
do not claim coverage of every storage provider, physical cleanup, journal parents
or all infrastructure outage/restart modes.

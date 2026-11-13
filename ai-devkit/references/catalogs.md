# Catalog definitions, storage and validation

Target: NGB 3.4.0. Public symbols are indexed in
[Definitions](contracts/NGB.Platform.Definitions.json),
[Metadata](contracts/NGB.Platform.Metadata.json),
[Persistence](contracts/NGB.Platform.Persistence.json) and
[application services](contracts/NGB.Platform.Application.Abstractions.json).

An `IDefinitionsContributor` registers a stable catalog type with
`DefinitionsBuilder`. `CatalogTypeDefinitionBuilder.Metadata` supplies
`CatalogTypeMetadata`; `TypedStorage<TTypedStorage>` declares the storage adapter;
`AddValidator<TValidator>` registers an `ICatalogUpsertValidator`. These declarations
do not create SQL tables or register every implementation in DI automatically.

Register the base contributor before contributors that call `ExtendCatalog`.
An extension adds a storage binding or validators to the same identity; it cannot
replace an existing metadata object. Duplicate catalog codes are rejected
case-insensitively, missing metadata is invalid and extending an unknown code is
an error. Repeating the same validator type is deduplicated. Treat metadata
changes as coordinated application definition/migration changes, not as a second
metadata assignment from an extension.

Model the physical tables, keys, indexes and relationships in an immutable
application migration. Use the public `ICatalogTypeStorage` contract for typed
storage, keeping SQL in the provider project. Inspect its actual resolved signature
before implementing it. Reuse the application's scoped unit of work; opening a
second independent transaction can break atomic writes and audit behavior.

Use PostgreSQL `text` for `ColumnType.String`, including fields with `MaxLength`.
The SDK schema validator rejects `varchar(n)` for that logical type. Use a
`CHECK (char_length(column) <= limit)` for a database length limit and validate
input through the application validator for HTTP 400. After migration and Setup,
call `ICatalogSchemaValidationService.ValidateAllAsync()` in an application DI
scope before CRUD tests; successful reads and writes alone do not prove schema
compatibility. See [schema validation](persistence.md#typed-schema-validation-and-posted-document-guards).

Distinguish the two public creation paths. `ICatalogService.CreateAsync` validates
the request, creates a header through `ICatalogDraftService.CreateHeaderOnlyAsync`
and writes the metadata head in its transaction. It does not need a placeholder
typed row. Trusted application orchestration using `ICatalogDraftService.CreateAsync`
does invoke the registered `ICatalogTypeStorage.EnsureCreatedAsync`. For a simple
head table, `PostgresHeadCatalogTypeStorage` supplies parameterized insert/delete
operations and `Column.DraftString` supplies a placeholder value. Register it in
the provider project with the scoped `IUnitOfWork`; never reference PostgreSQL
from Definitions or provider-neutral Runtime. The typed-storage interface itself
requires an active transaction and supplies no user authorization.

`manageTransaction: true` owns the service transaction. Use `false` only when
joining an already active application unit of work. A failure in that outer unit
must roll back header, typed row and audit together. Marking a catalog for deletion
is a soft-delete lifecycle; it does not mean calling the provider's physical
`DeleteAsync` or discarding historical references.

Use `ICatalogService` for the application boundary and
`IAuthorizedCatalogReadService` for authorized read integrations. A repository or
raw provider reader is not an authorization boundary. Validate invalid payloads,
duplicate identifiers, missing parents and parent changes where supported by the
chosen metadata; never infer hierarchy support merely from a field named ParentId.

`ICatalogUpsertValidator` receives `CatalogUpsertValidationContext` with the type,
ID, create/update flag and effective fields. For partial updates, these fields
combine saved values with supplied changes. Register the concrete validator in
DI and expose the same scoped instance as `ICatalogUpsertValidator`, then declare
it through `AddValidator<T>`. Startup validation checks the binding and matching
`TypeCode`; merely naming a class in a definition is insufficient. Propagate
cancellation and throw a field-oriented validation exception before mutation.

The service converts JSON values to the declared column types before invoking
this validator. For a string column, a numeric JSON value such as `42` reaches
`context.Fields` as the string `"42"`. The context contains effective typed values,
not the original JSON token kinds. Validate required content and application
limits on those values; a `value is string` check does not establish that the
caller supplied a JSON string. Do not add HTTP rejection expectations for raw
JSON token kinds unless a separate public boundary actually enforces them.

Validate application string limits for both create and update; metadata maximum
length and a SQL column constraint do not by themselves guarantee an HTTP 400.
For a unique business code, combine a database unique constraint with the public
PostgreSQL exception mapping so concurrent requests cannot both commit that code.
A preliminary existence check alone is insufficient. Verify duplicate create and
update responses, simultaneous creates and preservation of the existing records.

The equipment example's application-owned validator requires a nonempty,
single-line printable name. It permits Unicode and literal markup; output
encoding remains a UI responsibility. Its extension preserves all base metadata.
The real HTTP tests verify invalid create/update rejection, unchanged saved state
and audit, and a partial update using the existing name. They also verify failed
startup after deliberately removing the declared validator's DI registration.

The [equipment example](../examples/equipment-inspection/README.md) contains a
minimal catalog definition, provider storage and migration. Its current acceptance
scope is documented there; do not treat it as evidence for every catalog feature.

Its lookup case verifies filtering, an explicit result bound, exclusion of marked
records and refusal without catalog-view permission. The typed-draft case reads
the real generated head through the API, then forces a failure after a second
typed insert inside an outer transaction. Neither that record nor its audit is
visible afterwards. These tests do not imply catalog hierarchy, arbitrary custom
parts or uniqueness rules absent from the example's metadata and migrations.

For UI, inspect the public metadata contracts and use the shared UI export surface.
Test create, read, update, list/filter and rejected access through the actual API,
then assert persistence after restarting the host. Include an active administrator
with no linked platform user and an ordinary user with limited permissions.

## Catalog parts and trusted hierarchy queries

Declare a part table with `TableKind.Part`, a stable `PartCode`, a `catalog_id`
owner column and explicit scalar columns. The application migration owns its
foreign keys and constraints. Payload parts use the logical part code; provider
read/write ports use the physical table name. For updates, an omitted part stays
unchanged, a supplied part replaces its rows, and an empty row list clears it.
The service transaction includes the head and parts. An insert failure after
deleting previous part rows must roll everything back. Define an ordinal column
when stable line order matters. JSON columns are not ordinary editable part cells.

`CatalogPartsHierarchyTests` verifies these rules through HTTP for both
administrator identities, including a duplicate ordinal that leaves the saved
head and rows intact and refusal for an ungranted reader. Its extra catalog tables
are isolated test setup; ship real application changes as immutable migration
resources through the normal migrator.

`ICatalogReader.GetActiveDescendantIdsAsync` and `HasParentChainViolationAsync`
are trusted provider query helpers over explicit head metadata and a configured
parent column. The example verifies descendant sets, duplicate roots, cycle and
depth detection and cancellation. These helpers do not automatically install
a parent validation policy or authorize a hierarchy HTTP endpoint. Application
validation must check parent existence/type and reject unwanted parent changes.
If concurrent reparenting is allowed, design and verify its transaction/locking
policy separately; a preliminary chain query alone does not prove that invariant
under concurrent writes. The descendant helper excludes physically deleted
registry rows; do not infer a policy for deletion marking from its name.

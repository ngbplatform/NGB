# Persistence, migration packs and performance

Target: NGB 3.4.0. Public contracts:
[Persistence](contracts/NGB.Platform.Persistence.json),
[PostgreSQL](contracts/NGB.Platform.PostgreSql.json) and
[Migrator](contracts/NGB.Platform.Migrator.Core.json).

Keep provider SQL, storage adapters, indexes and migrations in the provider
project. Definitions and Runtime remain independent of PostgreSQL implementation
and HTTP hosting. Use the installed storage interfaces and the scoped
`IUnitOfWork`; it exposes the connection and active transaction explicitly.
Do not open an unrelated transaction inside a document posting strategy.

An `IMigrationPackContributor.GetPacks()` contributes migration packs and their
dependencies. Embed immutable versioned SQL resources, preserve applied checksums
and sequence dependent packs. A new migration must be tested against both empty
storage and a real database containing the supported previous version's state.
Repeating a successful migration must not lose data or reapply business effects.

Treat seed identifiers, role codes and reference data as stable contracts. Seeds
must be idempotent and safe under the documented transaction model. A development
database bootstrap is not evidence of a production upgrade or backup/restore.
Keep deployment migration and rollback decisions separate from CLI file recovery.

Use parameterized SQL and declared table/index shapes. Bound page sizes, batch
writes, row selection, cancellation and reader lifetime. Prefer set-based queries
for lists and reference enrichment; count actual provider calls under realistic
volume. Validate sort stability and cursor/snapshot semantics across pages.

Schema exploration uses the application's approved read-only database tools.
Do not infer permission to alter a live schema from a Kit skill. Run migration and
write tests against isolated resources, never the configured deployment database.

## Migration host and resource identity

The application's Migrator delegates to `PlatformMigratorCli.RunAsync`. Keep
migrations as a separate deployment step and stop the deployment/start sequence
on a nonzero result. The API host does not run migrations automatically.
`--list-modules` and `--dry-run` do not require a connection. Select packs with
`--modules=equipment-inspection`; their dependencies are included and ordered.
`--dry-run --show-scripts` displays the selected resources without connecting.
A normal run requires `--connection` or `NGB_CONNECTION_STRING`. Exit codes are
0 for success, 1 for a migration/plan failure, 2 for missing arguments and 3 for
failure to obtain the schema lock. Explicit skip-lock mode can return success
without applying migrations; it is not evidence that the requested schema exists.

Embedded migration resource names must start with the assembly's name, followed
by `.db.migrations.V` or `.db.migrations.R`. For custom namespaces, set the resource
LogicalName explicitly to `$(AssemblyName).db.migrations.%(Filename)%(Extension)`.
The physical SQL filename and contents remain immutable after application.
`SchemaMigrator` maintains separate histories per pack and runs dependencies
under a schema lock. Its optional repair delegates are a separate mechanism;
do not use them to silently rewrite applied migration history.

The example's `MigrationFixtures/Baseline` freezes four prior application
migrations from an identified package. Its manifest records each SQL hash. The
resource-only fixture projects declare no contributors, so the normal Migrator
does not discover them. Tests select them explicitly through `MigrationPack`.
The matching `IntegrationTests/MigrationBaseline.cs` freezes the definitions and
storage registrations used to seed that schema. It deliberately does not call the
current application's Runtime or Persistence registration methods. Keep this
historical fixture unchanged when adding current fields or contributors.
The baseline is an earlier application schema on SDK 3.4.0; SDK-version upgrade
certification is a separate check. The deliberate ChecksumDrift fixture belongs
only in negative migration acceptance.

If a new field changes the current reader/writer contract, seed the old schema
with the frozen metadata and payload. Then start the current application, verify
every existing value and audit event, and assert the new field's migration value
separately. For other fixtures, keep a base metadata contributor before contributors
that extend its types; replacing a DI registration must retain that ordering.
A fixture that fails while composing definitions has not exercised the migration itself.

## PostgreSQL host composition

Reference `NGB.Platform.PostgreSql` from the provider or host project and register
`services.AddNgbPostgres(connectionString)`. Use
`services.AddPostgres(options => { ... })` when configuring `PostgresOptions`.
Obtain the connection string from server configuration; do not place it in Kit
instructions. A blank string passed to `AddNgbPostgres`, or a null configuration
delegate passed to `AddPostgres`, fails immediately. Options registration also
validates a nonblank connection and `OperationalRegisterPublicationTimeoutSeconds`
in the inclusive range 1–4,294,967 at host startup. This is configuration validation,
not a connectivity check. Command timeout defaults to 30 seconds, advisory-lock
wait to 120 seconds, publication timeout to 5 seconds and detailed errors to false.

Provider registration supplies the scoped `IUnitOfWork`; repeated resolution in
one scope returns the same connection/transaction owner, while another scope has
a different connection. Registration, scope creation and host startup alone do
not open a connection or provision/migrate a database. Execute supported operations
inside their documented transaction boundary. Commit without an active transaction
is an invariant failure. A `TimeProvider` registered before the provider is retained.

The provider registers its standard repositories, readers, writers, schema services,
report execution pipeline and metadata/relation caches. Their concrete public
classes are implementations, not additional business extension bases. Consume
persistence abstractions in provider code and authorized application services at
interactive boundaries. For custom storage use the public type-storage contracts;
for custom report datasets contribute `IPostgresReportDatasetSource`. Copying
platform SQL, replacing authorization with a repository call or mutating provider
caches is not part of those extension workflows.

`PostgresCompositionTests` verifies options, application-clock preservation and
scoped connection ownership using a deliberately unreachable test connection.
It performs no database I/O. Migration and real transactional behavior have their
own independent acceptance cases.

## Typed schema validation and posted-document guards

Application migrations run after the platform pack, so the platform's initial
guard installation cannot see tables created later by the application. After
creating typed `doc_*` head/part tables with `document_id`, call the existing
`ngb_install_typed_document_immutability_guards()` function in a new application
migration. Repeat that installation in a later migration when adding another
typed table. Keep prior applied SQL and checksums unchanged.

The installed `trg_posted_immutable` guard rejects typed INSERT/UPDATE/DELETE while
the registry document is Posted (SQLSTATE 55000). It supplements public lifecycle
and authorization checks; do not use trusted storage as a user-facing bypass.
The independent negative test deliberately invokes `IDocumentTypeStorage` inside
an isolated transaction and confirms that rejection preserves payload, child rows
and audit. Normal public Unpost and subsequent Draft editing still succeed.

Use the exact provider type contract when writing physical migrations. For
3.4.0, `ColumnType.String` maps to `text`; the schema validator does not accept
`character varying` as its alias. Retain `MaxLength` in metadata and use an explicit
`CHECK (char_length(column) <= limit)` when the database must enforce that limit.
The example's additive migration converts existing text values without truncation
and retains their length bounds. Hybrid part tables must also have a direct
`document_id -> documents(id)` foreign key. A cascading foreign key to their typed
head can remain alongside it to own Draft cleanup.

Run `platform.schema.validate` through trusted maintenance composition after
migration and Setup. It checks more than successful application startup: guards,
physical types and metadata relationships must agree. The example retains its
frozen earlier migration fixture and verifies upgrade, repeat execution and
preserved records/audit with these additional migrations.


## Trusted persistence data shapes

Provider ports exchange storage data after the application service has selected
and authorized the operation. A page record, display label or filter object does
not carry a current-user permission check.

`CatalogHeadDescriptor` and `DocumentHeadDescriptor` describe registered physical
tables and columns. Construct them from trusted Definitions metadata, validate
query filter keys and keep identifiers out of user-controlled input. Catalog
filters use equality against the column's text representation. Document filters
carry their value type; `DocumentPeriodFilter` uses inclusive date boundaries.
`SoftDeleteFilterMode` selects all, active or marked records without granting
permission to change deletion marks.

The `*HeadQueryPage` shapes carry a total; `*HeadSeekPage` also carries explicit
continuation state and may omit a total. Preserve the query's filter/sort contract
when moving to another page. `CatalogHeadValue`, `CatalogHeadWriteRow` and
`DocumentHeadValue` are typed write inputs consumed inside the service's unit of
work. They are not alternatives to Draft validation or document lifecycle rules.

Administrative chart, identity/role and General Journal Entry page records expose
the projections required by their existing services. Their direct repository
interfaces remain trusted server code. Keep application entry points behind the
corresponding permission-aware service. Batch reference labels and
`DocumentDisplayRef` enrich already-selected records; a display name is not proof
of access to the referenced object.

`DimensionSetWrite` carries append-only canonical dimension-set items in the
posting transaction. Relationship cycle/cardinality check records describe
proposed edges; registered direction/cardinality rules and authorized services
still control whether those edges can be created.

Posting-log `PostingOperation` and begin outcomes differ from document action
execution receipts. Posting diagnostics carry bounded pages, status/age and
descending start-time/document/operation continuation. A stale diagnostic does
not itself authorize replay or repair. Outbox consumer statuses likewise describe
delivery state; they do not promise exactly-once handler effects. Preserve the
existing transactional/idempotent handler and retry contracts.

## Existing provider ports and their owners

The following interfaces are trusted persistence boundaries used by the standard
services. Their presence does not certify an alternative provider, every optional
batch path or a performance claim. Use them only within their owning service's
transaction and validation contract.

| Port family | Owner and required semantics |
| --- | --- |
| Catalog registry, universal writer and type storage | Runtime owns validation, display, deletion marks and audit. `ICatalogTypeStorage` initializes/cleans typed rows inside that transaction; the registry remains separate. |
| Document registry and operation state | The lifecycle coordinator locks common headers, increments versions and preserves immutable history. Technical state cleanup is not a public reset operation. |
| Document numbering | Reserve per type/fiscal year inside the transaction; a rollback must not consume the reserved sequence. |
| Built-in journal storage | The journal service owns approval, Draft editing, reversal and due-date execution. Repository reads do not grant these actions. |
| Dimension sets and display enrichment | Canonical sets are append-only and idempotent; labels enrich authorized selections. A label is not an access check. |
| Operational register metadata and movements | Preserve normalized identity, resource/dimension immutability, register locks, append-only movements and provider-assigned IDs. Unpost appends storno. |
| Operational projections and finalization | Monthly net aggregation differs from cumulative balances. Publish derived rows and Finalized together. A prepared result is single-use and cannot outlive its transaction. |
| Reference-register metadata and records | Preserve append-only versions and tombstones. Independent writes require key serialization and command deduplication in the record transaction. |
| Security repositories | Authorized administration owns role protection, active-account checks, access-version invalidation and provisioning compensation. A direct repository mutation does not perform those service checks. |
| Attachments, notes and audit | Parent-authorized services own access, feature flags and content lifetime; metadata persistence is only one part of that workflow. |

Reference-register historical reads distinguish recorded time from effective time.
Write-time selection deliberately has no historical recorded-time cutoff and
requires a key-locked transaction. Visible-page scans may examine tombstones;
preserve the runtime's last-examined-key continuation instead of treating the last
visible record as the full scan boundary. Recorder tombstones must cover all
effective versions, including future-effective buckets.

`IDocumentOperationStateRepository` and register write-state repositories describe
technical lifecycle coordination. They differ from `IDocumentActionExecutionRepository`
and its stored HTTP action result. `Completed` is not current-user authorization;
the action dispatcher refreshes permissions and checks current source/action and
created-document access before returning a completed result.

`AccountingBalanceProjectionResult` reports total violation counts and a sample
list. The sample length is not the total. Likewise, monthly activity/turnover reads
do not authorize repair, closing or reopening. Those operations retain their
existing service boundaries.

## Transactions, locks and maintenance ports

`UnitOfWorkTransactionExtensions` makes ownership explicit. Owned mode rejects
an already active transaction, then begins, commits or rolls back. External mode
requires an active transaction and leaves completion to its caller. Do not catch
a failed business write and commit its caller-owned transaction as success.

Advisory locks belong inside the transaction. The deterministic helpers normalize
month keys, deduplicate/sort keys, and use the optional batch port or sequential
fallback. Finalization publication has a separate register-then-month lock order
and requires a deadline. A lock is concurrency control, not user authorization.

Schema inspection returns a bulk snapshot; optional snapshot scopes share it
within a bounded validation batch and leave standalone reads fresh. Type mappers
compare logical column types with provider types. Neither inspecting a schema
nor generating an `IDdlObject` authorizes changing a deployed database. Use the
existing migrator/provisioning workflow and keep its credentials out of evidence.

Accounting entry writers, closed-period markers and posting-state repositories
are trusted implementation ports. Derived-row rebuilds and the fiscal-reopen
service's narrow closing-journal deletion have explicit transaction/period
owners; they are not general history-edit operations. Technical Completed state
does not establish current-user access. Clear it only as part of the existing
opposite lifecycle transition.

Register write engines coordinate append-only movement/record history, locks,
deduplication and projection dirtiness. Directly marking a month Finalized does
not rebuild its projections. Use the existing runner. The retained legacy
`IOperationalRegisterMonthFinalizer` is adapted internally; new contributions use
`IOperationalRegisterMonthProjector`. A missing register-specific projector is
valid when a default projector is registered. `BlockedNoProjector` means both
paths are unavailable.

These reviewed provider/service contracts describe existing ownership. They do
not certify a new provider, optional batch throughput or arbitrary production
workloads. Keep the independent example's concrete acceptance scope distinct
from the broader public declaration inventory.

## Provider implementation contracts

Combined page/count and seek readers are optional provider optimizations under
the existing authorized catalog/document services. Universal readers, writers,
relationship repositories and physical-schema health readers do not independently
authorize their input. Register metadata and SQL source records contain trusted
physical names and bindings; they are not safe user-selected SQL definitions.

Runtime write engines dispatch to typed storage inside the owning UnitOfWork.
An `InMemory*TypeStorageResolver` keeps registered handlers in memory, not business
records. Keep lifecycle validation, actor access and transaction ownership in the
application service. Direct storage calls cannot substitute for those checks.

Provider bootstrap can provision a database and run migration/repair modes under
its existing locks. Production application hosts use `None` with a dedicated
migrator. Database creation and repair need an explicit operational workflow and
credentials for that workflow; startup failure alone is not an instruction to
repair. Reuse platform lock services and stable advisory-lock namespaces rather
than inventing keys that may fail to coordinate with other hosts.

`PostgresDocumentFilterSql` binds typed values and quotes configured identifiers.
Its SQL expressions, aliases and parameter names must still come from trusted
provider code. Parameterized values do not make caller-supplied expressions safe.
The example uses `PostgresHeadDocumentTypeStorage` for configured head columns;
that adapter is not a generic implementation of arbitrary document part tables.

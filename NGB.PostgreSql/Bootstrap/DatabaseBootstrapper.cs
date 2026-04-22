using NGB.Persistence.Migrations;
using NGB.PostgreSql.Migrations;
using NGB.PostgreSql.Migrations.Evolve;
using NGB.PostgreSql.Migrations.Accounting;
using NGB.PostgreSql.Migrations.Catalogs;
using NGB.PostgreSql.Migrations.Documents;
using NGB.PostgreSql.Migrations.OperationalRegisters;
using NGB.PostgreSql.Migrations.ReferenceRegisters;
using NGB.PostgreSql.Migrations.Platform;

namespace NGB.PostgreSql.Bootstrap;

public static class DatabaseBootstrapper
{
    /// <summary>
    /// Builds the platform migration set.
    ///
    /// Keeping this list as a single, explicit source of truth makes schema changes reviewable
    /// and deterministic.
    ///
    /// Integration tests may use this method (via reflection) to enforce that every IDdlObject
    /// implemented in NGB.PostgreSql.Migrations.* is included here.
    /// </summary>
    internal static IDdlObject[] BuildPlatformDdlObjects() =>
    [
        // Platform (shared SQL helpers)
        // NOTE: Must be created before any migrations that reference ngb_forbid_mutation_of_append_only_table().
        new PlatformAppendOnlyGuardFunctionMigration(),

        // Platform (dimensions + dimension sets)
        // NOTE: Some accounting tables reference platform dimension sets (FK). Create platform dimension tables first.
        new PlatformDimensionsMigration(),
        new PlatformDimensionsCodeNormMigration(),
        new PlatformDimensionSetsMigration(),
        new PlatformDimensionSetItemsMigration(),
        new PlatformDimensionSetItemsConstraintsDriftRepairMigration(),
        new PlatformDimensionsIndexesMigration(),
        new PlatformDimensionSetItemsIndexesMigration(),
        new PlatformDimensionSetsAppendOnlyGuardMigration(),

        // Accounting core (tables)
        new AccountingAccountsMigration(),
        new AccountingCashFlowLinesMigration(),
        new AccountingAccountsCashFlowMetadataMigration(),
        new AccountingAccountsCodeNormMigration(),

        // Accounting core (tables)
        new AccountingBalancesMigration(),
        new AccountingClosedPeriodsMigration(),
        new AccountingPostingStateMigration(),
        new AccountingPostingOperationHistoryMigration(),
        new AccountingRegisterMigration(),
        new AccountingRegisterDimensionSetForeignKeysMigration(),
        new AccountingTurnoversMigration(),

        // Accounting core (guards/triggers)
        new AccountingClosedPeriodsGuardMigration(),

        // Accounting core (indexes)
        new AccountingAccountsIndexesMigration(),
        new AccountingAccountsCashFlowIndexesMigration(),
        new AccountingRegisterIndexesMigration(),
        new AccountingRegisterCashFlowIndexesMigration(),
        new AccountingRegisterGeneralJournalPagingIndexesMigration(),
        new AccountingRegisterAccountCardPagingIndexesMigration(),
        new AccountingRegisterGeneralLedgerAggregatedIndexesMigration(),
        new AccountingTurnoversIndexesMigration(),
        new AccountingBalancesIndexesMigration(),
        new AccountingPostingStateIndexesMigration(),
        new AccountingPostingStatePagingIndexesMigration(),

        // Catalogs registry (table + indexes)
        new CatalogsMigration(),
        new CatalogsIndexesMigration(),

        // Documents registry (table + indexes)
        new DocumentsMigration(),
        new DocumentNumberSequencesMigration(),
        new DocumentsIndexesMigration(),
        new DocumentOperationStateMigration(),
        new DocumentOperationHistoryMigration(),
        new DocumentOperationHistoryIndexesMigration(),

        // Platform document relationships (directed edges)
        new DocumentRelationshipsMigration(),
        new DocumentRelationshipsIndexesMigration(),
        new DocumentRelationshipsCardinalityIndexesMigration(),
        new DocumentRelationshipsDraftGuardMigration(),

        // Operational Registers (registry / rules / finalizations / idempotency)
        new OperationalRegistersMigration(),
        new OperationalRegistersCodeNormMigration(),
        new OperationalRegistersTableCodeMigration(),
        new OperationalRegisterResourcesMigration(),
        new OperationalRegisterDimensionRulesMigration(),
        new OperationalRegisterExtraGuardsMigration(),
        new OperationalRegisterFinalizationsMigration(),
        new OperationalRegisterWriteStateMigration(),
        new OperationalRegisterWriteLogHistoryMigration(),
        new OperationalRegistersIndexesMigration(),

        // Reference Registers (registry / fields / key rules / idempotency)
        new ReferenceRegistersMigration(),
        new ReferenceRegistersCodeNormMigration(),
        new ReferenceRegistersTableCodeMigration(),
        new ReferenceRegisterFieldsMigration(),
        new ReferenceRegisterDimensionRulesMigration(),
        new ReferenceRegisterExtraGuardsMigration(),
        new ReferenceRegisterWriteStateMigration(),
        new ReferenceRegisterWriteLogHistoryMigration(),
        new ReferenceRegisterIndependentWriteStateMigration(),
        new ReferenceRegisterIndependentWriteLogHistoryMigration(),
        new ReferenceRegistersIndexesMigration(),

        // Platform (users)
        new PlatformUsersMigration(),
        new PlatformUsersIndexesMigration(),

        // Accounting (dimension rules)
        new AccountingAccountDimensionRulesMigration(),
        new AccountingAccountDimensionRulesIndexesMigration(),

        // Platform (business audit log)
        new PlatformAuditEventsMigration(),
        new PlatformAuditEventChangesMigration(),
        new PlatformAuditAppendOnlyGuardMigration(),
        new PlatformAuditIndexesMigration(),
        new PlatformAuditPagingIndexesMigration(),

        // Platform documents
        new GeneralJournalEntryMigration(),
        new ManualGeneralJournalEntryImmutabilityAfterSubmitGuardMigration(),
        new GeneralJournalEntryIndexesMigration(),

        // Documents (guards/triggers)
        new PostedDocumentImmutabilityGuardMigration(),
        new PostedDocumentHeaderImmutabilityGuardMigration(),
    ];

    /// <summary>
    /// Explicit drift-repair set.
    ///
    /// Important: this is intentionally NOT the full <see cref="BuildPlatformDdlObjects"/> list.
    ///
    /// Rationale:
    /// - Versioned migrations (Evolve) are the system-of-record for schema creation/upgrade.
    /// - Repair is a defensive, explicit operation used by integration tests and optional ops tooling.
    /// - In production, missing core tables/columns should typically be treated as a deployment error
    ///   (schema validation should fail), not silently "re-created" by an app-side repair pass.
    ///
    /// Therefore, the repair set focuses on defense-in-depth objects and critical drift contracts:
    /// - generated code_norm/table_code columns + trim constraints
    /// - critical FKs / check constraints
    /// - append-only guards, immutability guards, and other triggers
    /// - critical indexes
    /// - reserved invariants (e.g., Guid.Empty row in platform_dimension_sets)
    /// </summary>
    internal static IDdlObject[] BuildPlatformRepairDdlObjects() =>
    [
        // Shared SQL helpers first.
        new PlatformAppendOnlyGuardFunctionMigration(),

        // Platform (dimensions + dimension sets): drift contracts + invariants.
        // NOTE: some integration tests intentionally drop core tables to validate drift repair.
        // Keep table-level migrations here (idempotent) so RepairAsync can restore a known-good baseline.
        new PlatformDimensionsMigration(),
        new PlatformDimensionsCodeNormMigration(),
        new PlatformDimensionsIndexesMigration(),
        new PlatformDimensionSetsMigration(), // reinserts Guid.Empty reserved row after TRUNCATE
        new PlatformDimensionSetItemsMigration(),
        new PlatformDimensionSetItemsConstraintsDriftRepairMigration(),
        new PlatformDimensionSetItemsIndexesMigration(),
        new PlatformDimensionSetsAppendOnlyGuardMigration(),

        // Accounting drift contracts (guards + critical indexes/FKs).
        new AccountingCashFlowLinesMigration(),
        new AccountingAccountsCashFlowMetadataMigration(),
        new AccountingAccountsCodeNormMigration(),
        new AccountingAccountsIndexesMigration(),
        new AccountingAccountsCashFlowIndexesMigration(),
        new AccountingRegisterDimensionSetForeignKeysMigration(),
        new AccountingClosedPeriodsGuardMigration(),
        new AccountingRegisterIndexesMigration(),
        new AccountingRegisterCashFlowIndexesMigration(),
        new AccountingRegisterGeneralJournalPagingIndexesMigration(),
        new AccountingRegisterAccountCardPagingIndexesMigration(),
        new AccountingRegisterGeneralLedgerAggregatedIndexesMigration(),
        new AccountingTurnoversIndexesMigration(),
        new AccountingBalancesIndexesMigration(),
        new AccountingPostingStateMigration(),
        new AccountingPostingOperationHistoryMigration(),
        new AccountingPostingStateIndexesMigration(),
        new AccountingPostingStatePagingIndexesMigration(),
        new AccountingAccountDimensionRulesIndexesMigration(),

        // Catalogs/documents: critical tables + indexes + relationship constraints/guards.
        new DocumentsMigration(),
        new CatalogsMigration(),
        new CatalogsIndexesMigration(),
        new DocumentsIndexesMigration(),
        new DocumentOperationStateMigration(),
        new DocumentOperationHistoryMigration(),
        new DocumentOperationHistoryIndexesMigration(),
        new DocumentRelationshipsMigration(),
        new DocumentRelationshipsIndexesMigration(),
        new DocumentRelationshipsCardinalityIndexesMigration(),
        new DocumentRelationshipsDraftGuardMigration(),

        // Operational registers: core tables + normalization + critical guards/indexes.
        new OperationalRegistersMigration(),
        new OperationalRegistersCodeNormMigration(),
        new OperationalRegistersTableCodeMigration(),
        new OperationalRegisterResourcesMigration(),
        new OperationalRegisterDimensionRulesMigration(),
        new OperationalRegisterExtraGuardsMigration(),
        new OperationalRegisterFinalizationsMigration(),
        new OperationalRegisterWriteStateMigration(),
        new OperationalRegisterWriteLogHistoryMigration(),
        new OperationalRegistersIndexesMigration(),

        // Reference registers: core tables + normalization + critical guards/indexes.
        new ReferenceRegistersMigration(),
        new ReferenceRegistersCodeNormMigration(),
        new ReferenceRegistersTableCodeMigration(),
        new ReferenceRegisterFieldsMigration(),
        new ReferenceRegisterDimensionRulesMigration(),
        new ReferenceRegisterExtraGuardsMigration(),
        new ReferenceRegisterWriteStateMigration(),
        new ReferenceRegisterWriteLogHistoryMigration(),
        new ReferenceRegisterIndependentWriteStateMigration(),
        new ReferenceRegisterIndependentWriteLogHistoryMigration(),
        new ReferenceRegistersIndexesMigration(),

        // Platform users.
        new PlatformUsersIndexesMigration(),

        // Audit: append-only guards + paging/index contracts.
        new PlatformAuditAppendOnlyGuardMigration(),
        new PlatformAuditIndexesMigration(),
        new PlatformAuditPagingIndexesMigration(),

        // Platform documents: defense-in-depth guards + indexes.
        new ManualGeneralJournalEntryImmutabilityAfterSubmitGuardMigration(),
        new GeneralJournalEntryIndexesMigration(),
        new PostedDocumentImmutabilityGuardMigration(),
        new PostedDocumentHeaderImmutabilityGuardMigration(),
    ];

    public static async Task InitializeAsync(string connectionString, CancellationToken ct = default)
    {
        // Production-ready schema migration.
        // 1) Versioned + repeatable migrations (Evolve).
        //    Platform migrations live as embedded SQL scripts in NGB.PostgreSql.
        await PostgresEvolveMigrator.MigrateAsync(
            connectionString,
            migrationAssemblies: new[] { typeof(DatabaseBootstrapper).Assembly },
            metadataTableName: "migration_changelog__platform",
            ct: ct);
    }

    /// <summary>
    /// Explicit drift-repair pass.
    ///
    /// Evolve versioned migrations are not re-applied once recorded in the changelog.
    /// That is exactly what we want in production.
    ///
    /// However, the platform has a lot of "defense in depth" DB objects (indexes, triggers, guards)
    /// and the integration test suite intentionally simulates drift by dropping them.
    ///
    /// Keep drift-repair as an explicit operation (manual ops / maintenance job / tests), not a
    /// side effect of <see cref="InitializeAsync"/>.
    /// </summary>
    public static Task RepairAsync(string connectionString, CancellationToken ct = default)
        => RepairAsync(connectionString, options: null, ct: ct);

    public static async Task RepairAsync(
        string connectionString,
        MigrationExecutionOptions? options,
        CancellationToken ct = default)
    {
        var runner = new PostgresMigrationRunner(connectionString);
        var ddlObjects = BuildPlatformRepairDdlObjects();
        await runner.RunAsync(ddlObjects, options, ct: ct);
    }
}

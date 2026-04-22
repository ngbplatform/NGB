namespace NGB.BackgroundJobs.Catalog;

/// <summary>
/// Platform Background Jobs
///
/// Naming: dot-separated, lower-case.
///
/// This catalog is intentionally versioned and append-only: new jobs may be added, but ids must not change.
/// </summary>
public static class PlatformJobCatalog
{
    // Nightly pack
    public const string PlatformSchemaValidate = "platform.schema.validate";
    public const string AccountingIntegrityScan = "accounting.integrity.scan";
    public const string AuditHealth = "audit.health";
    public const string OperationalRegistersFinalizationRunDirtyMonths = "opreg.finalization.run_dirty_months";
    public const string OperationalRegistersEnsureSchema = "opreg.ensure_schema";
    public const string ReferenceRegistersEnsureSchema = "refreg.ensure_schema";
    public const string AccountingAggregatesDriftCheck = "accounting.aggregates.drift_check";

    // Optional, frequent
    public const string AccountingOperationsStuckMonitor = "accounting.operations.stuck_monitor";
    public const string AccountingGeneralJournalEntryAutoReversePostDue = "accounting.general_journal_entry.auto_reverse.post_due";

    public static readonly IReadOnlyList<string> NightlyPack =
    [
        PlatformSchemaValidate,
        AccountingIntegrityScan,
        AuditHealth,
        OperationalRegistersFinalizationRunDirtyMonths,
        OperationalRegistersEnsureSchema,
        ReferenceRegistersEnsureSchema,
        AccountingAggregatesDriftCheck
    ];

    public static readonly IReadOnlyList<string> All =
    [
        .. NightlyPack,
        AccountingOperationsStuckMonitor,
        AccountingGeneralJournalEntryAutoReversePostDue
    ];
}

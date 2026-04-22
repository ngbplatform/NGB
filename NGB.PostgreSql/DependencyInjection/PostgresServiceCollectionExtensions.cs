using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NGB.Application.Abstractions.Services;
using NGB.Persistence.Accounts;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Checkers;
using NGB.Persistence.Catalogs;
using NGB.Persistence.Catalogs.Enrichment;
using NGB.Persistence.Catalogs.Universal;
using NGB.Persistence.Documents;
using NGB.Persistence.Documents.Numbering;
using NGB.Persistence.Documents.GeneralJournalEntry;
using NGB.Persistence.Documents.Storage;
using NGB.Persistence.Documents.Universal;
using NGB.Persistence.Dimensions;
using NGB.Persistence.Dimensions.Enrichment;
using NGB.Persistence.Locks;
using NGB.Persistence.Migrations;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.Periods;
using NGB.Persistence.PostingState;
using NGB.Persistence.ReferenceRegisters;
using NGB.Persistence.Reporting;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.Accounts;
using NGB.Persistence.Readers.PostingState;
using NGB.Persistence.Readers.Documents;
using NGB.Persistence.Readers.Reports;
using NGB.Persistence.Readers.Periods;
using NGB.Persistence.Schema;
using NGB.Persistence.UnitOfWork;
using NGB.Persistence.Writers;
using NGB.PostgreSql.Catalogs;
using NGB.PostgreSql.Accounts;
using NGB.PostgreSql.Checkers;
using NGB.PostgreSql.Dapper;
using NGB.PostgreSql.Documents;
using NGB.PostgreSql.Documents.Numbering;
using NGB.PostgreSql.Documents.GeneralJournalEntry;
using NGB.PostgreSql.AuditLog;
using NGB.PostgreSql.Dimensions;
using NGB.PostgreSql.Locks;
using NGB.PostgreSql.OperationalRegisters;
using NGB.PostgreSql.ReferenceRegisters;
using NGB.PostgreSql.Migrations;
using NGB.PostgreSql.Periods;
using NGB.PostgreSql.PostingState;
using NGB.PostgreSql.Readers;
using NGB.PostgreSql.Reporting;
using NGB.PostgreSql.Reporting.Accounting;
using NGB.PostgreSql.Schema;
using NGB.PostgreSql.UnitOfWork;
using NGB.PostgreSql.Writers;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.DependencyInjection;

public static class PostgresServiceCollectionExtensions
{
    /// <summary>
    /// Registers PostgreSQL infrastructure for NGB.
    /// </summary>
    /// <param name="services"></param>
    /// <param name="connectionString">Connection string pointing to target database.</param>
    public static IServiceCollection AddNgbPostgres(this IServiceCollection services, string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new NgbArgumentRequiredException(nameof(connectionString));

        return services.AddPostgres(options => options.ConnectionString = connectionString);
    }

    /// <summary>
    /// Registers PostgreSQL infrastructure for NGB with configuration action.
    /// </summary>
    public static IServiceCollection AddPostgres(
        this IServiceCollection services,
        Action<PostgresOptions> configureOptions)
    {
        if (configureOptions is null)
            throw new NgbArgumentRequiredException(nameof(configureOptions));
        
        // Configure options
        services.Configure(configureOptions);
        services.TryAddSingleton(TimeProvider.System);

        // Validate options on startup
        services.AddOptions<PostgresOptions>()
            .Validate(opts => !string.IsNullOrWhiteSpace(opts.ConnectionString),
                "PostgreSQL connection string must not be empty.")
            .ValidateOnStart();

        // One-time global Dapper configuration (DateOnly, etc.)
        DapperTypeHandlers.Register();

        // Migration runner
        services.TryAddSingleton<IMigrationRunner>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<PostgresOptions>>().Value;
            return new PostgresMigrationRunner(options.ConnectionString);
        });

        services.TryAddScoped<IUnitOfWork>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<PostgresOptions>>().Value;
            var logger = sp.GetRequiredService<ILogger<PostgresUnitOfWork>>();
            return new PostgresUnitOfWork(options.ConnectionString,  logger);
        });

        // AuditLog (persistence only; runtime integration comes later)
        services.TryAddScoped<IPlatformUserRepository, PostgresPlatformUserRepository>();
        services.TryAddScoped<IAuditEventWriter, PostgresAuditEventWriter>();
        services.TryAddScoped<IAuditEventReader, PostgresAuditEventReader>();

        // Dimensions (platform-wide Dimension Sets)
        services.TryAddScoped<IDimensionSetWriter, PostgresDimensionSetWriter>();
        services.TryAddScoped<IDimensionSetReader, PostgresDimensionSetReader>();
        services.TryAddScoped<IDimensionValueEnrichmentReader, PostgresDimensionValueEnrichmentReader>();

        // Operational Registers (persistence contracts only; runtime/write engine comes later)
        services.TryAddScoped<IOperationalRegisterRepository, PostgresOperationalRegisterRepository>();
        services.TryAddScoped<IOperationalRegisterAdminReader, PostgresOperationalRegisterAdminReader>();
        services.TryAddScoped<IOperationalRegisterPhysicalSchemaHealthReader, PostgresOperationalRegisterPhysicalSchemaHealthReader>();
        services.TryAddScoped<IOperationalRegisterDimensionRuleRepository, PostgresOperationalRegisterDimensionRuleRepository>();
        services.TryAddScoped<IOperationalRegisterResourceRepository, PostgresOperationalRegisterResourceRepository>();
        services.TryAddScoped<IOperationalRegisterFinalizationRepository, PostgresOperationalRegisterFinalizationRepository>();
        services.TryAddScoped<IOperationalRegisterWriteStateRepository, PostgresOperationalRegisterWriteStateRepository>();
        services.TryAddScoped<IOperationalRegisterMovementsStore, PostgresOperationalRegisterMovementsStore>();
        services.TryAddScoped<IOperationalRegisterMovementsReader, PostgresOperationalRegisterMovementsReader>();
        services.TryAddScoped<IOperationalRegisterMovementsQueryReader, PostgresOperationalRegisterMovementsQueryReader>();
        services.TryAddScoped<IOperationalRegisterResourceNetReader, PostgresOperationalRegisterResourceNetReader>();
        services.TryAddScoped<IOperationalRegisterMonthlyProjectionAggregator, PostgresOperationalRegisterMonthlyProjectionAggregator>();
        services.TryAddScoped<IOperationalRegisterTurnoversReader, PostgresOperationalRegisterTurnoversReader>();
        services.TryAddScoped<IOperationalRegisterBalancesReader, PostgresOperationalRegisterBalancesReader>();
        services.TryAddScoped<IOperationalRegisterTurnoversStore, PostgresOperationalRegisterTurnoversStore>();
        services.TryAddScoped<IOperationalRegisterBalancesStore, PostgresOperationalRegisterBalancesStore>();

        // Reference Registers (metadata + idempotency state)
        services.TryAddScoped<IReferenceRegisterRepository, PostgresReferenceRegisterRepository>();
        services.TryAddScoped<IReferenceRegisterFieldRepository, PostgresReferenceRegisterFieldRepository>();
        services.TryAddScoped<IReferenceRegisterDimensionRuleRepository, PostgresReferenceRegisterDimensionRuleRepository>();
        services.TryAddScoped<IReferenceRegisterWriteStateRepository, PostgresReferenceRegisterWriteStateRepository>();
        services.TryAddScoped<IReferenceRegisterIndependentWriteStateRepository, PostgresReferenceRegisterIndependentWriteStateRepository>();
        services.TryAddScoped<IReferenceRegisterKeyLock, PostgresReferenceRegisterKeyLock>();
        services.TryAddScoped<IReferenceRegisterRecordsStore, PostgresReferenceRegisterRecordsStore>();
        services.TryAddScoped<IReferenceRegisterRecordsReader, PostgresReferenceRegisterRecordsReader>();
        services.TryAddScoped<IReferenceRegisterPhysicalSchemaHealthReader, PostgresReferenceRegisterPhysicalSchemaHealthReader>();

        // Catalog
        services.TryAddScoped<ICatalogRepository, PostgresCatalogRepository>();
        services.TryAddScoped<ICatalogEnrichmentReader, PostgresCatalogEnrichmentReader>();
        services.TryAddScoped<ICatalogReader, PostgresCatalogReader>();
        services.TryAddScoped<ICatalogPartsReader, PostgresCatalogPartsReader>();
        services.TryAddScoped<ICatalogPartsWriter, PostgresCatalogPartsWriter>();
        services.TryAddScoped<ICatalogWriter, PostgresCatalogWriter>();

        // Chart of Accounts
        services.TryAddScoped<IChartOfAccountsRepository, PostgresChartOfAccountsRepository>();
        services.TryAddScoped<ICashFlowLineRepository, PostgresCashFlowLineRepository>();

        // Checkers
        services.TryAddScoped<PostgresAccountingIntegrityChecker>();
        services.TryAddScoped<IAccountingIntegrityChecker>(sp => sp.GetRequiredService<PostgresAccountingIntegrityChecker>());
        services.TryAddScoped<IAccountingIntegrityDiagnostics>(sp => sp.GetRequiredService<PostgresAccountingIntegrityChecker>());

        // Documents
        services.TryAddScoped<IDocumentRepository, PostgresDocumentRepository>();
        services.TryAddScoped<IDocumentReader, PostgresDocumentReader>();
        services.TryAddScoped<IDocumentDisplayReader, PostgresDocumentDisplayReader>();
        services.TryAddScoped<IDocumentPartsReader, PostgresDocumentPartsReader>();
        services.TryAddScoped<IDocumentPartsWriter, PostgresDocumentPartsWriter>();
        services.TryAddScoped<IDocumentWriter, PostgresDocumentWriter>();
        services.TryAddScoped<IDocumentRelationshipRepository, PostgresDocumentRelationshipRepository>();
        services.TryAddScoped<IDocumentRelationshipsPhysicalSchemaHealthReader, PostgresDocumentRelationshipsPhysicalSchemaHealthReader>();
        services.TryAddScoped<IDocumentNumberSequenceRepository, PostgresDocumentNumberSequenceRepository>();
        services.TryAddScoped<IDocumentOperationStateRepository, PostgresDocumentOperationStateRepository>();
        services.TryAddScoped<IGeneralJournalEntryRepository, PostgresGeneralJournalEntryRepository>();
        services.TryAddScoped<IGeneralJournalEntryUiQueryRepository, PostgresGeneralJournalEntryUiQueryRepository>();
        services.TryAddScoped<IDocumentTypeStorage, PostgresGeneralJournalEntryTypeStorage>();
        
        // Locks
        services.TryAddScoped<IAdvisoryLockManager, PostgresAdvisoryLockManager>();
        
        // Periods
        services.TryAddScoped<IClosedPeriodRepository, PostgresClosedPeriodRepository>();
        
        // PostingState
        services.TryAddScoped<IPostingStateRepository, PostgresPostingStateRepository>();

        // Readers
        services.TryAddScoped<PostgresAccountCardReader>();
        services.TryAddScoped<IAccountCardReader>(sp => sp.GetRequiredService<PostgresAccountCardReader>());
        services.TryAddScoped<IAccountCardPageReader>(sp => sp.GetRequiredService<PostgresAccountCardReader>());
        services.TryAddScoped<PostgresAccountCardEffectivePageReader>();
        services.TryAddScoped<IAccountCardEffectivePageReader>(sp => sp.GetRequiredService<PostgresAccountCardEffectivePageReader>());
        services.TryAddScoped<IAccountingConsistencySnapshotReader, PostgresAccountingConsistencySnapshotReader>();
        services.TryAddScoped<IBalanceSheetSnapshotReader, PostgresBalanceSheetSnapshotReader>();
        services.TryAddScoped<ICashFlowIndirectSnapshotReader, PostgresCashFlowIndirectSnapshotReader>();
        services.TryAddScoped<IGeneralLedgerAggregatedSnapshotReader, PostgresGeneralLedgerAggregatedSnapshotReader>();
        services.TryAddScoped<IIncomeStatementSnapshotReader, PostgresIncomeStatementSnapshotReader>();
        services.TryAddScoped<IStatementOfChangesInEquitySnapshotReader, PostgresStatementOfChangesInEquitySnapshotReader>();
        services.TryAddScoped<ITrialBalanceSnapshotReader, PostgresTrialBalanceSnapshotReader>();
        services.TryAddScoped<IAccountingBalanceReader, PostgresAccountingBalanceReader>();
        services.TryAddScoped<IAccountingEntryReader, PostgresAccountingEntryReader>();
        services.TryAddScoped<IAccountingTurnoverReader, PostgresAccountingTurnoverReader>();
        services.TryAddScoped<IAccountingOperationalBalanceReader, PostgresAccountingOperationalBalanceReader>();
        services.TryAddScoped<IAccountingTurnoverAggregationReader, PostgresAccountingTurnoverAggregationReader>();
        services.TryAddScoped<IAccountingPeriodActivityReader, PostgresAccountingPeriodActivityReader>();
        services.TryAddScoped<IAccountLookupReader, PostgresAccountLookupReader>();
        services.TryAddScoped<IRetainedEarningsAccountLookupReader, PostgresRetainedEarningsAccountLookupReader>();
        services.TryAddScoped<IClosedPeriodReader, PostgresClosedPeriodReader>();
        services.TryAddScoped<IDimensionDefinitionReader, PostgresDimensionDefinitionReader>();
        services.TryAddScoped<IGeneralJournalReader, PostgresGeneralJournalReader>();
        services.TryAddScoped<PostgresGeneralLedgerAggregatedReader>();
        services.TryAddScoped<IGeneralLedgerAggregatedPageReader>(sp => sp.GetRequiredService<PostgresGeneralLedgerAggregatedReader>());
        services.TryAddScoped<ILedgerAnalysisFlatDetailReader, PostgresLedgerAnalysisFlatDetailReader>();
        services.TryAddScoped<IPostingStateReader, PostgresPostingStateReader>();
        services.TryAddScoped<IDocumentRelationshipGraphReader, PostgresDocumentRelationshipGraphReader>();

        // Reporting foundation
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostgresReportDatasetSource, AccountingLedgerAnalysisPostgresDatasetSource>());
        services.TryAddSingleton<PostgresReportDatasetCatalog>();
        services.TryAddScoped<PostgresReportSqlBuilder>();
        services.TryAddScoped<PostgresReportDatasetExecutor>();
        services.TryAddScoped<PostgresReportPlanExecutor>();
        services.TryAddScoped<ITabularReportPlanExecutor>(sp => sp.GetRequiredService<PostgresReportPlanExecutor>());
        services.TryAddScoped<IReportVariantRepository, PostgresReportVariantRepository>();
        
        // Schema
        services.TryAddScoped<IDbSchemaInspector, PostgresSchemaInspector>();
        services.TryAddScoped<IDbTypeMapper, PostgresDbTypeMapper>();
        services.TryAddScoped<IAccountingCoreSchemaValidationService, PostgresAccountingCoreSchemaValidationService>();
        services.TryAddScoped<IOperationalRegistersCoreSchemaValidationService, PostgresOperationalRegistersCoreSchemaValidationService>();
        services.TryAddScoped<IReferenceRegistersCoreSchemaValidationService, PostgresReferenceRegistersCoreSchemaValidationService>();
        services.TryAddScoped<IDocumentsCoreSchemaValidationService, PostgresDocumentsCoreSchemaValidationService>();
        
        // Writers
        services.TryAddScoped<IAccountingBalanceWriter, PostgresAccountingBalanceWriter>();
        services.TryAddScoped<IAccountingEntryMaintenanceWriter, PostgresAccountingEntryMaintenanceWriter>();
        services.TryAddScoped<IAccountingEntryWriter, PostgresAccountingEntryWriter>();
        services.TryAddScoped<IAccountingTurnoverWriter, PostgresAccountingTurnoverWriter>();

        return services;
    }
}

/// <summary>
/// Configuration options for NGB PostgreSQL infrastructure.
/// </summary>
public sealed class PostgresOptions
{
    /// <summary>
    /// PostgreSQL connection string.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Command timeout in seconds. Default is 30 seconds.
    /// </summary>
    public int CommandTimeout { get; set; } = 30;

    /// <summary>
    /// Max time to wait for PostgreSQL advisory locks.
    /// 
    /// We intentionally keep advisory locks transactional (pg_*_xact_lock), so a long-running
    /// transaction can block other work. A bounded wait makes contention observable and prevents
    /// "infinite" hangs under load.
    /// </summary>
    public int AdvisoryLockWaitTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Whether to enable detailed error messages. Default is false.
    /// </summary>
    public bool EnableDetailedErrors { get; set; }
}

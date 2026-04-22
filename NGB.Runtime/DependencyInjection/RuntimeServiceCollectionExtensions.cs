using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.Balances;
using NGB.Accounting.Posting.Validators;
using NGB.Application.Abstractions.Services;
using NGB.Definitions;
using NGB.Definitions.Documents.Approval;
using NGB.Definitions.Documents.Numbering;
using NGB.Metadata.Catalogs.Storage;
using NGB.Metadata.Documents.Storage;
using NGB.Persistence.Readers.Reports;
using NGB.Persistence.Catalogs.Storage;
using NGB.Persistence.Documents.Storage;
using NGB.Runtime.Accounts;
using NGB.Runtime.Admin;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Catalogs;
using NGB.Runtime.Catalogs.Validation;
using NGB.Runtime.Catalogs.Storage;
using NGB.Runtime.Definitions.Validation;
using NGB.Runtime.Dimensions;
using NGB.Runtime.Documents.Storage;
using NGB.Runtime.Documents;
using NGB.Runtime.Documents.Derivations;
using NGB.Runtime.Documents.Posting;
using NGB.Runtime.Documents.Numbering;
using NGB.Runtime.Documents.Validation;
using NGB.Runtime.Documents.Policies;
using NGB.Runtime.Documents.Workflow;
using NGB.Runtime.Documents.GeneralJournalEntry;
using NGB.Runtime.Documents.GeneralJournalEntry.Policies;
using NGB.Runtime.Maintenance;
using NGB.Runtime.OperationalRegisters;
using NGB.Runtime.OperationalRegisters.Projections;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using NGB.Runtime.Reporting;
using NGB.Runtime.Reporting.Canonical;
using NGB.Runtime.Reporting.Definitions;
using NGB.Runtime.Reporting.Datasets;
using NGB.Runtime.ReferenceRegisters;
using NGB.Runtime.Ui;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.DependencyInjection;

public static class RuntimeServiceCollectionExtensions
{
    /// <summary>
    /// Registers provider-agnostic Runtime services (orchestration, schema validation, reports).
    /// Infrastructure (DB) implementations must be registered separately.
    /// </summary>
    public static IServiceCollection AddNgbRuntime(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        // Definitions (module-composed, reflection-free)
        services.AddNgbDefinitions();

        // Platform document definitions
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDefinitionsContributor, GeneralJournalEntryDefinitionsContributor>());

        // Platform relationship type definitions
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDefinitionsContributor, DocumentRelationshipsDefinitionsContributor>());

        // Definitions startup validation (fail-fast)
        services.TryAddSingleton<IDefinitionsValidationService, DefinitionsValidationService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, DefinitionsStartupValidatorHostedService>());

        // Metadata registries (schema validation). Built from Definitions.
        // NOTE: use TryAdd to allow tests/hosts to override with custom registries.
        services.TryAddSingleton<IDocumentTypeRegistry>(sp =>
        {
            var defs = sp.GetService(typeof(DefinitionsRegistry)) as DefinitionsRegistry;
            if (defs is null)
            {
                throw new NgbConfigurationViolationException(
                    message: "NgbDefinitionsRegistry is not registered. Ensure AddNgbDefinitions() is called before AddNgbRuntime().",
                    context: new Dictionary<string, object?>
                    {
                        ["service"] = typeof(DefinitionsRegistry).FullName
                    });
            }
            
            var reg = new DocumentTypeRegistry();
            
            foreach (var d in defs.Documents)
            {
                reg.Register(d.Metadata);
            }
            
            return reg;
        });

        services.TryAddSingleton<ICatalogTypeRegistry>(sp =>
        {
            var defs = sp.GetService(typeof(DefinitionsRegistry)) as DefinitionsRegistry;
            if (defs is null)
            {
                throw new NgbConfigurationViolationException(
                    message: "NgbDefinitionsRegistry is not registered. Ensure AddNgbDefinitions() is called before AddNgbRuntime().",
                    context: new Dictionary<string, object?>
                    {
                        ["service"] = typeof(DefinitionsRegistry).FullName
                    });
            }
            
            var reg = new CatalogTypeRegistry();
            
            foreach (var c in defs.Catalogs)
            {
                reg.Register(c.Metadata);
            }
            
            return reg;
        });

        // Business AuditLog
        services.TryAddScoped<ICurrentActorContext, NullCurrentActorContext>();
        services.TryAddScoped<IAuditLogService, AuditLogService>();
        services.TryAddScoped<IAuditLogQueryService, AuditLogQueryService>();

        // Dimensions (platform-wide Dimension Sets)
        services.TryAddScoped<IDimensionSetService, DimensionSetService>();

        // Accounting
        services.TryAddSingleton<AccountingBalanceCalculator>();
        services.TryAddScoped<AccountingNegativeBalanceChecker>();

        // Posting validator (required by PostingEngine). Hosts may override.
        services.TryAddSingleton<IAccountingPostingValidator, BasicAccountingPostingValidator>();

        // Fallback account lookup (handles inactive accounts with historic movements)
        services.TryAddScoped<IAccountByIdResolver, AccountByIdResolver>();

        // Chart of Accounts (loaded from persistence)
        services.TryAddScoped<IChartOfAccountsProvider, ChartOfAccountsProvider>();
        services.TryAddScoped<IChartOfAccountsAdminService, ChartOfAccountsAdminService>();
        services.TryAddScoped<IChartOfAccountsManagementService, ChartOfAccountsManagementService>();

        // Admin UI facade (menu composition + CoA maintenance)
        services.TryAddScoped<IMainMenuService, MainMenuService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IMainMenuContributor, AccountingDocumentsMainMenuContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IMainMenuContributor, AccountingReportsMainMenuContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IMainMenuContributor, AccountingAdminMainMenuContributor>());
        services.TryAddScoped<IAdminService, AdminService>();

        // UI payload enrichment (reference values -> { id, display })
        services.TryAddScoped<IReferencePayloadEnricher, ReferencePayloadEnricher>();

        // Catalogs
        services.TryAddScoped<ICatalogDraftService, CatalogDraftService>();
        services.TryAddScoped<ICatalogService, CatalogService>();
        services.TryAddScoped<ICatalogValidatorResolver, DefinitionsCatalogValidatorResolver>();

        // Resolvers over typed storages (must be Scoped because storages typically depend on scoped UnitOfWork)
        services.TryAddScoped<ICatalogTypeStorageResolver, CompositeCatalogTypeStorageResolver>();

        services.TryAddScoped<ICatalogSchemaValidationService, CatalogSchemaValidationService>();
        services.TryAddScoped<CatalogWriteEngine>();

        // Documents
        services.TryAddScoped<IDocumentDraftService, DocumentDraftService>();
        services.TryAddScoped<IDocumentService, DocumentService>();
        services.TryAddScoped<IDocumentEffectsQueryService, DocumentEffectsQueryService>();
        services.TryAddScoped<IDocumentRelationshipService, DocumentRelationshipService>();
        services.TryAddScoped<IDocumentRelationshipGraphReadService, DocumentRelationshipGraphReadService>();
        services.TryAddScoped<IDocumentDerivationService, DocumentDerivationService>();
        services.TryAddScoped<IDocumentPostingActionResolver, DefinitionsDocumentPostingActionResolver>();
        services.TryAddScoped<IDocumentOperationalRegisterPostingActionResolver, DefinitionsDocumentOperationalRegisterPostingActionResolver>();
        services.TryAddScoped<IDocumentReferenceRegisterPostingActionResolver, DefinitionsDocumentReferenceRegisterPostingActionResolver>();
        services.TryAddScoped<IDocumentValidatorResolver, DefinitionsDocumentValidatorResolver>();
        services.TryAddScoped<IDocumentNumberingPolicyResolver, DefinitionsDocumentNumberingPolicyResolver>();
        services.TryAddScoped<IDocumentApprovalPolicyResolver, DefinitionsDocumentApprovalPolicyResolver>();
        services.TryAddScoped<IDocumentWorkflowExecutor, DocumentWorkflowExecutor>();

        // Platform policies
        services.TryAddScoped<GeneralJournalEntryNumberingPolicy>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDocumentNumberingPolicy, GeneralJournalEntryNumberingPolicy>());
        services.TryAddScoped<GeneralJournalEntryApprovalPolicy>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDocumentApprovalPolicy, GeneralJournalEntryApprovalPolicy>());

        // Resolver over typed storages (must be Scoped because storages typically depend on scoped UnitOfWork)
        services.TryAddScoped<IDocumentTypeStorageResolver, CompositeDocumentTypeStorageResolver>();

        services.TryAddScoped<DocumentPostingLifecycleCoordinator>();
        services.TryAddScoped<IDocumentPostingService, DocumentPostingService>();
        services.TryAddScoped<IDocumentSchemaValidationService, DocumentSchemaValidationService>();
        services.TryAddScoped<DocumentWriteEngine>();

        // Document numbering (platform-wide)
        services.TryAddSingleton<IDocumentNumberFormatter, DefaultDocumentNumberFormatter>();
        services.TryAddScoped<IDocumentNumberingService, DocumentNumberingService>();
        services.TryAddScoped<IDocumentNumberingAndTypedSyncService, DocumentNumberingAndTypedSyncService>();

        // Platform documents (provider-agnostic services)
        services.TryAddScoped<IGeneralJournalEntryDocumentService, GeneralJournalEntryDocumentService>();
        services.TryAddScoped<IGeneralJournalEntryFacade, GeneralJournalEntryFacade>();
        services.TryAddScoped<IGeneralJournalEntryUiService, GeneralJournalEntryUiService>();
        services.TryAddScoped<IGeneralJournalEntrySystemReversalRunner, GeneralJournalEntrySystemReversalRunner>();

        // Periods
        services.TryAddScoped<IPeriodClosingService, PeriodClosingService>();
        services.TryAddScoped<IPeriodClosingUiService, PeriodClosingUiService>();

        // Operational Registers (metadata + rules)
        services.TryAddScoped<IOperationalRegisterManagementService, OperationalRegisterManagementService>();
        services.TryAddScoped<IOperationalRegisterAdminReadService, OperationalRegisterAdminReadService>();
        services.TryAddScoped<IOperationalRegisterAdminMaintenanceService, OperationalRegisterAdminMaintenanceService>();
        services.TryAddScoped<IOperationalRegisterAdminEndpoint, OperationalRegisterAdminEndpoint>();
        services.TryAddScoped<IOperationalRegisterWriteEngine, OperationalRegisterWriteEngine>();
        services.TryAddScoped<IOperationalRegisterFinalizationService, OperationalRegisterFinalizationService>();
        services.TryAddScoped<IOperationalRegisterMovementsApplier, OperationalRegisterMovementsApplier>();
        services.TryAddScoped<IOperationalRegisterDefaultMonthProjector, DefaultOperationalRegisterMonthProjector>();
        services.TryAddScoped<IOperationalRegisterFinalizationRunner, OperationalRegisterFinalizationRunner>();
        
        // Operational Registers read-side (UI/report facade)
        services.TryAddScoped<IOperationalRegisterReadService, OperationalRegisterReadService>();

        // Reference Registers (metadata + rules)
        services.TryAddScoped<IReferenceRegisterManagementService, ReferenceRegisterManagementService>();
        services.TryAddScoped<IReferenceRegisterReadService, ReferenceRegisterReadService>();
        services.TryAddScoped<IReferenceRegisterIndependentWriteService, ReferenceRegisterIndependentWriteService>();
        services.TryAddScoped<IReferenceRegisterWriteEngine, ReferenceRegisterWriteEngine>();
        services.TryAddScoped<IReferenceRegisterRecordsApplier, ReferenceRegisterRecordsApplier>();
        services.TryAddScoped<IReferenceRegisterAdminReadService, ReferenceRegisterAdminReadService>();
        services.TryAddScoped<IReferenceRegisterAdminMaintenanceService, ReferenceRegisterAdminMaintenanceService>();
        services.TryAddScoped<IReferenceRegisterAdminEndpoint, ReferenceRegisterAdminEndpoint>();

        // Posting
        services.TryAddScoped<IAccountingPostingContextFactory, AccountingPostingContextFactory>();
        services.TryAddScoped<AccountingPostingPipeline>();
        services.TryAddScoped<PostingEngine>();
        services.TryAddScoped<RepostingService>();
        services.TryAddScoped<UnpostingService>();

        // Reporting
        services.TryAddScoped<IAccountCardEffectivePagedReportReader, AccountCardEffectivePagedReportService>();
        services.TryAddScoped<IAccountingConsistencyReportReader, AccountingConsistencyReportService>();
        services.TryAddScoped<IGeneralJournalReportReader, GeneralJournalReportService>();
        services.TryAddScoped<GeneralLedgerAggregatedReportService>();
        services.TryAddScoped<IGeneralLedgerAggregatedPagedReportReader>(sp => sp.GetRequiredService<GeneralLedgerAggregatedReportService>());
        services.TryAddScoped<IPostingStateReportReader, PostingStateReportService>();
        services.TryAddScoped<ITrialBalanceReader, TrialBalanceService>();
        services.TryAddScoped<ITrialBalanceReportReader, TrialBalanceReportService>();
        services.TryAddScoped<IBalanceSheetReportReader, BalanceSheetReportService>();
        services.TryAddScoped<ICashFlowIndirectReportReader, CashFlowIndirectReportService>();
        services.TryAddScoped<IIncomeStatementReportReader, IncomeStatementReportService>();
        services.TryAddScoped<IStatementOfChangesInEquityReportReader, StatementOfChangesInEquityReportService>();

        // Reporting support services
        services.TryAddScoped<IDimensionScopeExpansionService, DimensionScopeExpansionService>();

        // Reporting foundation
        services.TryAddSingleton<IReportDefinitionProvider, ReportDefinitionCatalog>();
        services.TryAddSingleton<IReportDatasetCatalog, ReportDatasetCatalog>();
        services.TryAddSingleton<IReportLayoutValidator, ReportLayoutValidator>();
        services.TryAddScoped<IReportVariantAccessContext, NullReportVariantAccessContext>();
        services.TryAddScoped<IReportVariantService, ReportVariantService>();
        services.TryAddSingleton<IReportExportService, ReportXlsxExportService>();
        services.TryAddSingleton<IRenderedReportSnapshotStore>(sp =>
        {
            var cache = sp.GetService<IMemoryCache>();
            return cache is null
                ? NullRenderedReportSnapshotStore.Instance
                : new MemoryCacheRenderedReportSnapshotStore(cache);
        });
        services.TryAddScoped<ReportVariantRequestResolver>();
        services.TryAddScoped<IReportPlanExecutor, CompositeReportPlanExecutor>();
        services.TryAddScoped<IReportEngine, ReportEngine>();
        services.TryAddScoped<ReportFilterScopeExpander>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IReportDefinitionSource, AccountingLedgerAnalysisDefinitionSource>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IReportDefinitionSource, CanonicalAccountingReportDefinitionSource>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IReportDatasetSource, AccountingLedgerAnalysisDatasetSource>());
        services.TryAddScoped<ReportExecutionPlanner>();
        services.TryAddScoped<ReportSheetBuilder>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IReportSpecializedPlanExecutor, LedgerAnalysisComposableReportExecutor>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IReportSpecializedPlanExecutor, TrialBalanceCanonicalReportExecutor>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IReportSpecializedPlanExecutor, GeneralJournalCanonicalReportExecutor>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IReportSpecializedPlanExecutor, AccountCardCanonicalReportExecutor>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IReportSpecializedPlanExecutor, GeneralLedgerAggregatedCanonicalReportExecutor>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IReportSpecializedPlanExecutor, PostingLogCanonicalReportExecutor>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IReportSpecializedPlanExecutor, BalanceSheetCanonicalReportExecutor>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IReportSpecializedPlanExecutor, CashFlowIndirectCanonicalReportExecutor>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IReportSpecializedPlanExecutor, StatementOfChangesInEquityCanonicalReportExecutor>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IReportSpecializedPlanExecutor, IncomeStatementCanonicalReportExecutor>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IReportSpecializedPlanExecutor, AccountingConsistencyCanonicalReportExecutor>());

        // Maintenance/repair (rebuild derived accounting data)
        services.TryAddScoped<IAccountingRebuildService, AccountingRebuildService>();

        return services;
    }
}

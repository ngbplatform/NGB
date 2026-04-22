using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.Catalogs.Storage;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Catalogs;
using NGB.PostgreSql.Reporting;
using NGB.PropertyManagement.BackgroundJobs;
using NGB.PostgreSql.Documents;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.PostgreSql.BackgroundJobs;
using NGB.PropertyManagement.PostgreSql.Documents;
using NGB.PropertyManagement.PostgreSql.Payables;
using NGB.PropertyManagement.PostgreSql.Receivables;
using NGB.PropertyManagement.PostgreSql.Reporting;
using NGB.PropertyManagement.Payables;
using NGB.PropertyManagement.Receivables;
using NGB.PropertyManagement.Reporting;

namespace NGB.PropertyManagement.PostgreSql.DependencyInjection;

public static class PropertyManagementPostgresModuleServiceCollectionExtensions
{
    public static IServiceCollection AddPropertyManagementPostgresModule(this IServiceCollection services)
    {
        // IMPORTANT: do NOT use TryAddEnumerable with factory-based registrations; it can't deduplicate them.

        // Most PM catalogs are simple head-only tables => use the generic PostgresHeadCatalogTypeStorage.
        services.AddScoped<ICatalogTypeStorage>(sp =>
            new PostgresHeadCatalogTypeStorage(
                sp.GetRequiredService<IUnitOfWork>(),
                PropertyManagementCodes.Party,
                headTable: "cat_pm_party",
                columns: [PostgresHeadCatalogTypeStorage.Column.DraftString("display", "display")]));

        services.AddScoped<ICatalogTypeStorage>(sp =>
            new PostgresHeadCatalogTypeStorage(
                sp.GetRequiredService<IUnitOfWork>(),
                PropertyManagementCodes.Property,
                headTable: "cat_pm_property",
                columns: [PostgresHeadCatalogTypeStorage.Column.DraftString("display", "display")]));

        services.AddScoped<ICatalogTypeStorage>(sp =>
            new PostgresHeadCatalogTypeStorage(
                sp.GetRequiredService<IUnitOfWork>(),
                PropertyManagementCodes.BankAccount,
                headTable: "cat_pm_bank_account",
                columns: [PostgresHeadCatalogTypeStorage.Column.DraftString("display", "display")]));

        services.AddScoped<ICatalogTypeStorage>(sp =>
            new PostgresHeadCatalogTypeStorage(
                sp.GetRequiredService<IUnitOfWork>(),
                PropertyManagementCodes.MaintenanceCategory,
                headTable: "cat_pm_maintenance_category",
                columns: [PostgresHeadCatalogTypeStorage.Column.DraftString("display", "display")]));

        // pm.accounting_policy is head-only, but its FK columns are populated by the Setup service.
        //
        // IMPORTANT:
        // - Typed head rows are created at Draft creation time (EnsureCreatedAsync).
        // - Therefore FK columns MUST be nullable at the database level, otherwise Draft creation would require
        //   us to insert "placeholder" GUIDs that would violate FK constraints.
        services.AddScoped<ICatalogTypeStorage>(sp =>
            new PostgresHeadCatalogTypeStorage(
                sp.GetRequiredService<IUnitOfWork>(),
                PropertyManagementCodes.AccountingPolicy,
                headTable: "cat_pm_accounting_policy",
                columns: [PostgresHeadCatalogTypeStorage.Column.DraftString("display", "display")]));

        // pm.receivable_charge_type is head-only; FK columns are configured by the admin workflow.
        // Same rationale as pm.accounting_policy: Draft creation inserts a typed head row first.
        services.AddScoped<ICatalogTypeStorage>(sp =>
            new PostgresHeadCatalogTypeStorage(
                sp.GetRequiredService<IUnitOfWork>(),
                PropertyManagementCodes.ReceivableChargeType,
                headTable: "cat_pm_receivable_charge_type",
                columns: [PostgresHeadCatalogTypeStorage.Column.DraftString("display", "display")]));

        // pm.payable_charge_type is also created through the generic draft-first catalog flow.
        // debit_account_id is populated by setup/admin update after the typed head row exists.
        services.AddScoped<ICatalogTypeStorage>(sp =>
            new PostgresHeadCatalogTypeStorage(
                sp.GetRequiredService<IUnitOfWork>(),
                PropertyManagementCodes.PayableChargeType,
                headTable: "cat_pm_payable_charge_type",
                columns: [PostgresHeadCatalogTypeStorage.Column.DraftString("display", "display")]));

        // Documents are handled by the universal DocumentService + IDocumentWriter.
        // Register per-type IDocumentTypeStorage only for advanced cases (parts tables, custom draft hooks).
        services.AddScoped<IPostgresDocumentListFilterSqlContributor, PropertyManagementDocumentListFilterSqlContributor>();

        // Posting handlers need fast typed reads for posting.
        services.AddScoped<IPropertyManagementDocumentReaders, PropertyManagementDocumentReaders>();
        services.AddScoped<IPropertyManagementRentChargeGenerationReader, PropertyManagementRentChargeGenerationReader>();

        // Receivables read/report services (PostgreSQL).
        services.AddScoped<IReceivablesReconciliationService, PostgresReceivablesReconciliationService>();
        services.AddScoped<IPayablesReconciliationService, PostgresPayablesReconciliationService>();
        services.AddScoped<IReceivableApplyHeadWriter, PostgresReceivableApplyHeadWriter>();
        services.AddScoped<IPayableApplyHeadWriter, PostgresPayableApplyHeadWriter>();

        // PM building and occupancy summary report readers (PostgreSQL).
        services.AddScoped<IBuildingSummaryReader, PostgresBuildingSummaryReader>();
        services.AddScoped<IOccupancySummaryReader, PostgresOccupancySummaryReader>();
        services.AddScoped<IMaintenanceQueueReader, PostgresMaintenanceQueueReader>();
        services.AddScoped<ITenantStatementReader, PostgresTenantStatementReader>();

        // PM-specific reporting dataset bindings.
        services.AddSingleton<IPostgresReportDatasetSource, PmAccountingLedgerAnalysisPostgresDatasetSource>();

        return services;
    }
}

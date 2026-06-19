using NGB.Accounting.Documents;
using NGB.Core.Reporting;
using NGB.Core.Security;

namespace NGB.PropertyManagement.Definitions;

public sealed record PropertyManagementSecurityPermissionDefault(
    string ResourceKind,
    string ResourceCode,
    string ActionCode);

public sealed record PropertyManagementSecurityRoleDefault(
    string Code,
    string Name,
    string Description,
    IReadOnlyList<PropertyManagementSecurityPermissionDefault> Permissions);

public static class PropertyManagementSecurityDefaults
{
    public const string BuildingSummaryReport = "pm.building.summary";
    public const string OccupancySummaryReport = "pm.occupancy.summary";
    public const string TenantStatementReport = PropertyManagementCodes.TenantStatement;
    public const string MaintenanceQueueReport = "pm.maintenance.queue";
    public const string ReceivablesAgingReport = "pm.receivables.aging";
    public const string ReceivablesOpenItemsReport = "pm.receivables.open_items";
    public const string ReceivablesOpenItemsDetailsReport = "pm.receivables.open_items.details";
    
    public const string HomePage = "pm.home";
    public const string ReceivablesOpenItemsPage = "pm.receivables.open_items.page";
    public const string ReceivablesReconciliationPage = "pm.receivables.reconciliation";
    public const string PayablesOpenItemsPage = "pm.payables.open_items";
    public const string PayablesReconciliationPage = "pm.payables.reconciliation";

    private static readonly string[] ReceivableDocuments =
    [
        PropertyManagementCodes.RentCharge,
        PropertyManagementCodes.ReceivableCharge,
        PropertyManagementCodes.LateFeeCharge,
        PropertyManagementCodes.ReceivablePayment,
        PropertyManagementCodes.ReceivableReturnedPayment,
        PropertyManagementCodes.ReceivableCreditMemo,
        PropertyManagementCodes.ReceivableApply
    ];

    private static readonly string[] PayableDocuments =
    [
        PropertyManagementCodes.PayableCharge,
        PropertyManagementCodes.PayablePayment,
        PropertyManagementCodes.PayableCreditMemo,
        PropertyManagementCodes.PayableApply
    ];

    private static readonly string[] MaintenanceDocuments =
    [
        PropertyManagementCodes.MaintenanceRequest,
        PropertyManagementCodes.WorkOrder,
        PropertyManagementCodes.WorkOrderCompletion
    ];

    private static readonly string[] PortfolioDocuments =
    [
        PropertyManagementCodes.Lease
    ];

    private static readonly string[] AccountingDocuments =
    [
        AccountingDocumentTypeCodes.GeneralJournalEntry
    ];

    private static readonly string[] Catalogs =
    [
        PropertyManagementCodes.Property,
        PropertyManagementCodes.Party,
        PropertyManagementCodes.AccountingPolicy,
        PropertyManagementCodes.BankAccount,
        PropertyManagementCodes.MaintenanceCategory,
        PropertyManagementCodes.ReceivableChargeType,
        PropertyManagementCodes.PayableChargeType
    ];

    private static readonly string[] PmReports =
    [
        BuildingSummaryReport,
        OccupancySummaryReport,
        TenantStatementReport,
        MaintenanceQueueReport,
        ReceivablesAgingReport,
        ReceivablesOpenItemsReport,
        ReceivablesOpenItemsDetailsReport
    ];

    private static readonly string[] AccountingReports =
    [
        AccountingReportCodes.TrialBalance,
        AccountingReportCodes.BalanceSheet,
        AccountingReportCodes.IncomeStatement,
        AccountingReportCodes.CashFlowStatementIndirect,
        AccountingReportCodes.StatementOfChangesInEquity,
        AccountingReportCodes.GeneralJournal,
        AccountingReportCodes.AccountCard,
        AccountingReportCodes.GeneralLedgerAggregated,
        AccountingReportCodes.LedgerAnalysis
    ];

    private static readonly string[] Pages =
    [
        HomePage,
        ReceivablesOpenItemsPage,
        ReceivablesReconciliationPage,
        PayablesOpenItemsPage,
        PayablesReconciliationPage
    ];

    public static IReadOnlyList<PropertyManagementSecurityRoleDefault> Roles { get; } =
    [
        new(
            "pm-administrator",
            "PM Administrator",
            "Full Property Management application access including security management.",
            AllPmPermissions()
                .Concat(AdminAccounting(manage: true))
                .Concat([
                    P(NgbResourceKinds.System, NgbPermissionResources.Users, NgbPermissionActions.View),
                    P(NgbResourceKinds.System, NgbPermissionResources.Users, NgbPermissionActions.Manage),
                    P(NgbResourceKinds.System, NgbPermissionResources.Roles, NgbPermissionActions.View),
                    P(NgbResourceKinds.System, NgbPermissionResources.Roles, NgbPermissionActions.Manage),
                    P(NgbResourceKinds.System, NgbPermissionResources.Permissions, NgbPermissionActions.View),
                    P(NgbResourceKinds.System, NgbPermissionResources.Audit, NgbPermissionActions.View)])
                .ToArray()),

        new(
            "pm-accountant",
            "PM Accountant",
            "Accounting-focused PM access across receivables, payables and financial reports.",
            DocumentOps(ReceivableDocuments.Concat(PayableDocuments).Concat(AccountingDocuments), canPost: true)
                .Concat(CatalogViewAndLookup(Catalogs))
                .Concat(ReportExecuteExport(AccountingReports.Concat(PmReports.Where(static x => x.StartsWith("pm.receivables", StringComparison.OrdinalIgnoreCase)))))
                .Concat(AdminAccounting(manage: true))
                .Concat(PageView(Pages))
                .ToArray()),

        new(
            "pm-ar-clerk",
            "PM AR Clerk",
            "Receivables document and receivables report access.",
            DocumentOps(ReceivableDocuments, canPost: true)
                .Concat(DocumentRead(PortfolioDocuments))
                .Concat(CatalogViewAndLookup([PropertyManagementCodes.Property, PropertyManagementCodes.Party, PropertyManagementCodes.ReceivableChargeType, PropertyManagementCodes.BankAccount]))
                .Concat(ReportExecuteExport(PmReports.Where(static x => x.Contains("receivables", StringComparison.OrdinalIgnoreCase) || x == PropertyManagementCodes.TenantStatement)))
                .Concat(PageView([HomePage, ReceivablesOpenItemsPage, ReceivablesReconciliationPage]))
                .ToArray()),

        new(
            "pm-ap-clerk",
            "PM AP Clerk",
            "Payables document and operational payables access.",
            DocumentOps(PayableDocuments, canPost: true)
                .Concat(CatalogViewAndLookup([PropertyManagementCodes.Property, PropertyManagementCodes.Party, PropertyManagementCodes.PayableChargeType, PropertyManagementCodes.BankAccount]))
                .Concat(PageView([HomePage, PayablesOpenItemsPage, PayablesReconciliationPage]))
                .ToArray()),

        new(
            "pm-property-manager",
            "PM Property Manager",
            "Portfolio, lease, tenant statement and limited receivables visibility.",
            DocumentOps(PortfolioDocuments, canPost: false)
                .Concat(DocumentRead(ReceivableDocuments))
                .Concat(CatalogEdit([PropertyManagementCodes.Property, PropertyManagementCodes.Party]))
                .Concat(ReportExecuteExport([BuildingSummaryReport, OccupancySummaryReport, TenantStatementReport]))
                .Concat(PageView([HomePage, ReceivablesOpenItemsPage]))
                .ToArray()),

        new(
            "pm-maintenance-coordinator",
            "PM Maintenance Coordinator",
            "Maintenance request, work order and maintenance queue access.",
            DocumentOps(MaintenanceDocuments, canPost: true)
                .Concat(CatalogEdit([PropertyManagementCodes.MaintenanceCategory, PropertyManagementCodes.Property, PropertyManagementCodes.Party]))
                .Concat(ReportExecuteExport([MaintenanceQueueReport]))
                .Concat(PageView([HomePage]))
                .ToArray()),

        new(
            "pm-auditor",
            "PM Auditor",
            "Read-only PM audit access with effects and flow visibility.",
            DocumentRead(ReceivableDocuments.Concat(PayableDocuments).Concat(MaintenanceDocuments).Concat(PortfolioDocuments).Concat(AccountingDocuments), includeAudit: true)
                .Concat(CatalogViewAndLookup(Catalogs))
                .Concat(ReportExecute(PmReports.Concat(AccountingReports)))
                .Concat(AdminAccounting(manage: false))
                .Concat(PageView(Pages))
                .ToArray()),

        new(
            "pm-read-only",
            "PM Read Only",
            "Read-only operational access without exports.",
            DocumentRead(ReceivableDocuments.Concat(PayableDocuments).Concat(MaintenanceDocuments).Concat(PortfolioDocuments).Concat(AccountingDocuments))
                .Concat(CatalogViewAndLookup(Catalogs))
                .Concat(ReportExecute(PmReports))
                .Concat(PageView(Pages))
                .ToArray())
    ];

    private static IEnumerable<PropertyManagementSecurityPermissionDefault> AllPmPermissions()
        => DocumentOps(ReceivableDocuments.Concat(PayableDocuments).Concat(MaintenanceDocuments).Concat(PortfolioDocuments).Concat(AccountingDocuments), canPost: true)
            .Concat(CatalogEdit(Catalogs))
            .Concat(ReportExecuteExport(PmReports.Concat(AccountingReports)))
            .Concat(PageView(Pages))
            .Concat([
                P(NgbResourceKinds.External, PropertyManagementCodes.Watchdog, NgbPermissionActions.View),
                P(NgbResourceKinds.External, PropertyManagementCodes.BackgroundJobs, NgbPermissionActions.View)]);

    private static IEnumerable<PropertyManagementSecurityPermissionDefault> DocumentOps(IEnumerable<string> codes, bool canPost)
    {
        var actions = canPost
            ? new[]
            {
                NgbPermissionActions.View,
                NgbPermissionActions.Lookup,
                NgbPermissionActions.Create,
                NgbPermissionActions.EditDraft,
                NgbPermissionActions.Post,
                NgbPermissionActions.Unpost,
                NgbPermissionActions.Repost,
                NgbPermissionActions.ViewEffects,
                NgbPermissionActions.ViewFlow,
                NgbPermissionActions.ViewAudit
            }
            : [
                NgbPermissionActions.View,
                NgbPermissionActions.Lookup,
                NgbPermissionActions.Create,
                NgbPermissionActions.EditDraft,
                NgbPermissionActions.ViewEffects,
                NgbPermissionActions.ViewFlow
            ];

        return codes.SelectMany(code => actions.Select(action => P(NgbResourceKinds.Document, code, action)));
    }

    private static IEnumerable<PropertyManagementSecurityPermissionDefault> DocumentRead(
        IEnumerable<string> codes,
        bool includeAudit = false)
    {
        var actions = includeAudit
            ? new[]
            {
                NgbPermissionActions.View,
                NgbPermissionActions.Lookup,
                NgbPermissionActions.ViewEffects,
                NgbPermissionActions.ViewFlow,
                NgbPermissionActions.ViewAudit
            }
            : [NgbPermissionActions.View, NgbPermissionActions.Lookup];

        return codes.SelectMany(code => actions.Select(action => P(NgbResourceKinds.Document, code, action)));
    }

    private static IEnumerable<PropertyManagementSecurityPermissionDefault> CatalogEdit(IEnumerable<string> codes)
        => codes.SelectMany(static code => new[]
        {
            NgbPermissionActions.View,
            NgbPermissionActions.Lookup,
            NgbPermissionActions.Create,
            NgbPermissionActions.Edit
        }.Select(action => P(NgbResourceKinds.Catalog, code, action)));

    private static IEnumerable<PropertyManagementSecurityPermissionDefault> CatalogViewAndLookup(IEnumerable<string> codes)
        => codes.SelectMany(static code => new[]
        {
            NgbPermissionActions.View,
            NgbPermissionActions.Lookup
        }.Select(action => P(NgbResourceKinds.Catalog, code, action)));

    private static IEnumerable<PropertyManagementSecurityPermissionDefault> ReportExecute(IEnumerable<string> codes)
        => codes.SelectMany(static code => new[]
        {
            NgbPermissionActions.View,
            NgbPermissionActions.Execute
        }.Select(action => P(NgbResourceKinds.Report, code, action)));

    private static IEnumerable<PropertyManagementSecurityPermissionDefault> ReportExecuteExport(IEnumerable<string> codes)
        => codes.SelectMany(static code => new[]
        {
            NgbPermissionActions.View,
            NgbPermissionActions.Execute,
            NgbPermissionActions.Export,
            NgbPermissionActions.SavePrivateVariant,
            NgbPermissionActions.ManageSharedVariants,
            NgbPermissionActions.DeleteVariant
        }.Select(action => P(NgbResourceKinds.Report, code, action)));

    private static IEnumerable<PropertyManagementSecurityPermissionDefault> PageView(IEnumerable<string> codes)
        => codes.Select(static code => P(NgbResourceKinds.Page, code, NgbPermissionActions.View));

    private static IEnumerable<PropertyManagementSecurityPermissionDefault> AdminAccounting(bool manage)
    {
        yield return P(NgbResourceKinds.Admin, NgbPermissionResources.ChartOfAccounts, NgbPermissionActions.View);
        yield return P(NgbResourceKinds.Admin, NgbPermissionResources.PeriodClosing, NgbPermissionActions.View);
        yield return P(NgbResourceKinds.Admin, NgbPermissionResources.PostingLog, NgbPermissionActions.View);
        yield return P(NgbResourceKinds.Admin, NgbPermissionResources.Integrity, NgbPermissionActions.View);

        if (!manage)
            yield break;

        yield return P(NgbResourceKinds.Admin, NgbPermissionResources.ChartOfAccounts, NgbPermissionActions.Manage);
        yield return P(NgbResourceKinds.Admin, NgbPermissionResources.PeriodClosing, NgbPermissionActions.CloseMonth);
        yield return P(NgbResourceKinds.Admin, NgbPermissionResources.PeriodClosing, NgbPermissionActions.ReopenMonth);
        yield return P(NgbResourceKinds.Admin, NgbPermissionResources.PeriodClosing, NgbPermissionActions.CloseFiscalYear);
        yield return P(NgbResourceKinds.Admin, NgbPermissionResources.PeriodClosing, NgbPermissionActions.ReopenFiscalYear);
    }

    private static PropertyManagementSecurityPermissionDefault P(string kind, string code, string action)
        => new(kind, code, action);
}

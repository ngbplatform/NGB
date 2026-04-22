using NGB.Api.Models;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Admin;
using NGB.Metadata.Catalogs.Storage;
using NGB.Metadata.Documents.Storage;

namespace NGB.PropertyManagement.Api.Services;

/// <summary>
/// Curated Property Management main menu contributor.
/// Keeps IA domain-driven while still checking registry availability for PM catalog and document endpoints.
/// </summary>
internal sealed class PropertyManagementMainMenuContributor(
    ICatalogTypeRegistry catalogs,
    IDocumentTypeRegistry documents,
    ExternalLinksSettings externalLinks)
    : IMainMenuContributor
{
    public Task<IReadOnlyList<MainMenuGroupDto>> ContributeAsync(CancellationToken ct)
    {
        var availableCatalogs = catalogs
            .All()
            .Select(static x => x.CatalogCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var availableDocuments = documents
            .GetAll()
            .Select(static x => x.TypeCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<MainMenuGroupDto> groups =
        [
            CreateGroup(
                label: "Dashboard",
                icon: "home",
                ordinal: 10,
                CreatePageItem("pm.home", "Dashboard", "/home", "home", 10)),
            CreateGroup(
                label: "Portfolio",
                icon: "building-2",
                ordinal: 20,
                CreateCatalogItem(availableCatalogs, PropertyManagementCodes.Property, "Properties & Units", "building-2", 10),
                CreateCatalogItem(availableCatalogs, PropertyManagementCodes.Party, "Parties", "users", 20),
                CreateDocumentItem(availableDocuments, PropertyManagementCodes.Lease, "Leases", "file-text", 30),
                CreatePageItem("pm.building.summary", "Building Summary", "/reports/pm.building.summary", "bar-chart", 40),
                CreatePageItem("pm.occupancy.summary", "Occupancy Summary", "/reports/pm.occupancy.summary", "bar-chart", 50)),
            CreateGroup(
                label: "Receivables",
                icon: "coins",
                ordinal: 30,
                CreatePageItem("pm.receivables.open_items.page", "Open Items", "/receivables/open-items", "list", 10),
                CreatePageItem("pm.receivables.reconciliation", "Reconciliation", "/receivables/reconciliation", "scale", 20),
                CreateDocumentItem(availableDocuments, PropertyManagementCodes.RentCharge, "Rent Charges", "file-text", 30),
                CreateDocumentItem(availableDocuments, PropertyManagementCodes.ReceivableCharge, "Other Charges", "file-text", 40),
                CreateDocumentItem(availableDocuments, PropertyManagementCodes.LateFeeCharge, "Late Fees", "file-text", 50),
                CreateDocumentItem(availableDocuments, PropertyManagementCodes.ReceivablePayment, "Payments", "receipt", 60),
                CreateDocumentItem(availableDocuments, PropertyManagementCodes.ReceivableReturnedPayment, "Returned Payments", "receipt", 70),
                CreateDocumentItem(availableDocuments, PropertyManagementCodes.ReceivableCreditMemo, "Credit Memos", "file-text", 80),
                CreateDocumentItem(availableDocuments, PropertyManagementCodes.ReceivableApply, "Allocations", "git-merge", 90),
                CreatePageItem(PropertyManagementCodes.TenantStatement, "Tenant Statement", "/reports/pm.tenant.statement", "bar-chart", 100),
                CreatePageItem("pm.receivables.aging", "Aging", "/reports/pm.receivables.aging", "bar-chart", 110),
                CreatePageItem("pm.receivables.open_items", "Open Items Report", "/reports/pm.receivables.open_items", "bar-chart", 120),
                CreatePageItem("pm.receivables.open_items.details", "Open Items Detail", "/reports/pm.receivables.open_items.details", "bar-chart", 130)),
            CreateGroup(
                label: "Payables",
                icon: "wallet",
                ordinal: 40,
                CreatePageItem("pm.payables.open_items", "Open Items", "/payables/open-items", "list", 10),
                CreatePageItem("pm.payables.reconciliation", "Reconciliation", "/payables/reconciliation", "scale", 20),
                CreateDocumentItem(availableDocuments, PropertyManagementCodes.PayableCharge, "Charges", "file-text", 30),
                CreateDocumentItem(availableDocuments, PropertyManagementCodes.PayablePayment, "Payments", "receipt", 40),
                CreateDocumentItem(availableDocuments, PropertyManagementCodes.PayableCreditMemo, "Credit Memos", "file-text", 50),
                CreateDocumentItem(availableDocuments, PropertyManagementCodes.PayableApply, "Allocations", "git-merge", 60)),
            CreateGroup(
                label: "Maintenance",
                icon: "wrench",
                ordinal: 50,
                CreateDocumentItem(availableDocuments, PropertyManagementCodes.MaintenanceRequest, "Requests", "clipboard-list", 10),
                CreateDocumentItem(availableDocuments, PropertyManagementCodes.WorkOrder, "Work Orders", "wrench", 20),
                CreateDocumentItem(availableDocuments, PropertyManagementCodes.WorkOrderCompletion, "Completions", "check-square", 30),
                CreatePageItem("pm.maintenance.queue", "Open Queue", "/reports/pm.maintenance.queue", "bar-chart", 40),
                CreateCatalogItem(availableCatalogs, PropertyManagementCodes.MaintenanceCategory, "Categories", "tag", 50)),
            CreateGroup(
                label: "Setup & Controls",
                icon: "settings",
                ordinal: 70,
                CreateCatalogItem(availableCatalogs, PropertyManagementCodes.AccountingPolicy, "Accounting Policy", "settings", 10),
                CreateCatalogItem(availableCatalogs, PropertyManagementCodes.BankAccount, "Bank Accounts", "landmark", 30),
                CreateCatalogItem(availableCatalogs, PropertyManagementCodes.ReceivableChargeType, "Receivable Charge Types", "tag", 40),
                CreateCatalogItem(availableCatalogs, PropertyManagementCodes.PayableChargeType, "Payable Charge Types", "tag", 50),
                CreateExternalItem(externalLinks.HealthUiUrl, PropertyManagementCodes.Watchdog, "Health", "heart-pulse", 90),
                CreateExternalItem(externalLinks.BackgroundJobsUiUrl, PropertyManagementCodes.BackgroundJobs, "Background Jobs", "cogs", 100))
        ];

        return Task.FromResult(groups);
    }

    private static MainMenuGroupDto CreateGroup(string label, string icon, int ordinal, params MainMenuItemDto?[] items)
    {
        var visibleItems = items
            .Where(static item => item is not null)
            .Cast<MainMenuItemDto>()
            .OrderBy(static item => item.Ordinal)
            .ToArray();

        return new MainMenuGroupDto(Label: label, Items: visibleItems, Ordinal: ordinal, Icon: icon);
    }

    private static MainMenuItemDto CreatePageItem(string code, string label, string route, string icon, int ordinal)
        => new(Kind: "page", Code: code, Label: label, Route: route, Icon: icon, Ordinal: ordinal);

    private static MainMenuItemDto? CreateExternalItem(
        string? url,
        string code,
        string label,
        string icon,
        int ordinal)
        => string.IsNullOrWhiteSpace(url)
            ? null
            : new MainMenuItemDto(
                Kind: "external",
                Code: code,
                Label: label,
                Route: url,
                Icon: icon,
                Ordinal: ordinal);

    private static MainMenuItemDto? CreateCatalogItem(
        IReadOnlySet<string> availableCatalogs,
        string catalogCode,
        string label,
        string icon,
        int ordinal)
        => availableCatalogs.Contains(catalogCode)
            ? new MainMenuItemDto(
                Kind: "catalog",
                Code: catalogCode,
                Label: label,
                Route: $"/catalogs/{catalogCode}",
                Icon: icon,
                Ordinal: ordinal)
            : null;

    private static MainMenuItemDto? CreateDocumentItem(
        IReadOnlySet<string> availableDocuments,
        string documentType,
        string label,
        string icon,
        int ordinal)
        => availableDocuments.Contains(documentType)
            ? new MainMenuItemDto(
                Kind: "document",
                Code: documentType,
                Label: label,
                Route: $"/documents/{documentType}",
                Icon: icon,
                Ordinal: ordinal)
            : null;
}

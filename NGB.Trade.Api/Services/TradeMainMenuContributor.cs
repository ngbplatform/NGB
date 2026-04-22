using NGB.Api.Models;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Admin;
using NGB.Metadata.Catalogs.Storage;
using NGB.Metadata.Documents.Storage;

namespace NGB.Trade.Api.Services;

internal sealed class TradeMainMenuContributor(
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
                "Dashboard",
                "home",
                5,
                CreatePageItem(TradeCodes.DashboardOverviewReport, "Dashboard", "/home", "home", 10)),
            CreateGroup(
                "Inventory",
                "grid",
                10,
                CreateCatalogItem(availableCatalogs, TradeCodes.Item, "Items", "tag", 10),
                CreateCatalogItem(availableCatalogs, TradeCodes.Warehouse, "Warehouses", "building-2", 20),
                CreateCatalogItem(availableCatalogs, TradeCodes.UnitOfMeasure, "Units of Measure", "scale", 30),
                CreateDocumentItem(availableDocuments, TradeCodes.InventoryTransfer, "Inventory Transfers", "file-text", 40),
                CreateDocumentItem(availableDocuments, TradeCodes.InventoryAdjustment, "Inventory Adjustments", "file-text", 50),
                CreatePageItem(TradeCodes.InventoryBalancesReport, "Inventory Balances", $"/reports/{TradeCodes.InventoryBalancesReport}", "bar-chart", 60),
                CreatePageItem(TradeCodes.InventoryMovementsReport, "Inventory Movements", $"/reports/{TradeCodes.InventoryMovementsReport}", "bar-chart", 70)),
            CreateGroup(
                "Purchasing",
                "wallet",
                15,
                CreateCatalogItem(availableCatalogs, TradeCodes.Party, "Parties", "users", 10),
                CreateDocumentItem(availableDocuments, TradeCodes.PurchaseReceipt, "Purchase Receipts", "file-text", 20),
                CreateDocumentItem(availableDocuments, TradeCodes.VendorPayment, "Vendor Payments", "file-text", 30),
                CreateDocumentItem(availableDocuments, TradeCodes.VendorReturn, "Vendor Returns", "file-text", 40),
                CreatePageItem(TradeCodes.PurchasesByVendorReport, "Purchases by Vendor", $"/reports/{TradeCodes.PurchasesByVendorReport}", "bar-chart", 50)),
            CreateGroup(
                "Sales",
                "coins",
                18,
                CreateCatalogItem(availableCatalogs, TradeCodes.Party, "Parties", "users", 10),
                CreateDocumentItem(availableDocuments, TradeCodes.SalesInvoice, "Sales Invoices", "file-text", 20),
                CreateDocumentItem(availableDocuments, TradeCodes.CustomerPayment, "Customer Payments", "file-text", 30),
                CreateDocumentItem(availableDocuments, TradeCodes.CustomerReturn, "Customer Returns", "file-text", 40),
                CreatePageItem(TradeCodes.SalesByItemReport, "Sales by Item", $"/reports/{TradeCodes.SalesByItemReport}", "bar-chart", 50),
                CreatePageItem(TradeCodes.SalesByCustomerReport, "Sales by Customer", $"/reports/{TradeCodes.SalesByCustomerReport}", "bar-chart", 60)),
            CreateGroup(
                "Pricing",
                "tag",
                20,
                CreateCatalogItem(availableCatalogs, TradeCodes.PriceType, "Price Types", "tag", 10),
                CreateDocumentItem(availableDocuments, TradeCodes.ItemPriceUpdate, "Item Price Updates", "file-text", 20),
                CreatePageItem(TradeCodes.CurrentItemPricesReport, "Current Item Prices", $"/reports/{TradeCodes.CurrentItemPricesReport}", "bar-chart", 30)),
            CreateGroup(
                "Setup & Controls",
                "settings",
                70,
                CreateCatalogItem(availableCatalogs, TradeCodes.AccountingPolicy, "Accounting Policy", "settings", 10),
                CreateCatalogItem(availableCatalogs, TradeCodes.PaymentTerms, "Payment Terms", "list", 20),
                CreateCatalogItem(availableCatalogs, TradeCodes.InventoryAdjustmentReason, "Inventory Adjustment Reasons", "clipboard-list", 30),
                CreateExternalItem(externalLinks.HealthUiUrl, TradeCodes.Watchdog, "Health", "heart-pulse", 90),
                CreateExternalItem(externalLinks.BackgroundJobsUiUrl, TradeCodes.BackgroundJobs, "Background Jobs", "cogs", 100))
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

        return new MainMenuGroupDto(label, visibleItems, ordinal, icon);
    }

    private static MainMenuItemDto CreatePageItem(string code, string label, string route, string icon, int ordinal)
        => new("page", code, label, route, icon, ordinal);

    private static MainMenuItemDto? CreateExternalItem(string? url, string code, string label, string icon, int ordinal)
        => string.IsNullOrWhiteSpace(url)
            ? null
            : new MainMenuItemDto("external", code, label, url, icon, ordinal);

    private static MainMenuItemDto? CreateCatalogItem(
        IReadOnlySet<string> availableCatalogs,
        string catalogCode,
        string label,
        string icon,
        int ordinal)
        => availableCatalogs.Contains(catalogCode)
            ? new MainMenuItemDto("catalog", catalogCode, label, $"/catalogs/{catalogCode}", icon, ordinal)
            : null;

    private static MainMenuItemDto? CreateDocumentItem(
        IReadOnlySet<string> availableDocuments,
        string documentType,
        string label,
        string icon,
        int ordinal)
        => availableDocuments.Contains(documentType)
            ? new MainMenuItemDto("document", documentType, label, $"/documents/{documentType}", icon, ordinal)
            : null;
}

using NGB.Application.Abstractions.Services;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;
using NGB.Trade.Runtime.Reporting.Datasets;

namespace NGB.Trade.Runtime.Reporting;

public sealed class TradeCanonicalReportDefinitionSource : IReportDefinitionSource
{
    public IReadOnlyList<ReportDefinitionDto> GetDefinitions()
        =>
        [
            new(
                ReportCode: TradeCodes.DashboardOverviewReport,
                Name: "Trade Overview",
                Group: "Dashboard",
                Description: "Month-to-date trade KPIs, top selling items, and recent documents.",
                Mode: ReportExecutionMode.Canonical,
                Capabilities: BuildCapabilities(
                    allowsFilters: false,
                    allowsVariants: false,
                    allowsGrandTotals: false,
                    maxVisibleColumns: 5,
                    maxVisibleRows: 32,
                    maxRenderedCells: 160),
                DefaultLayout: new ReportLayoutDto(
                    ShowDetails: false,
                    ShowSubtotals: false,
                    ShowGrandTotals: false),
                Parameters:
                [
                    new ReportParameterMetadataDto(
                        "as_of_utc",
                        "date",
                        false,
                        Label: "As of")
                ],
                Presentation: new ReportPresentationDto(
                    InitialPageSize: 32,
                    RowNoun: "dashboard rows",
                    EmptyStateMessage: "No dashboard activity yet.")),
            new(
                ReportCode: TradeCodes.InventoryBalancesReport,
                Name: "Inventory Balances",
                Group: "Inventory",
                Description: "Flexible inventory analysis by warehouse and item",
                Mode: ReportExecutionMode.Composable,
                Dataset: TradeInventoryBalancesDatasetModel.Create(),
                Capabilities: BuildComposableCapabilities(
                    maxRowGroupDepth: 4,
                    maxVisibleColumns: 12,
                    maxVisibleRows: 2_000,
                    maxRenderedCells: 24_000),
                DefaultLayout: new ReportLayoutDto(
                    RowGroups:
                    [
                        new ReportGroupingDto("warehouse_display"),
                        new ReportGroupingDto("item_display")
                    ],
                    Measures:
                    [
                        new ReportMeasureSelectionDto("quantity_on_hand")
                    ],
                    Sorts:
                    [
                        new ReportSortDto("warehouse_display"),
                        new ReportSortDto("item_display")
                    ],
                    ShowDetails: false,
                    ShowSubtotals: true,
                    ShowSubtotalsOnSeparateRows: false,
                    ShowGrandTotals: true),
                Parameters:
                [
                    new ReportParameterMetadataDto(
                        "as_of_utc",
                        "date",
                        false,
                        Label: "As of")
                ],
                Filters:
                [
                    LookupFilter("item_id", "Item", TradeCodes.Item),
                    LookupFilter("warehouse_id", "Warehouse", TradeCodes.Warehouse)
                ],
                Presentation: new ReportPresentationDto(
                    InitialPageSize: 200,
                    RowNoun: "balance rows",
                    EmptyStateMessage: "No inventory balance positions match the selected criteria.")),
            new(
                ReportCode: TradeCodes.InventoryMovementsReport,
                Name: "Inventory Movements",
                Group: "Inventory",
                Description: "Audit stock transactions by warehouse, item, and document",
                Mode: ReportExecutionMode.Composable,
                Dataset: TradeInventoryMovementsDatasetModel.Create(),
                Capabilities: BuildComposableCapabilities(
                    maxRowGroupDepth: 5,
                    maxVisibleColumns: 16,
                    maxVisibleRows: 5_000,
                    maxRenderedCells: 60_000),
                DefaultLayout: new ReportLayoutDto(
                    RowGroups:
                    [
                        new ReportGroupingDto("warehouse_display"),
                        new ReportGroupingDto("item_display")
                    ],
                    Measures:
                    [
                        new ReportMeasureSelectionDto("qty_in"),
                        new ReportMeasureSelectionDto("qty_out"),
                        new ReportMeasureSelectionDto("qty_delta")
                    ],
                    DetailFields:
                    [
                        "occurred_at_utc",
                        "document_display"
                    ],
                    Sorts:
                    [
                        new ReportSortDto("warehouse_display"),
                        new ReportSortDto("item_display"),
                        new ReportSortDto("occurred_at_utc")
                    ],
                    ShowDetails: true,
                    ShowSubtotals: true,
                    ShowSubtotalsOnSeparateRows: false,
                    ShowGrandTotals: true),
                Parameters:
                [
                    new ReportParameterMetadataDto(
                        "from_utc",
                        "date",
                        false,
                        Label: "From"),
                    new ReportParameterMetadataDto(
                        "to_utc",
                        "date",
                        false,
                        Label: "To")
                ],
                Filters:
                [
                    LookupFilter("item_id", "Item", TradeCodes.Item),
                    LookupFilter("warehouse_id", "Warehouse", TradeCodes.Warehouse)
                ],
                Presentation: new ReportPresentationDto(
                    InitialPageSize: 200,
                    RowNoun: "movement rows",
                    EmptyStateMessage: "No inventory movements match the selected criteria.")),
            new(
                ReportCode: TradeCodes.SalesByItemReport,
                Name: "Sales by Item",
                Group: "Sales",
                Description: "Net sales, returns, COGS, and gross margin by item",
                Mode: ReportExecutionMode.Canonical,
                Capabilities: BuildCapabilities(
                    allowsFilters: true,
                    allowsVariants: true,
                    allowsGrandTotals: true,
                    maxVisibleColumns: 9,
                    maxVisibleRows: 500,
                    maxRenderedCells: 4_500),
                DefaultLayout: new ReportLayoutDto(
                    ShowDetails: false,
                    ShowSubtotals: false,
                    ShowGrandTotals: true),
                Parameters: BuildPeriodParameters(),
                Filters:
                [
                    LookupFilter("item_id", "Item", TradeCodes.Item),
                    LookupFilter("customer_id", "Customer", TradeCodes.Party),
                    LookupFilter("warehouse_id", "Warehouse", TradeCodes.Warehouse)
                ],
                Presentation: new ReportPresentationDto(
                    InitialPageSize: 100,
                    RowNoun: "items",
                    EmptyStateMessage: "No sales activity for the selected period.")),
            new(
                ReportCode: TradeCodes.SalesByCustomerReport,
                Name: "Sales by Customer",
                Group: "Sales",
                Description: "Net sales, returns, and gross margin by customer",
                Mode: ReportExecutionMode.Canonical,
                Capabilities: BuildCapabilities(
                    allowsFilters: true,
                    allowsVariants: true,
                    allowsGrandTotals: true,
                    maxVisibleColumns: 9,
                    maxVisibleRows: 500,
                    maxRenderedCells: 4_500),
                DefaultLayout: new ReportLayoutDto(
                    ShowDetails: false,
                    ShowSubtotals: false,
                    ShowGrandTotals: true),
                Parameters: BuildPeriodParameters(),
                Filters:
                [
                    LookupFilter("customer_id", "Customer", TradeCodes.Party),
                    LookupFilter("item_id", "Item", TradeCodes.Item),
                    LookupFilter("warehouse_id", "Warehouse", TradeCodes.Warehouse)
                ],
                Presentation: new ReportPresentationDto(
                    InitialPageSize: 100,
                    RowNoun: "customers",
                    EmptyStateMessage: "No customer sales activity for the selected period.")),
            new(
                ReportCode: TradeCodes.PurchasesByVendorReport,
                Name: "Purchases by Vendor",
                Group: "Purchasing",
                Description: "Net purchases and vendor returns by vendor",
                Mode: ReportExecutionMode.Canonical,
                Capabilities: BuildCapabilities(
                    allowsFilters: true,
                    allowsVariants: true,
                    allowsGrandTotals: true,
                    maxVisibleColumns: 6,
                    maxVisibleRows: 500,
                    maxRenderedCells: 3_000),
                DefaultLayout: new ReportLayoutDto(
                    ShowDetails: false,
                    ShowSubtotals: false,
                    ShowGrandTotals: true),
                Parameters: BuildPeriodParameters(),
                Filters:
                [
                    LookupFilter("vendor_id", "Vendor", TradeCodes.Party),
                    LookupFilter("item_id", "Item", TradeCodes.Item),
                    LookupFilter("warehouse_id", "Warehouse", TradeCodes.Warehouse)
                ],
                Presentation: new ReportPresentationDto(
                    InitialPageSize: 100,
                    RowNoun: "vendors",
                    EmptyStateMessage: "No purchasing activity for the selected period.")),
            new(
                ReportCode: TradeCodes.CurrentItemPricesReport,
                Name: "Current Item Prices",
                Group: "Pricing",
                Description: "Current item prices by item and price type",
                Mode: ReportExecutionMode.Canonical,
                Capabilities: BuildCapabilities(
                    allowsFilters: true,
                    allowsVariants: true,
                    allowsGrandTotals: false,
                    maxVisibleColumns: 6,
                    maxVisibleRows: 500,
                    maxRenderedCells: 3_000),
                DefaultLayout: new ReportLayoutDto(
                    ShowDetails: false,
                    ShowSubtotals: false,
                    ShowGrandTotals: false),
                Filters:
                [
                    LookupFilter("item_id", "Item", TradeCodes.Item),
                    LookupFilter("price_type_id", "Price Type", TradeCodes.PriceType)
                ])
        ];

    private static ReportCapabilitiesDto BuildCapabilities(
        bool allowsFilters,
        bool allowsVariants,
        bool allowsGrandTotals,
        int maxVisibleColumns,
        int maxVisibleRows,
        int maxRenderedCells)
        => new(
            AllowsFilters: allowsFilters,
            AllowsRowGroups: false,
            AllowsColumnGroups: false,
            AllowsMeasures: false,
            AllowsDetailFields: false,
            AllowsSorting: false,
            AllowsShowDetails: false,
            AllowsSubtotals: false,
            AllowsGrandTotals: allowsGrandTotals,
            AllowsVariants: allowsVariants,
            AllowsXlsxExport: true,
            MaxVisibleColumns: maxVisibleColumns,
            MaxVisibleRows: maxVisibleRows,
            MaxRenderedCells: maxRenderedCells);

    private static ReportCapabilitiesDto BuildComposableCapabilities(
        int maxRowGroupDepth,
        int maxVisibleColumns,
        int maxVisibleRows,
        int maxRenderedCells)
        => new(
            AllowsFilters: true,
            AllowsRowGroups: true,
            AllowsColumnGroups: false,
            AllowsMeasures: true,
            AllowsDetailFields: true,
            AllowsSorting: true,
            AllowsShowDetails: true,
            AllowsSubtotals: true,
            AllowsSeparateRowSubtotals: true,
            AllowsGrandTotals: true,
            AllowsVariants: true,
            AllowsXlsxExport: true,
            MaxRowGroupDepth: maxRowGroupDepth,
            MaxVisibleColumns: maxVisibleColumns,
            MaxVisibleRows: maxVisibleRows,
            MaxRenderedCells: maxRenderedCells);

    private static IReadOnlyList<ReportParameterMetadataDto> BuildPeriodParameters()
        =>
        [
            new ReportParameterMetadataDto(
                "from_utc",
                "date",
                false,
                Label: "From"),
            new ReportParameterMetadataDto(
                "to_utc",
                "date",
                false,
                Label: "To")
        ];

    private static ReportFilterFieldDto LookupFilter(string fieldCode, string label, string catalogType)
        => new(
            FieldCode: fieldCode,
            Label: label,
            DataType: "uuid",
            IsRequired: false,
            IsMulti: true,
            Lookup: new CatalogLookupSourceDto(catalogType));
}

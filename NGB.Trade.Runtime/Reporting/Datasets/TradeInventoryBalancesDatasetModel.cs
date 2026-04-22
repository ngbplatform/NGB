using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;

namespace NGB.Trade.Runtime.Reporting.Datasets;

public static class TradeInventoryBalancesDatasetModel
{
    public const string DatasetCode = TradeCodes.InventoryBalancesReport;

    public static ReportDatasetDto Create()
        => new(
            DatasetCode: DatasetCode,
            Fields:
            [
                new ReportFieldDto(
                    "warehouse_id",
                    "Warehouse",
                    "uuid",
                    ReportFieldKind.Dimension,
                    IsFilterable: true,
                    Lookup: new CatalogLookupSourceDto(TradeCodes.Warehouse)),
                new ReportFieldDto(
                    "warehouse_display",
                    "Warehouse",
                    "string",
                    ReportFieldKind.Dimension,
                    IsGroupable: true,
                    IsSortable: true,
                    IsSelectable: true),
                new ReportFieldDto(
                    "item_id",
                    "Item",
                    "uuid",
                    ReportFieldKind.Dimension,
                    IsFilterable: true,
                    Lookup: new CatalogLookupSourceDto(TradeCodes.Item)),
                new ReportFieldDto(
                    "item_display",
                    "Item",
                    "string",
                    ReportFieldKind.Dimension,
                    IsGroupable: true,
                    IsSortable: true,
                    IsSelectable: true),
                new ReportFieldDto(
                    "dimension_set_id",
                    "Dimension Set",
                    "uuid",
                    ReportFieldKind.System)
            ],
            Measures:
            [
                new ReportMeasureDto(
                    "quantity_on_hand",
                    "Quantity On Hand",
                    "decimal",
                    [ReportAggregationKind.Sum, ReportAggregationKind.Min, ReportAggregationKind.Max, ReportAggregationKind.Average])
            ]);
}

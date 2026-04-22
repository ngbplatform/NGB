using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;

namespace NGB.Trade.Runtime.Reporting.Datasets;

public static class TradeInventoryMovementsDatasetModel
{
    public const string DatasetCode = TradeCodes.InventoryMovementsReport;

    public static ReportDatasetDto Create()
        => new(
            DatasetCode: DatasetCode,
            Fields:
            [
                new ReportFieldDto(
                    "occurred_at_utc",
                    "Date",
                    "datetime",
                    ReportFieldKind.Time,
                    IsGroupable: true,
                    IsSortable: true,
                    IsSelectable: true,
                    SupportedTimeGrains: [ReportTimeGrain.Day, ReportTimeGrain.Week, ReportTimeGrain.Month, ReportTimeGrain.Quarter, ReportTimeGrain.Year]),
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
                    "document_id",
                    "Document",
                    "uuid",
                    ReportFieldKind.Dimension,
                    Lookup: new DocumentLookupSourceDto(
                    [
                        TradeCodes.PurchaseReceipt,
                        TradeCodes.SalesInvoice,
                        TradeCodes.InventoryTransfer,
                        TradeCodes.InventoryAdjustment,
                        TradeCodes.CustomerReturn,
                        TradeCodes.VendorReturn
                    ])),
                new ReportFieldDto(
                    "document_display",
                    "Document",
                    "string",
                    ReportFieldKind.Detail,
                    IsGroupable: true,
                    IsSortable: true,
                    IsSelectable: true),
                new ReportFieldDto(
                    "movement_id",
                    "Movement",
                    "int64",
                    ReportFieldKind.System),
                new ReportFieldDto(
                    "dimension_set_id",
                    "Dimension Set",
                    "uuid",
                    ReportFieldKind.System)
            ],
            Measures:
            [
                new ReportMeasureDto(
                    "qty_in",
                    "Qty In",
                    "decimal",
                    [ReportAggregationKind.Sum, ReportAggregationKind.Min, ReportAggregationKind.Max, ReportAggregationKind.Average]),
                new ReportMeasureDto(
                    "qty_out",
                    "Qty Out",
                    "decimal",
                    [ReportAggregationKind.Sum, ReportAggregationKind.Min, ReportAggregationKind.Max, ReportAggregationKind.Average]),
                new ReportMeasureDto(
                    "qty_delta",
                    "Qty Delta",
                    "decimal",
                    [ReportAggregationKind.Sum, ReportAggregationKind.Min, ReportAggregationKind.Max, ReportAggregationKind.Average])
            ]);
}

using NGB.Accounting.Documents;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;

namespace NGB.Runtime.Reporting.Datasets;

public static class AccountingLedgerAnalysisDatasetModel
{
    public const string DatasetCode = "accounting.ledger.analysis";

    public static ReportDatasetDto Create()
        => new(
            DatasetCode: DatasetCode,
            Fields:
            [
                new ReportFieldDto(
                    "entry_id",
                    "Entry",
                    "int64",
                    ReportFieldKind.System),
                new ReportFieldDto(
                    "posting_side",
                    "Posting Side",
                    "string",
                    ReportFieldKind.System),
                new ReportFieldDto(
                    "period_utc",
                    "Period",
                    "datetime",
                    ReportFieldKind.Time,
                    IsGroupable: true,
                    IsSortable: true,
                    IsSelectable: true,
                    SupportedTimeGrains: [ReportTimeGrain.Day, ReportTimeGrain.Week, ReportTimeGrain.Month, ReportTimeGrain.Quarter, ReportTimeGrain.Year]),
                new ReportFieldDto(
                    "account_id",
                    "Account",
                    "uuid",
                    ReportFieldKind.Dimension,
                    IsFilterable: true,
                    Lookup: new ChartOfAccountsLookupSourceDto()),
                new ReportFieldDto(
                    "account_display",
                    "Account",
                    "string",
                    ReportFieldKind.Dimension,
                    IsGroupable: true,
                    IsSortable: true,
                    IsSelectable: true),
                new ReportFieldDto(
                    "account_code",
                    "Account Code",
                    "string",
                    ReportFieldKind.Attribute),
                new ReportFieldDto(
                    "account_name",
                    "Account Name",
                    "string",
                    ReportFieldKind.Attribute),
                new ReportFieldDto(
                    "document_id",
                    "Document",
                    "uuid",
                    ReportFieldKind.Dimension,
                    Lookup: new DocumentLookupSourceDto([AccountingDocumentTypeCodes.GeneralJournalEntry])),
                new ReportFieldDto(
                    "document_display",
                    "Document",
                    "string",
                    ReportFieldKind.Detail,
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
                    "debit_amount",
                    "Debit",
                    "decimal",
                    [ReportAggregationKind.Sum, ReportAggregationKind.Min, ReportAggregationKind.Max, ReportAggregationKind.Average]),
                new ReportMeasureDto(
                    "credit_amount",
                    "Credit",
                    "decimal",
                    [ReportAggregationKind.Sum, ReportAggregationKind.Min, ReportAggregationKind.Max, ReportAggregationKind.Average]),
                new ReportMeasureDto(
                    "net_amount",
                    "Net",
                    "decimal",
                    [ReportAggregationKind.Sum, ReportAggregationKind.Min, ReportAggregationKind.Max, ReportAggregationKind.Average])
            ]);
}

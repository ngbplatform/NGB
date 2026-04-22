using NGB.Application.Abstractions.Services;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;
using NGB.Runtime.Reporting.Datasets;

namespace NGB.Runtime.Reporting.Definitions;

public sealed class AccountingLedgerAnalysisDefinitionSource : IReportDefinitionSource
{
    public IReadOnlyList<ReportDefinitionDto> GetDefinitions()
        =>
        [
            new ReportDefinitionDto(
                ReportCode: AccountingLedgerAnalysisDatasetModel.DatasetCode,
                Name: "Ledger Analysis",
                Group: "Accounting",
                Description: "Composable accounting ledger analysis",
                Mode: ReportExecutionMode.Composable,
                Dataset: AccountingLedgerAnalysisDatasetModel.Create(),
                Capabilities: new ReportCapabilitiesDto(
                    AllowsFilters: true,
                    AllowsRowGroups: true,
                    AllowsColumnGroups: true,
                    AllowsMeasures: true,
                    AllowsDetailFields: true,
                    AllowsSorting: true,
                    AllowsShowDetails: true,
                    AllowsSubtotals: true,
                    AllowsSeparateRowSubtotals: true,
                    AllowsGrandTotals: true,
                    AllowsVariants: true,
                    AllowsXlsxExport: true,
                    MaxRowGroupDepth: 5,
                    MaxColumnGroupDepth: 2,
                    MaxVisibleColumns: 64,
                    MaxVisibleRows: 5_000,
                    MaxRenderedCells: 100_000),
                DefaultLayout: new ReportLayoutDto(
                    RowGroups:
                    [
                        new ReportGroupingDto("account_display"),
                        new ReportGroupingDto("period_utc")
                    ],
                    Measures:
                    [
                        new ReportMeasureSelectionDto("debit_amount"),
                        new ReportMeasureSelectionDto("credit_amount"),
                        new ReportMeasureSelectionDto("net_amount")
                    ],
                    DetailFields: [],
                    Sorts:
                    [
                        new ReportSortDto("account_display"),
                        new ReportSortDto("period_utc")
                    ],
                    ShowDetails: false,
                    ShowSubtotals: true,
                    ShowGrandTotals: true),
                Parameters:
                [
                    new ReportParameterMetadataDto(
                        "from_utc",
                        "date",
                        true,
                        Label: "From"),
                    new ReportParameterMetadataDto(
                        "to_utc",
                        "date",
                        true,
                        Label: "To")
                ],
                Filters:
                [
                    new ReportFilterFieldDto(
                        "account_id",
                        "Account",
                        "uuid",
                        IsMulti: true,
                        Lookup: new ChartOfAccountsLookupSourceDto())
                ],
                Presentation: new ReportPresentationDto(
                    GroupedPagingMode: ReportGroupedPagingMode.BoundedNoCursor))
        ];
}

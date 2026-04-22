using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;
using NGB.Runtime.Ui;

namespace NGB.Runtime.Reporting.Definitions;

public sealed class CanonicalAccountingReportDefinitionSource : IReportDefinitionSource
{
    public IReadOnlyList<ReportDefinitionDto> GetDefinitions()
        =>
        [
            new(
                ReportCode: "accounting.trial_balance",
                Name: "Trial Balance",
                Group: "Accounting",
                Description: "Complete summary of ledger accounts",
                Mode: ReportExecutionMode.Canonical,
                Capabilities: new ReportCapabilitiesDto(
                    AllowsFilters: true,
                    AllowsRowGroups: false,
                    AllowsColumnGroups: false,
                    AllowsMeasures: false,
                    AllowsDetailFields: false,
                    AllowsSorting: false,
                    AllowsShowDetails: false,
                    AllowsSubtotals: true,
                    AllowsSeparateRowSubtotals: false,
                    AllowsGrandTotals: true,
                    AllowsVariants: true,
                    AllowsXlsxExport: true,
                    MaxVisibleColumns: 3,
                    MaxVisibleRows: 10_000,
                    MaxRenderedCells: 70_000),
                DefaultLayout: new ReportLayoutDto(
                    ShowDetails: false,
                    ShowSubtotals: true,
                    ShowGrandTotals: true),
                Parameters: CreateCanonicalDateRangeParameters(),
                Filters: []),
            new(
                ReportCode: "accounting.balance_sheet",
                Name: "Balance Sheet",
                Group: "Accounting",
                Description: "Statement of assets, liabilities, and equity as of month end",
                Mode: ReportExecutionMode.Canonical,
                Capabilities: new ReportCapabilitiesDto(
                    AllowsFilters: true,
                    AllowsRowGroups: false,
                    AllowsColumnGroups: false,
                    AllowsMeasures: false,
                    AllowsDetailFields: false,
                    AllowsSorting: false,
                    AllowsShowDetails: false,
                    AllowsSubtotals: true,
                    AllowsSeparateRowSubtotals: false,
                    AllowsGrandTotals: true,
                    AllowsVariants: true,
                    AllowsXlsxExport: true,
                    MaxVisibleColumns: 4,
                    MaxVisibleRows: 500,
                    MaxRenderedCells: 2_000),
                DefaultLayout: new ReportLayoutDto(
                    ShowDetails: false,
                    ShowSubtotals: true,
                    ShowGrandTotals: true),
                Parameters: CreateBalanceSheetParameters(),
                Filters: []),
            new(
                ReportCode: "accounting.income_statement",
                Name: "Income Statement",
                Group: "Accounting",
                Description: "Profit & loss",
                Mode: ReportExecutionMode.Canonical,
                Capabilities: new ReportCapabilitiesDto(
                    AllowsFilters: true,
                    AllowsRowGroups: false,
                    AllowsColumnGroups: false,
                    AllowsMeasures: false,
                    AllowsDetailFields: false,
                    AllowsSorting: false,
                    AllowsShowDetails: false,
                    AllowsSubtotals: true,
                    AllowsSeparateRowSubtotals: false,
                    AllowsGrandTotals: true,
                    AllowsVariants: true,
                    AllowsXlsxExport: true,
                    MaxVisibleColumns: 4,
                    MaxVisibleRows: 500,
                    MaxRenderedCells: 2_000),
                DefaultLayout: new ReportLayoutDto(
                    ShowDetails: false,
                    ShowSubtotals: true,
                    ShowGrandTotals: true),
                Parameters: CreateCanonicalDateRangeParameters(),
                Filters: []),
            new(
                ReportCode: "accounting.cash_flow_statement_indirect",
                Name: "Cash Flow Statement",
                Group: "Accounting",
                Description: "Indirect cash flow from operating, investing, and financing",
                Mode: ReportExecutionMode.Canonical,
                Capabilities: new ReportCapabilitiesDto(
                    AllowsFilters: false,
                    AllowsRowGroups: false,
                    AllowsColumnGroups: false,
                    AllowsMeasures: false,
                    AllowsDetailFields: false,
                    AllowsSorting: false,
                    AllowsShowDetails: false,
                    AllowsSubtotals: false,
                    AllowsSeparateRowSubtotals: false,
                    AllowsGrandTotals: false,
                    AllowsVariants: true,
                    AllowsXlsxExport: true,
                    MaxVisibleColumns: 2,
                    MaxVisibleRows: 200,
                    MaxRenderedCells: 2_000),
                DefaultLayout: new ReportLayoutDto(
                    ShowDetails: false,
                    ShowSubtotals: false,
                    ShowGrandTotals: false),
                Parameters: CreateCanonicalDateRangeParameters(),
                Filters: []),
            new(
                ReportCode: "accounting.statement_of_changes_in_equity",
                Name: "Statement of Changes in Equity",
                Group: "Accounting",
                Description: "Rollforward of equity from opening to closing period",
                Mode: ReportExecutionMode.Canonical,
                Capabilities: new ReportCapabilitiesDto(
                    AllowsFilters: true,
                    AllowsRowGroups: false,
                    AllowsColumnGroups: false,
                    AllowsMeasures: false,
                    AllowsDetailFields: false,
                    AllowsSorting: false,
                    AllowsShowDetails: false,
                    AllowsSubtotals: false,
                    AllowsSeparateRowSubtotals: false,
                    AllowsGrandTotals: true,
                    AllowsVariants: true,
                    AllowsXlsxExport: true,
                    MaxVisibleColumns: 4,
                    MaxVisibleRows: 500,
                    MaxRenderedCells: 2_500),
                DefaultLayout: new ReportLayoutDto(
                    ShowDetails: false,
                    ShowSubtotals: false,
                    ShowGrandTotals: true),
                Parameters: CreateCanonicalDateRangeParameters(),
                Filters: []),
            new(
                ReportCode: "accounting.general_journal",
                Name: "General Journal",
                Group: "Accounting",
                Description: "Transaction Log",
                Mode: ReportExecutionMode.Canonical,
                Capabilities: new ReportCapabilitiesDto(
                    AllowsFilters: true,
                    AllowsRowGroups: false,
                    AllowsColumnGroups: false,
                    AllowsMeasures: false,
                    AllowsDetailFields: false,
                    AllowsSorting: false,
                    AllowsShowDetails: false,
                    AllowsSubtotals: false,
                    AllowsSeparateRowSubtotals: false,
                    AllowsGrandTotals: false,
                    AllowsVariants: true,
                    AllowsXlsxExport: true,
                    MaxVisibleColumns: 8,
                    MaxVisibleRows: 100,
                    MaxRenderedCells: 9_000),
                DefaultLayout: new ReportLayoutDto(
                    ShowDetails: false,
                    ShowSubtotals: false,
                    ShowGrandTotals: false),
                Parameters: CreateCanonicalDateRangeParameters(),
                Presentation: new ReportPresentationDto(
                    InitialPageSize: 200,
                    RowNoun: "journal line"),
                Filters:
                [
                    new ReportFilterFieldDto(
                        "debit_account_id",
                        "Debit account",
                        "uuid",
                        Lookup: new ChartOfAccountsLookupSourceDto()),
                    new ReportFilterFieldDto(
                        "credit_account_id",
                        "Credit account",
                        "uuid",
                        Lookup: new ChartOfAccountsLookupSourceDto()),
                    new ReportFilterFieldDto(
                        "is_storno",
                        "Storno",
                        "bool",
                        Options: ReportFilterOptionTools.ToReportFilterOptions<YesNo>())
                ]),
            new(
                ReportCode: "accounting.account_card",
                Name: "Account Card",
                Group: "Accounting",
                Description: "Detailed register lines with running balance",
                Mode: ReportExecutionMode.Canonical,
                Capabilities: new ReportCapabilitiesDto(
                    AllowsFilters: true,
                    AllowsRowGroups: false,
                    AllowsColumnGroups: false,
                    AllowsMeasures: false,
                    AllowsDetailFields: false,
                    AllowsSorting: false,
                    AllowsShowDetails: false,
                    AllowsSubtotals: false,
                    AllowsSeparateRowSubtotals: false,
                    AllowsGrandTotals: false,
                    AllowsVariants: true,
                    AllowsXlsxExport: true,
                    MaxVisibleColumns: 7,
                    MaxVisibleRows: 100,
                    MaxRenderedCells: 10_000),
                DefaultLayout: new ReportLayoutDto(
                    ShowDetails: false,
                    ShowSubtotals: false,
                    ShowGrandTotals: false),
                Parameters: CreateCanonicalDateRangeParameters(),
                Presentation: new ReportPresentationDto(
                    InitialPageSize: 150,
                    RowNoun: "account card line",
                    EmptyStateMessage: "Open the Composer, choose an account and period, and run again."),
                Filters:
                [
                    new ReportFilterFieldDto(
                        "account_id",
                        "Account",
                        "uuid",
                        IsRequired: true,
                        Lookup: new ChartOfAccountsLookupSourceDto())
                ]),
            new(
                ReportCode: "accounting.general_ledger_aggregated",
                Name: "General Ledger",
                Group: "Accounting",
                Description: "Summary of all account balances and totals",
                Mode: ReportExecutionMode.Canonical,
                Capabilities: new ReportCapabilitiesDto(
                    AllowsFilters: true,
                    AllowsRowGroups: false,
                    AllowsColumnGroups: false,
                    AllowsMeasures: false,
                    AllowsDetailFields: false,
                    AllowsSorting: false,
                    AllowsShowDetails: false,
                    AllowsSubtotals: false,
                    AllowsSeparateRowSubtotals: false,
                    AllowsGrandTotals: true,
                    AllowsVariants: true,
                    AllowsXlsxExport: true,
                    MaxVisibleColumns: 8,
                    MaxVisibleRows: 10_000,
                    MaxRenderedCells: 90_000),
                DefaultLayout: new ReportLayoutDto(
                    ShowDetails: false,
                    ShowSubtotals: false,
                    ShowGrandTotals: true),
                Parameters: CreateCanonicalDateRangeParameters(),
                Presentation: new ReportPresentationDto(
                    InitialPageSize: 100,
                    RowNoun: "ledger line",
                    EmptyStateMessage: "Open the Composer, choose an account and period, and run again."),
                Filters:
                [
                    new ReportFilterFieldDto(
                        "account_id",
                        "Account",
                        "uuid",
                        IsRequired: true,
                        Lookup: new ChartOfAccountsLookupSourceDto())
                ]),
            new(
                ReportCode: "accounting.posting_log",
                Name: "Posting Log",
                Group: "Accounting",
                Description: "Posting engine activity log for diagnostics and support",
                Mode: ReportExecutionMode.Canonical,
                Capabilities: new ReportCapabilitiesDto(
                    AllowsFilters: true,
                    AllowsRowGroups: false,
                    AllowsColumnGroups: false,
                    AllowsMeasures: false,
                    AllowsDetailFields: false,
                    AllowsSorting: false,
                    AllowsShowDetails: false,
                    AllowsSubtotals: false,
                    AllowsSeparateRowSubtotals: false,
                    AllowsGrandTotals: false,
                    AllowsVariants: true,
                    AllowsXlsxExport: true,
                    MaxVisibleColumns: 7,
                    MaxVisibleRows: 100,
                    MaxRenderedCells: 7_000),
                DefaultLayout: new ReportLayoutDto(
                    ShowDetails: false,
                    ShowSubtotals: false,
                    ShowGrandTotals: false),
                Parameters: CreatePostingLogParameters(),
                Presentation: new ReportPresentationDto(
                    InitialPageSize: 100,
                    RowNoun: "posting operation",
                    EmptyStateMessage: "Adjust the time window or filters and run again."),
                Filters:
                [
                    new ReportFilterFieldDto(
                        "operation",
                        "Operation",
                        "string",
                        Options: ReportFilterOptionTools.ToReportFilterOptions<PostingOperation>()),
                    new ReportFilterFieldDto(
                        "status",
                        "Status",
                        "string",
                        Options: ReportFilterOptionTools.ToReportFilterOptions<PostingStateStatus>())
                ]),
            new(
                ReportCode: "accounting.consistency",
                Name: "Integrity Checks",
                Group: "Accounting",
                Description: "Turnover & balance diagnostics",
                Mode: ReportExecutionMode.Canonical,
                Capabilities: new ReportCapabilitiesDto(
                    AllowsFilters: true,
                    AllowsRowGroups: false,
                    AllowsColumnGroups: false,
                    AllowsMeasures: false,
                    AllowsDetailFields: false,
                    AllowsSorting: false,
                    AllowsShowDetails: false,
                    AllowsSubtotals: false,
                    AllowsSeparateRowSubtotals: false,
                    AllowsGrandTotals: true,
                    AllowsVariants: true,
                    AllowsXlsxExport: true,
                    MaxVisibleColumns: 6,
                    MaxVisibleRows: 500,
                    MaxRenderedCells: 3_000),
                DefaultLayout: new ReportLayoutDto(
                    ShowDetails: false,
                    ShowSubtotals: false,
                    ShowGrandTotals: true),
                Parameters: CreateConsistencyParameters(),
                Filters: [])
        ];

    private static ReportParameterMetadataDto[] CreateCanonicalDateRangeParameters()
        =>
        [
            new("from_utc", "date", true, Label: "From"),
            new("to_utc", "date", true, Label: "To")
        ];

    private static ReportParameterMetadataDto[] CreateBalanceSheetParameters()
        =>
        [
            new("as_of_utc", "date", true, Label: "As of")
        ];

    private static ReportParameterMetadataDto[] CreatePostingLogParameters()
        =>
        [
            new("from_utc", "date_time_utc", false, Label: "From"),
            new("to_utc", "date_time_utc", false, Label: "To")
        ];

    private static ReportParameterMetadataDto[] CreateConsistencyParameters()
        =>
        [
            new("period_utc", "date", true, Label: "Month"),
            new("previous_period_utc", "date", false, Description: "chain check", Label: "Previous month")
        ];
}

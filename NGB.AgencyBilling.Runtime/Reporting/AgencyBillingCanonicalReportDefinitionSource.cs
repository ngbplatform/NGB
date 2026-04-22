using NGB.Application.Abstractions.Services;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;
using NGB.AgencyBilling.Runtime.Reporting.Datasets;

namespace NGB.AgencyBilling.Runtime.Reporting;

public sealed class AgencyBillingCanonicalReportDefinitionSource : IReportDefinitionSource
{
    public IReadOnlyList<ReportDefinitionDto> GetDefinitions()
        =>
        [
            new(
                ReportCode: AgencyBillingCodes.UnbilledTimeReport,
                Name: "Unbilled Time",
                Group: "Operations",
                Description: "Open billable time that has not yet been invoiced",
                Mode: ReportExecutionMode.Composable,
                Dataset: AgencyBillingUnbilledTimeDatasetModel.Create(),
                Capabilities: BuildComposableCapabilities(maxRowGroupDepth: 4, maxVisibleColumns: 12, maxVisibleRows: 2_000, maxRenderedCells: 24_000),
                DefaultLayout: new ReportLayoutDto(
                    RowGroups:
                    [
                        new ReportGroupingDto("client_display"),
                        new ReportGroupingDto("project_display"),
                        new ReportGroupingDto("team_member_display")
                    ],
                    Measures:
                    [
                        new ReportMeasureSelectionDto("hours_open"),
                        new ReportMeasureSelectionDto("amount_open")
                    ],
                    Sorts:
                    [
                        new ReportSortDto("client_display"),
                        new ReportSortDto("project_display"),
                        new ReportSortDto("team_member_display")
                    ],
                    ShowDetails: false,
                    ShowSubtotals: true,
                    ShowSubtotalsOnSeparateRows: false,
                    ShowGrandTotals: true),
                Parameters:
                [
                    new ReportParameterMetadataDto("as_of_utc", "date", true, Label: "As of")
                ],
                Filters:
                [
                    LookupFilter("client_id", "Client", AgencyBillingCodes.Client),
                    LookupFilter("project_id", "Project", AgencyBillingCodes.Project),
                    LookupFilter("team_member_id", "Team Member", AgencyBillingCodes.TeamMember)
                ],
                Presentation: new ReportPresentationDto(InitialPageSize: 200, RowNoun: "unbilled time rows", EmptyStateMessage: "No open unbilled time matches the selected criteria.")),
            new(
                ReportCode: AgencyBillingCodes.ProjectProfitabilityReport,
                Name: "Project Profitability",
                Group: "Finance",
                Description: "Revenue, cost, margin, and collections by project",
                Mode: ReportExecutionMode.Composable,
                Dataset: AgencyBillingProjectProfitabilityDatasetModel.Create(),
                Capabilities: BuildComposableCapabilities(maxRowGroupDepth: 4, maxVisibleColumns: 14, maxVisibleRows: 2_000, maxRenderedCells: 28_000),
                DefaultLayout: new ReportLayoutDto(
                    RowGroups:
                    [
                        new ReportGroupingDto("client_display"),
                        new ReportGroupingDto("project_display")
                    ],
                    Measures:
                    [
                        new ReportMeasureSelectionDto("billed_amount"),
                        new ReportMeasureSelectionDto("cost_amount"),
                        new ReportMeasureSelectionDto("gross_margin_amount"),
                        new ReportMeasureSelectionDto("outstanding_ar_amount")
                    ],
                    Sorts:
                    [
                        new ReportSortDto("client_display"),
                        new ReportSortDto("project_display")
                    ],
                    ShowDetails: false,
                    ShowSubtotals: true,
                    ShowSubtotalsOnSeparateRows: false,
                    ShowGrandTotals: true),
                Parameters:
                [
                    new ReportParameterMetadataDto("as_of_utc", "date", true, Label: "As of")
                ],
                Filters:
                [
                    LookupFilter("client_id", "Client", AgencyBillingCodes.Client),
                    LookupFilter("project_id", "Project", AgencyBillingCodes.Project),
                    LookupFilter("project_manager_id", "Project Manager", AgencyBillingCodes.TeamMember)
                ],
                Presentation: new ReportPresentationDto(InitialPageSize: 200, RowNoun: "project rows", EmptyStateMessage: "No project profitability data is available for the selected criteria.")),
            new(
                ReportCode: AgencyBillingCodes.InvoiceRegisterReport,
                Name: "Invoice Register",
                Group: "Billing",
                Description: "Invoice issue, due, applied, and open balance positions",
                Mode: ReportExecutionMode.Composable,
                Dataset: AgencyBillingInvoiceRegisterDatasetModel.Create(),
                Capabilities: BuildComposableCapabilities(maxRowGroupDepth: 4, maxVisibleColumns: 16, maxVisibleRows: 5_000, maxRenderedCells: 60_000),
                DefaultLayout: new ReportLayoutDto(
                    RowGroups:
                    [
                        new ReportGroupingDto("client_display"),
                        new ReportGroupingDto("project_display")
                    ],
                    Measures:
                    [
                        new ReportMeasureSelectionDto("invoice_amount"),
                        new ReportMeasureSelectionDto("applied_amount"),
                        new ReportMeasureSelectionDto("balance_amount")
                    ],
                    DetailFields:
                    [
                        "invoice_display",
                        "invoice_date",
                        "due_date",
                        "payment_status"
                    ],
                    Sorts:
                    [
                        new ReportSortDto("client_display"),
                        new ReportSortDto("project_display"),
                        new ReportSortDto("invoice_date")
                    ],
                    ShowDetails: true,
                    ShowSubtotals: true,
                    ShowSubtotalsOnSeparateRows: false,
                    ShowGrandTotals: true),
                Parameters:
                [
                    new ReportParameterMetadataDto("from_utc", "date", true, Label: "From"),
                    new ReportParameterMetadataDto("to_utc", "date", true, Label: "To")
                ],
                Filters:
                [
                    LookupFilter("client_id", "Client", AgencyBillingCodes.Client),
                    LookupFilter("project_id", "Project", AgencyBillingCodes.Project),
                    DocumentFilter("contract_id", "Client Contract", AgencyBillingCodes.ClientContract)
                ],
                Presentation: new ReportPresentationDto(InitialPageSize: 200, RowNoun: "invoice rows", EmptyStateMessage: "No invoices match the selected period and filters.")),
            new(
                ReportCode: AgencyBillingCodes.ArAgingReport,
                Name: "AR Aging",
                Group: "Finance",
                Description: "Outstanding receivables grouped by aging bucket",
                Mode: ReportExecutionMode.Composable,
                Dataset: AgencyBillingArAgingDatasetModel.Create(),
                Capabilities: BuildComposableCapabilities(maxRowGroupDepth: 4, maxVisibleColumns: 16, maxVisibleRows: 5_000, maxRenderedCells: 60_000),
                DefaultLayout: new ReportLayoutDto(
                    RowGroups:
                    [
                        new ReportGroupingDto("client_display"),
                        new ReportGroupingDto("aging_bucket")
                    ],
                    Measures:
                    [
                        new ReportMeasureSelectionDto("open_amount"),
                        new ReportMeasureSelectionDto("current_amount"),
                        new ReportMeasureSelectionDto("bucket_1_30_amount"),
                        new ReportMeasureSelectionDto("bucket_31_60_amount"),
                        new ReportMeasureSelectionDto("bucket_61_90_amount"),
                        new ReportMeasureSelectionDto("bucket_90_plus_amount")
                    ],
                    DetailFields:
                    [
                        "invoice_display",
                        "due_date",
                        "days_past_due"
                    ],
                    Sorts:
                    [
                        new ReportSortDto("client_display"),
                        new ReportSortDto("aging_bucket"),
                        new ReportSortDto("due_date")
                    ],
                    ShowDetails: true,
                    ShowSubtotals: true,
                    ShowSubtotalsOnSeparateRows: false,
                    ShowGrandTotals: true),
                Parameters:
                [
                    new ReportParameterMetadataDto("as_of_utc", "date", true, Label: "As of")
                ],
                Filters:
                [
                    LookupFilter("client_id", "Client", AgencyBillingCodes.Client),
                    LookupFilter("project_id", "Project", AgencyBillingCodes.Project)
                ],
                Presentation: new ReportPresentationDto(InitialPageSize: 200, RowNoun: "open invoice rows", EmptyStateMessage: "No outstanding receivables exist as of the selected date.")),
            new(
                ReportCode: AgencyBillingCodes.TeamUtilizationReport,
                Name: "Team Utilization",
                Group: "Delivery",
                Description: "Measures team billability across projects and services",
                Mode: ReportExecutionMode.Composable,
                Dataset: AgencyBillingTeamUtilizationDatasetModel.Create(),
                Capabilities: BuildComposableCapabilities(maxRowGroupDepth: 5, maxVisibleColumns: 16, maxVisibleRows: 5_000, maxRenderedCells: 60_000),
                DefaultLayout: new ReportLayoutDto(
                    RowGroups:
                    [
                        new ReportGroupingDto("team_member_display"),
                        new ReportGroupingDto("project_display")
                    ],
                    Measures:
                    [
                        new ReportMeasureSelectionDto("hours_total"),
                        new ReportMeasureSelectionDto("billable_hours"),
                        new ReportMeasureSelectionDto("non_billable_hours"),
                        new ReportMeasureSelectionDto("billable_amount"),
                        new ReportMeasureSelectionDto("cost_amount")
                    ],
                    Sorts:
                    [
                        new ReportSortDto("team_member_display"),
                        new ReportSortDto("project_display")
                    ],
                    ShowDetails: false,
                    ShowSubtotals: true,
                    ShowSubtotalsOnSeparateRows: false,
                    ShowGrandTotals: true),
                Parameters:
                [
                    new ReportParameterMetadataDto("from_utc", "date", true, Label: "From"),
                    new ReportParameterMetadataDto("to_utc", "date", true, Label: "To")
                ],
                Filters:
                [
                    LookupFilter("team_member_id", "Team Member", AgencyBillingCodes.TeamMember),
                    LookupFilter("client_id", "Client", AgencyBillingCodes.Client),
                    LookupFilter("project_id", "Project", AgencyBillingCodes.Project),
                    LookupFilter("service_item_id", "Service Item", AgencyBillingCodes.ServiceItem)
                ],
                Presentation: new ReportPresentationDto(InitialPageSize: 200, RowNoun: "utilization rows", EmptyStateMessage: "No utilization data exists for the selected period.")),
        ];

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

    private static ReportFilterFieldDto LookupFilter(string fieldCode, string label, string catalogType)
        => new(
            fieldCode,
            label,
            "uuid",
            IsRequired: false,
            IsMulti: true,
            Lookup: new CatalogLookupSourceDto(catalogType));

    private static ReportFilterFieldDto DocumentFilter(string fieldCode, string label, params string[] documentTypes)
        => new(
            fieldCode,
            label,
            "uuid",
            IsRequired: false,
            IsMulti: true,
            Lookup: new DocumentLookupSourceDto(documentTypes));
}

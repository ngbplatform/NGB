using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.CRM.Runtime.Reporting.Datasets;

namespace NGB.CRM.Runtime.Reporting;

public sealed class CrmCanonicalReportDefinitionSource : IReportDefinitionSource
{
    public IReadOnlyList<ReportDefinitionDto> GetDefinitions()
        =>
        [
            Composable(
                CrmCodes.SalesPipelineReport,
                "Sales Pipeline",
                "Pipeline",
                "Open and closed opportunities by account, stage, probability, and expected close date.",
                CrmPipelineDatasetModel.Create(),
                new ReportLayoutDto(
                    RowGroups:
                    [
                        new ReportGroupingDto("stage_display"),
                        new ReportGroupingDto("customer_display"),
                        new ReportGroupingDto("status"),
                        new ReportGroupingDto("opportunity_display")
                    ],
                    Measures:
                    [
                        new ReportMeasureSelectionDto("amount"),
                        new ReportMeasureSelectionDto("weighted_amount")
                    ],
                    Sorts:
                    [
                        new ReportSortDto("stage_display"),
                        new ReportSortDto("customer_display")
                    ],
                    ShowDetails: false)),
            Composable(
                CrmCodes.OpportunityHistoryReport,
                "Opportunity History",
                "Pipeline",
                "Chronological opportunity conversion and update events.",
                CrmOpportunityHistoryDatasetModel.Create(),
                new ReportLayoutDto(
                    RowGroups:
                    [
                        new ReportGroupingDto("customer_display"),
                        new ReportGroupingDto("opportunity_display"),
                        new ReportGroupingDto("stage_display")
                    ],
                    Measures:
                    [
                        new ReportMeasureSelectionDto("amount")
                    ],
                    Sorts:
                    [
                        new ReportSortDto("customer_display"),
                        new ReportSortDto("opportunity_display")
                    ],
                    ShowDetails: false)),
            Composable(
                CrmCodes.LeadConversionFunnelReport,
                "Lead Conversion Funnel",
                "Leads",
                "Lead intake, qualification, and conversion counts.",
                CrmLeadFunnelDatasetModel.Create(),
                new ReportLayoutDto(
                    RowGroups:
                    [
                        new ReportGroupingDto("funnel_step"),
                        new ReportGroupingDto("document_display")
                    ],
                    Measures:
                    [
                        new ReportMeasureSelectionDto("lead_count")
                    ],
                    Sorts:
                    [
                        new ReportSortDto("funnel_step")
                    ],
                    ShowDetails: false)),
            Composable(
                CrmCodes.ActivitySummaryReport,
                "Activity Summary",
                "Activities",
                "Completed and planned sales activities by type, account, contact, and outcome.",
                CrmActivitySummaryDatasetModel.Create(),
                new ReportLayoutDto(
                    RowGroups:
                    [
                        new ReportGroupingDto("activity_type"),
                        new ReportGroupingDto("customer_display"),
                        new ReportGroupingDto("contact_display"),
                        new ReportGroupingDto("outcome")
                    ],
                    Measures:
                    [
                        new ReportMeasureSelectionDto("activity_count")
                    ],
                    ShowDetails: false)),
            Composable(
                CrmCodes.QuoteRegisterReport,
                "Quote Register",
                "Quotes",
                "Posted quotes, statuses, validity, and amounts.",
                CrmQuoteRegisterDatasetModel.Create(),
                new ReportLayoutDto(
                    RowGroups:
                    [
                        new ReportGroupingDto("quote_status"),
                        new ReportGroupingDto("customer_display"),
                        new ReportGroupingDto("contact_display"),
                        new ReportGroupingDto("currency")
                    ],
                    Measures:
                    [
                        new ReportMeasureSelectionDto("amount"),
                        new ReportMeasureSelectionDto("quote_count")
                    ],
                    ShowDetails: false))
        ];

    private static ReportDefinitionDto Composable(
        string code,
        string name,
        string group,
        string description,
        ReportDatasetDto dataset,
        ReportLayoutDto layout)
        => new(
            ReportCode: code,
            Name: name,
            Group: group,
            Description: description,
            Mode: ReportExecutionMode.Composable,
            Dataset: dataset,
            Capabilities: new ReportCapabilitiesDto(
                AllowsFilters: true,
                AllowsRowGroups: true,
                AllowsMeasures: true,
                AllowsDetailFields: true,
                AllowsSorting: true,
                AllowsShowDetails: true,
                AllowsSubtotals: true,
                AllowsSeparateRowSubtotals: false,
                AllowsGrandTotals: true,
                AllowsVariants: true,
                AllowsXlsxExport: true,
                MaxRowGroupDepth: 4,
                MaxVisibleColumns: 16,
                MaxVisibleRows: 2_000,
                MaxRenderedCells: 32_000),
            DefaultLayout: layout,
            Presentation: new ReportPresentationDto(
                InitialPageSize: 200,
                RowNoun: "rows",
                EmptyStateMessage: "No CRM activity matches the selected criteria."));
}

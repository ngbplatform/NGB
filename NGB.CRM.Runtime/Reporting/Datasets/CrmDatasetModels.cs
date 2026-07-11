using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;

namespace NGB.CRM.Runtime.Reporting.Datasets;

public static class CrmPipelineDatasetModel
{
    public static ReportDatasetDto Create()
        => new(
            DatasetCode: CrmCodes.SalesPipelineReport,
            Fields:
            [
                DocumentLookupId("opportunity_id", "Opportunity", CrmCodes.LeadConversion),
                Text("opportunity_display", "Opportunity", selectable: true, groupable: true, sortable: true),
                CatalogLookupId("customer_id", "Account", CrmCodes.Account),
                Text("customer_display", "Account", selectable: true, groupable: true, sortable: true),
                CatalogLookupId("stage_id", "Stage", CrmCodes.OpportunityStage),
                Text("stage_display", "Stage", selectable: true, groupable: true, sortable: true),
                Text("status", "Status", selectable: true, groupable: true, sortable: true, filterable: true),
                Date("expected_close_date", "Expected Close", selectable: true, groupable: true, sortable: true, filterable: true)
            ],
            Measures:
            [
                Decimal("amount", "Amount"),
                Decimal("weighted_amount", "Weighted Amount"),
                Decimal("probability", "Probability")
            ]);

    internal static ReportFieldDto Text(
        string code,
        string label,
        bool selectable = false,
        bool groupable = false,
        bool sortable = false,
        bool filterable = false)
        => new(code, label, "string", ReportFieldKind.Dimension, filterable, groupable, sortable, selectable);

    internal static ReportFieldDto Date(
        string code,
        string label,
        bool selectable = false,
        bool groupable = false,
        bool sortable = false,
        bool filterable = false)
        => new(code, label, "date", ReportFieldKind.Dimension, filterable, groupable, sortable, selectable);

    internal static ReportFieldDto DateTime(
        string code,
        string label,
        bool selectable = false,
        bool groupable = false,
        bool sortable = false,
        bool filterable = false)
        => new(code, label, "datetime", ReportFieldKind.Dimension, filterable, groupable, sortable, selectable);

    internal static ReportFieldDto CatalogLookupId(string code, string label, string catalogType)
        => new(
            code,
            label,
            "uuid",
            ReportFieldKind.Dimension,
            IsFilterable: true,
            IsGroupable: false,
            IsSortable: false,
            IsSelectable: false,
            Lookup: new CatalogLookupSourceDto(catalogType));

    internal static ReportFieldDto DocumentLookupId(string code, string label, params string[] documentTypes)
        => new(
            code,
            label,
            "uuid",
            ReportFieldKind.Dimension,
            IsFilterable: true,
            IsGroupable: false,
            IsSortable: false,
            IsSelectable: false,
            Lookup: new DocumentLookupSourceDto(documentTypes));

    internal static ReportMeasureDto Decimal(string code, string label)
        => new(code, label, "decimal", [
            ReportAggregationKind.Sum,
            ReportAggregationKind.Min,
            ReportAggregationKind.Max,
            ReportAggregationKind.Average
        ]);

    internal static ReportMeasureDto Count(string code, string label)
        => new(code, label, "int64", [ReportAggregationKind.Sum]);
}

public static class CrmOpportunityHistoryDatasetModel
{
    public static ReportDatasetDto Create()
        => new(
            DatasetCode: CrmCodes.OpportunityHistoryReport,
            Fields:
            [
                CrmPipelineDatasetModel.DateTime("event_at_utc", "Event Time", selectable: true, groupable: true, sortable: true, filterable: true),
                CrmPipelineDatasetModel.Text("event_type", "Event Type", selectable: true, groupable: true, sortable: true, filterable: true),
                CrmPipelineDatasetModel.DocumentLookupId("opportunity_id", "Opportunity", CrmCodes.LeadConversion),
                CrmPipelineDatasetModel.Text("opportunity_display", "Opportunity", selectable: true, groupable: true, sortable: true),
                CrmPipelineDatasetModel.CatalogLookupId("customer_id", "Account", CrmCodes.Account),
                CrmPipelineDatasetModel.Text("customer_display", "Account", selectable: true, groupable: true, sortable: true),
                CrmPipelineDatasetModel.CatalogLookupId("stage_id", "Stage", CrmCodes.OpportunityStage),
                CrmPipelineDatasetModel.Text("stage_display", "Stage", selectable: true, groupable: true, sortable: true),
                CrmPipelineDatasetModel.Text("status", "Status", selectable: true, groupable: true, sortable: true, filterable: true)
            ],
            Measures:
            [
                CrmPipelineDatasetModel.Decimal("amount", "Amount"),
                CrmPipelineDatasetModel.Decimal("probability", "Probability")
            ]);
}

public static class CrmLeadFunnelDatasetModel
{
    public static ReportDatasetDto Create()
        => new(
            DatasetCode: CrmCodes.LeadConversionFunnelReport,
            Fields:
            [
                CrmPipelineDatasetModel.DateTime("event_at_utc", "Event Time", selectable: true, groupable: true, sortable: true, filterable: true),
                CrmPipelineDatasetModel.Text("funnel_step", "Funnel Step", selectable: true, groupable: true, sortable: true),
                CrmPipelineDatasetModel.Text("lead_source", "Lead Source", selectable: true, groupable: true, sortable: true, filterable: true),
                CrmPipelineDatasetModel.Text("industry", "Industry", selectable: true, groupable: true, sortable: true, filterable: true),
                CrmPipelineDatasetModel.DocumentLookupId(
                    "document_id",
                    "Document",
                    CrmCodes.LeadIntake,
                    CrmCodes.LeadQualification,
                    CrmCodes.LeadConversion),
                CrmPipelineDatasetModel.Text("document_display", "Document", selectable: true, groupable: true, sortable: true)
            ],
            Measures:
            [
                CrmPipelineDatasetModel.Count("lead_count", "Leads")
            ]);
}

public static class CrmActivitySummaryDatasetModel
{
    public static ReportDatasetDto Create()
        => new(
            DatasetCode: CrmCodes.ActivitySummaryReport,
            Fields:
            [
                CrmPipelineDatasetModel.Date("activity_date", "Activity Date", selectable: true, groupable: true, sortable: true, filterable: true),
                CrmPipelineDatasetModel.Text("activity_type", "Activity Type", selectable: true, groupable: true, sortable: true, filterable: true),
                CrmPipelineDatasetModel.CatalogLookupId("customer_id", "Account", CrmCodes.Account),
                CrmPipelineDatasetModel.Text("customer_display", "Account", selectable: true, groupable: true, sortable: true),
                CrmPipelineDatasetModel.CatalogLookupId("contact_id", "Contact", CrmCodes.Contact),
                CrmPipelineDatasetModel.Text("contact_display", "Contact", selectable: true, groupable: true, sortable: true),
                CrmPipelineDatasetModel.Text("outcome", "Outcome", selectable: true, groupable: true, sortable: true, filterable: true)
            ],
            Measures:
            [
                CrmPipelineDatasetModel.Count("activity_count", "Activities")
            ]);
}

public static class CrmQuoteRegisterDatasetModel
{
    public static ReportDatasetDto Create()
        => new(
            DatasetCode: CrmCodes.QuoteRegisterReport,
            Fields:
            [
                CrmPipelineDatasetModel.Date("quote_date", "Quote Date", selectable: true, groupable: true, sortable: true, filterable: true),
                CrmPipelineDatasetModel.Text("quote_status", "Quote Status", selectable: true, groupable: true, sortable: true, filterable: true),
                CrmPipelineDatasetModel.CatalogLookupId("customer_id", "Account", CrmCodes.Account),
                CrmPipelineDatasetModel.Text("customer_display", "Account", selectable: true, groupable: true, sortable: true),
                CrmPipelineDatasetModel.CatalogLookupId("contact_id", "Contact", CrmCodes.Contact),
                CrmPipelineDatasetModel.Text("contact_display", "Contact", selectable: true, groupable: true, sortable: true),
                CrmPipelineDatasetModel.Text("currency", "Currency", selectable: true, groupable: true, sortable: true, filterable: true)
            ],
            Measures:
            [
                CrmPipelineDatasetModel.Decimal("amount", "Amount"),
                CrmPipelineDatasetModel.Count("quote_count", "Quotes")
            ]);
}

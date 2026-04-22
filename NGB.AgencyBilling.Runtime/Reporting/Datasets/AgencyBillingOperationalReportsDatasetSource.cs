using NGB.Application.Abstractions.Services;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;
using static NGB.AgencyBilling.Runtime.Reporting.Datasets.AgencyBillingReportDatasetModelCommon;

namespace NGB.AgencyBilling.Runtime.Reporting.Datasets;

public sealed class AgencyBillingOperationalReportsDatasetSource : IReportDatasetSource
{
    public IReadOnlyList<ReportDatasetDto> GetDatasets()
        =>
        [
            AgencyBillingUnbilledTimeDatasetModel.Create(),
            AgencyBillingProjectProfitabilityDatasetModel.Create(),
            AgencyBillingInvoiceRegisterDatasetModel.Create(),
            AgencyBillingArAgingDatasetModel.Create(),
            AgencyBillingTeamUtilizationDatasetModel.Create()
        ];
}

public static class AgencyBillingUnbilledTimeDatasetModel
{
    public const string DatasetCode = AgencyBillingCodes.UnbilledTimeReport;

    public static ReportDatasetDto Create()
        => new(
            DatasetCode,
            Fields:
            [
                DateField("work_date", "Work Date"),
                LookupField("client_id", "Client", AgencyBillingCodes.Client),
                DisplayField("client_display", "Client"),
                LookupField("project_id", "Project", AgencyBillingCodes.Project),
                DisplayField("project_display", "Project"),
                LookupField("team_member_id", "Team Member", AgencyBillingCodes.TeamMember),
                DisplayField("team_member_display", "Team Member"),
                DocumentField("timesheet_id", "Timesheet", AgencyBillingCodes.Timesheet),
                DetailField("timesheet_display", "Timesheet")
            ],
            Measures:
            [
                AmountMeasure("hours_open", "Hours Open"),
                AmountMeasure("amount_open", "Amount Open")
            ]);
}

public static class AgencyBillingProjectProfitabilityDatasetModel
{
    public const string DatasetCode = AgencyBillingCodes.ProjectProfitabilityReport;

    public static ReportDatasetDto Create()
        => new(
            DatasetCode,
            Fields:
            [
                LookupField("client_id", "Client", AgencyBillingCodes.Client),
                DisplayField("client_display", "Client"),
                LookupField("project_id", "Project", AgencyBillingCodes.Project),
                DisplayField("project_display", "Project"),
                LookupField("project_manager_id", "Project Manager", AgencyBillingCodes.TeamMember),
                DisplayField("project_manager_display", "Project Manager"),
                AttributeField("project_status", "Project Status"),
                AttributeField("billing_model_display", "Billing Model")
            ],
            Measures:
            [
                AmountMeasure("total_hours", "Total Hours"),
                AmountMeasure("billable_hours", "Billable Hours"),
                AmountMeasure("delivery_value_amount", "Delivery Value"),
                AmountMeasure("cost_amount", "Cost Amount"),
                AmountMeasure("budget_hours", "Budget Hours"),
                AmountMeasure("budget_amount", "Budget Amount"),
                AmountMeasure("billed_amount", "Billed Amount"),
                AmountMeasure("collected_amount", "Collected Amount"),
                AmountMeasure("outstanding_ar_amount", "Outstanding AR"),
                AmountMeasure("gross_margin_amount", "Gross Margin")
            ]);
}

public static class AgencyBillingInvoiceRegisterDatasetModel
{
    public const string DatasetCode = AgencyBillingCodes.InvoiceRegisterReport;

    public static ReportDatasetDto Create()
        => new(
            DatasetCode,
            Fields:
            [
                DateField("invoice_date", "Invoice Date"),
                DateField("due_date", "Due Date"),
                LookupField("client_id", "Client", AgencyBillingCodes.Client),
                DisplayField("client_display", "Client"),
                LookupField("project_id", "Project", AgencyBillingCodes.Project),
                DisplayField("project_display", "Project"),
                DocumentField("contract_id", "Client Contract", AgencyBillingCodes.ClientContract),
                DetailField("contract_display", "Client Contract"),
                DocumentField("invoice_id", "Invoice", AgencyBillingCodes.SalesInvoice),
                DetailField("invoice_display", "Invoice"),
                AttributeField("currency_code", "Currency"),
                AttributeField("payment_status", "Payment Status")
            ],
            Measures:
            [
                AmountMeasure("invoice_amount", "Invoice Amount"),
                AmountMeasure("applied_amount", "Applied Amount"),
                AmountMeasure("balance_amount", "Balance Amount")
            ]);
}

public static class AgencyBillingArAgingDatasetModel
{
    public const string DatasetCode = AgencyBillingCodes.ArAgingReport;

    public static ReportDatasetDto Create()
        => new(
            DatasetCode,
            Fields:
            [
                DateField("invoice_date", "Invoice Date"),
                DateField("due_date", "Due Date"),
                LookupField("client_id", "Client", AgencyBillingCodes.Client),
                DisplayField("client_display", "Client"),
                LookupField("project_id", "Project", AgencyBillingCodes.Project),
                DisplayField("project_display", "Project"),
                DocumentField("invoice_id", "Invoice", AgencyBillingCodes.SalesInvoice),
                DetailField("invoice_display", "Invoice"),
                AttributeField("aging_bucket", "Aging Bucket"),
                new ReportFieldDto("days_past_due", "Days Past Due", "int32", ReportFieldKind.Attribute, IsGroupable: true, IsSortable: true, IsSelectable: true)
            ],
            Measures:
            [
                AmountMeasure("open_amount", "Open Amount"),
                AmountMeasure("current_amount", "Current"),
                AmountMeasure("bucket_1_30_amount", "1-30"),
                AmountMeasure("bucket_31_60_amount", "31-60"),
                AmountMeasure("bucket_61_90_amount", "61-90"),
                AmountMeasure("bucket_90_plus_amount", "90+")
            ]);
}

public static class AgencyBillingTeamUtilizationDatasetModel
{
    public const string DatasetCode = AgencyBillingCodes.TeamUtilizationReport;

    public static ReportDatasetDto Create()
        => new(
            DatasetCode,
            Fields:
            [
                DateField("work_date", "Work Date"),
                LookupField("team_member_id", "Team Member", AgencyBillingCodes.TeamMember),
                DisplayField("team_member_display", "Team Member"),
                LookupField("client_id", "Client", AgencyBillingCodes.Client),
                DisplayField("client_display", "Client"),
                LookupField("project_id", "Project", AgencyBillingCodes.Project),
                DisplayField("project_display", "Project"),
                LookupField("service_item_id", "Service Item", AgencyBillingCodes.ServiceItem),
                DisplayField("service_item_display", "Service Item"),
                DocumentField("timesheet_id", "Timesheet", AgencyBillingCodes.Timesheet),
                DetailField("timesheet_display", "Timesheet")
            ],
            Measures:
            [
                AmountMeasure("hours_total", "Total Hours"),
                AmountMeasure("billable_hours", "Billable Hours"),
                AmountMeasure("non_billable_hours", "Non-Billable Hours"),
                AmountMeasure("billable_amount", "Billable Amount"),
                AmountMeasure("cost_amount", "Cost Amount")
            ]);
}

internal static class AgencyBillingReportDatasetModelCommon
{
    private static readonly IReadOnlyList<ReportAggregationKind> DecimalAggregations =
    [
        ReportAggregationKind.Sum,
        ReportAggregationKind.Min,
        ReportAggregationKind.Max,
        ReportAggregationKind.Average
    ];

    private static readonly IReadOnlyList<ReportTimeGrain> TimeGrains =
    [
        ReportTimeGrain.Day,
        ReportTimeGrain.Week,
        ReportTimeGrain.Month,
        ReportTimeGrain.Quarter,
        ReportTimeGrain.Year
    ];

    public static ReportFieldDto DateField(string code, string label)
        => new(
            code,
            label,
            "datetime",
            ReportFieldKind.Time,
            IsGroupable: true,
            IsSortable: true,
            IsSelectable: true,
            SupportedTimeGrains: TimeGrains);

    public static ReportFieldDto LookupField(string code, string label, string catalogType)
        => new(
            code,
            label,
            "uuid",
            ReportFieldKind.Dimension,
            IsFilterable: true,
            Lookup: new CatalogLookupSourceDto(catalogType));

    public static ReportFieldDto DocumentField(string code, string label, params string[] documentTypes)
        => new(
            code,
            label,
            "uuid",
            ReportFieldKind.Dimension,
            IsFilterable: true,
            Lookup: new DocumentLookupSourceDto(documentTypes));

    public static ReportFieldDto DisplayField(string code, string label)
        => new(
            code,
            label,
            "string",
            ReportFieldKind.Dimension,
            IsGroupable: true,
            IsSortable: true,
            IsSelectable: true);

    public static ReportFieldDto DetailField(string code, string label)
        => new(
            code,
            label,
            "string",
            ReportFieldKind.Detail,
            IsGroupable: true,
            IsSortable: true,
            IsSelectable: true);

    public static ReportFieldDto AttributeField(string code, string label)
        => new(
            code,
            label,
            "string",
            ReportFieldKind.Attribute,
            IsGroupable: true,
            IsSortable: true,
            IsSelectable: true);

    public static ReportMeasureDto AmountMeasure(string code, string label)
        => new(code, label, "decimal", DecimalAggregations);
}

using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Reporting;
using NGB.PropertyManagement.Definitions;
using NGB.PropertyManagement.Reporting;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.Runtime.Reporting;
using NGB.Runtime.Reporting.Canonical;
using NGB.Runtime.Reporting.Internal;

namespace NGB.PropertyManagement.Runtime.Reporting;

public sealed class ReceivablesAgingCanonicalReportExecutor(
    IReceivablesReportReader reader,
    IPropertyManagementAccountingPolicyReader policyReader)
    : IReportSpecializedPlanExecutor
{
    public string ReportCode => PropertyManagementSecurityDefaults.ReceivablesAgingReport;

    public async Task<ReportDataPage> ExecuteAsync(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        CancellationToken ct)
    {
        var leaseId = CanonicalReportExecutionHelper.GetRequiredGuidFilter(definition, request, "lease_id");
        var asOf = CanonicalReportExecutionHelper.GetRequiredDateOnlyParameter(definition, request, "as_of_utc");

        var policy = await policyReader.GetRequiredAsync(ct);
        var cursorKind = SpecializedReportCursorCodec.BuildKind(
            ReportCode,
            policy.ReceivablesOpenItemsOperationalRegisterId.ToString("D"),
            leaseId.ToString("D"),
            asOf.ToString("yyyy-MM-dd"));
        var cursor = request.DisablePaging || string.IsNullOrWhiteSpace(request.Cursor)
            ? null
            : SpecializedReportCursorCodec.Decode<ReceivablesReportPageCursor>(cursorKind, request.Cursor);
        var offset = cursor?.Offset ?? Math.Max(0, request.Offset);
        var limit = request.DisablePaging
            ? PagingLimits.MaxMaterializedRows + 1
            : request.Limit <= 0 ? 50 : request.Limit;
        var page = cursor is not null
            ? await reader.GetCursorPageAsync(
                policy.ReceivablesOpenItemsOperationalRegisterId, leaseId,
                ReceivablesReportMode.Aging, cursor, limit, ct)
            : await reader.GetPageAsync(
                policy.ReceivablesOpenItemsOperationalRegisterId, leaseId,
                ReceivablesReportMode.Aging, offset, limit, ct);

        var rows = page.Rows.Select(row => ToDetailRow(ToRow(row, asOf))).ToList();
        if (request.Layout?.ShowGrandTotals != false && page.Total > 0)
            rows.Add(ToTotalRow(page.TotalOriginal, page.TotalOutstanding));

        var subtitle = string.Join(" · ", new[]
        {
            page.PartyDisplay,
            page.PropertyDisplay,
            page.LeaseDisplay,
            asOf.ToString("yyyy-MM-dd")
        }.Where(x => !string.IsNullOrWhiteSpace(x)));

        var sheet = new ReportSheetDto(
            Columns:
            [
                new ReportSheetColumnDto("bucket", "Bucket", "string", Width: 150, IsFrozen: true),
                new ReportSheetColumnDto("charge", "Charge", "string", Width: 220),
                new ReportSheetColumnDto("charge_type", "Charge Type", "string", Width: 150),
                new ReportSheetColumnDto("due_on_utc", "Due On", "date", Width: 120),
                new ReportSheetColumnDto("days_past_due", "Days Past Due", "int32", Width: 120),
                new ReportSheetColumnDto("original_amount", "Original", "decimal", Width: 120),
                new ReportSheetColumnDto("outstanding_amount", "Outstanding", "decimal", Width: 120)
            ],
            Rows: rows,
            Meta: new ReportSheetMetaDto(
                Title: definition.Name,
                Subtitle: subtitle,
                Diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["executor"] = "canonical-pm-receivables-aging"
                }));

        var hasMore = page.HasMore || offset + page.Rows.Count < page.Total;
        var nextCursor = !request.DisablePaging && hasMore
            ? SpecializedReportCursorCodec.Encode(
                cursorKind,
                new ReceivablesReportPageCursor(
                    offset + page.Rows.Count, page.Total, page.TotalOriginal,
                    page.TotalOutstanding, page.TotalCredit, page.PartyDisplay,
                    page.PropertyDisplay, page.LeaseDisplay))
            : null;

        return CanonicalReportExecutionHelper.CreatePrebuiltPage(
            sheet: sheet,
            offset: offset,
            limit: limit,
            total: page.Total,
            hasMore: hasMore,
            nextCursor: nextCursor,
            diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["executor"] = "canonical-pm-receivables-aging"
            });
    }

    private static AgingRow ToRow(ReceivablesReportRow charge, DateOnly asOf)
    {
        var dueOnUtc = charge.DueOnUtc.GetValueOrDefault();
        var daysPastDue = (asOf.ToDateTime(TimeOnly.MinValue) - dueOnUtc.ToDateTime(TimeOnly.MinValue)).Days;

        return new AgingRow(
            Bucket: BucketLabel(daysPastDue),
            ChargeDisplay: charge.Display,
            ChargeTypeDisplay: charge.ChargeTypeDisplay,
            DueOnUtc: dueOnUtc,
            DaysPastDue: daysPastDue,
            OriginalAmount: charge.OriginalAmount,
            OutstandingAmount: charge.OpenAmount,
            DocumentType: charge.DocumentType,
            DocumentId: charge.DocumentId);
    }

    private static string BucketLabel(int daysPastDue)
        => daysPastDue switch
        {
            <= 0 => "Current",
            <= 30 => "Past due 1–30 days",
            <= 60 => "Past due 31–60 days",
            <= 90 => "Past due 61–90 days",
            _ => "Past due 91+ days"
        };

    private static ReportSheetRowDto ToDetailRow(AgingRow row)
        => new(
            ReportRowKind.Detail,
            Cells:
            [
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(row.Bucket), row.Bucket, "string"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(row.ChargeDisplay ?? string.Empty), row.ChargeDisplay ?? string.Empty, "string", Action: string.IsNullOrWhiteSpace(row.DocumentType) ? null : ReportCellActions.BuildDocumentAction(row.DocumentType, row.DocumentId)),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(row.ChargeTypeDisplay ?? string.Empty), row.ChargeTypeDisplay ?? string.Empty, "string"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(row.DueOnUtc), row.DueOnUtc.ToString("yyyy-MM-dd"), "date"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(row.DaysPastDue), row.DaysPastDue.ToString(), "int32"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(row.OriginalAmount), row.OriginalAmount.ToString("0.##"), "decimal"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(row.OutstandingAmount), row.OutstandingAmount.ToString("0.##"), "decimal")
            ]);

    private static ReportSheetRowDto ToTotalRow(decimal totalOriginal, decimal totalOutstanding)
        => new(
            ReportRowKind.Total,
            Cells:
            [
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue("Total"), "Total", "string", SemanticRole: "label"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(string.Empty), string.Empty, "string"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(string.Empty), string.Empty, "string"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(string.Empty), string.Empty, "string"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(string.Empty), string.Empty, "string"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(totalOriginal), totalOriginal.ToString("0.##"), "decimal", SemanticRole: "total"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(totalOutstanding), totalOutstanding.ToString("0.##"), "decimal", SemanticRole: "total")
            ],
            SemanticRole: "grand_total");

    private sealed record AgingRow(
        string Bucket,
        string? ChargeDisplay,
        string? ChargeTypeDisplay,
        DateOnly DueOnUtc,
        int DaysPastDue,
        decimal OriginalAmount,
        decimal OutstandingAmount,
        string? DocumentType,
        Guid DocumentId);
}

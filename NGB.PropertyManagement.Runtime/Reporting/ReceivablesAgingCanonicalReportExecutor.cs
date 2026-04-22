using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.PropertyManagement.Contracts.Receivables;
using NGB.PropertyManagement.Runtime.Receivables;
using NGB.Runtime.Reporting.Canonical;
using NGB.Runtime.Reporting.Internal;

namespace NGB.PropertyManagement.Runtime.Reporting;

public sealed class ReceivablesAgingCanonicalReportExecutor(IReceivablesOpenItemsDetailsService details)
    : IReportSpecializedPlanExecutor
{
    public string ReportCode => "pm.receivables.aging";

    public async Task<ReportDataPage> ExecuteAsync(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        CancellationToken ct)
    {
        var leaseId = CanonicalReportExecutionHelper.GetRequiredGuidFilter(definition, request, "lease_id");
        var asOf = CanonicalReportExecutionHelper.GetRequiredDateOnlyParameter(definition, request, "as_of_utc");

        var open = await details.GetOpenItemsDetailsAsync(Guid.Empty, Guid.Empty, leaseId, ct: ct);
        var rowsAll = open.Charges
            .Select(charge => ToRow(charge, asOf))
            .ToArray();

        var total = rowsAll.Length;
        var offset = Math.Max(0, request.Offset);
        var limit = request.Limit <= 0 ? 50 : request.Limit;
        var slice = rowsAll.Skip(offset).Take(limit).ToArray();
        var hasMore = offset + slice.Length < total;

        var rows = slice.Select(ToDetailRow).ToList();
        if (request.Layout?.ShowGrandTotals != false && rowsAll.Length > 0)
            rows.Add(ToTotalRow(rowsAll));

        var subtitle = string.Join(" · ", new[]
        {
            open.PartyDisplay,
            open.PropertyDisplay,
            open.LeaseDisplay,
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

        return CanonicalReportExecutionHelper.CreatePrebuiltPage(
            sheet: sheet,
            offset: offset,
            limit: limit,
            total: total,
            hasMore: hasMore,
            nextCursor: null,
            diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["executor"] = "canonical-pm-receivables-aging"
            });
    }

    private static AgingRow ToRow(ReceivablesOpenChargeItemDetailsDto charge, DateOnly asOf)
    {
        var daysPastDue = (asOf.ToDateTime(TimeOnly.MinValue) - charge.DueOnUtc.ToDateTime(TimeOnly.MinValue)).Days;
        return new AgingRow(
            Bucket: BucketLabel(daysPastDue),
            ChargeDisplay: charge.ChargeDisplay,
            ChargeTypeDisplay: charge.ChargeTypeDisplay,
            DueOnUtc: charge.DueOnUtc,
            DaysPastDue: daysPastDue,
            OriginalAmount: charge.OriginalAmount,
            OutstandingAmount: charge.OutstandingAmount,
            DocumentType: charge.DocumentType,
            DocumentId: charge.ChargeDocumentId);
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

    private static ReportSheetRowDto ToTotalRow(IReadOnlyList<AgingRow> rows)
        => new(
            ReportRowKind.Total,
            Cells:
            [
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue("Total"), "Total", "string", SemanticRole: "label"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(string.Empty), string.Empty, "string"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(string.Empty), string.Empty, "string"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(string.Empty), string.Empty, "string"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(string.Empty), string.Empty, "string"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(rows.Sum(x => x.OriginalAmount)), rows.Sum(x => x.OriginalAmount).ToString("0.##"), "decimal", SemanticRole: "total"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(rows.Sum(x => x.OutstandingAmount)), rows.Sum(x => x.OutstandingAmount).ToString("0.##"), "decimal", SemanticRole: "total")
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

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

public sealed class ReceivablesOpenItemsDetailsCanonicalReportExecutor(
    IReceivablesReportReader reader,
    IPropertyManagementAccountingPolicyReader policyReader)
    : IReportSpecializedPlanExecutor
{
    public string ReportCode => PropertyManagementSecurityDefaults.ReceivablesOpenItemsDetailsReport;

    public async Task<ReportDataPage> ExecuteAsync(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        CancellationToken ct)
    {
        var leaseId = CanonicalReportExecutionHelper.GetRequiredGuidFilter(definition, request, "lease_id");

        var policy = await policyReader.GetRequiredAsync(ct);
        var cursorKind = SpecializedReportCursorCodec.BuildKind(
            ReportCode,
            policy.ReceivablesOpenItemsOperationalRegisterId.ToString("D"),
            leaseId.ToString("D"));
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
                ReceivablesReportMode.OpenItemsDetails, cursor, limit, ct)
            : await reader.GetPageAsync(
                policy.ReceivablesOpenItemsOperationalRegisterId, leaseId,
                ReceivablesReportMode.OpenItemsDetails, offset, limit, ct);

        var rows = page.Rows.Select(x => ToDetailRow(new OpenItemDetailsRow(
            Kind: x.IsCharge ? "Charge" : "Credit",
            ItemDisplay: x.Display,
            DueOnUtc: x.DueOnUtc,
            ReceivedOnUtc: x.ReceivedOnUtc,
            ChargeTypeDisplay: x.ChargeTypeDisplay,
            OriginalAmount: x.OriginalAmount,
            OutstandingAmount: x.IsCharge ? x.OpenAmount : null,
            AvailableCredit: x.IsCharge ? null : x.OpenAmount,
            DocumentType: x.DocumentType,
            DocumentId: x.DocumentId))).ToList();

        if (request.Layout?.ShowGrandTotals != false && page.Total > 0)
            rows.Add(ToTotalRow(page.TotalOutstanding, page.TotalCredit));

        var sheet = new ReportSheetDto(
            Columns:
            [
                new ReportSheetColumnDto("kind", "Kind", "string", Width: 100, IsFrozen: true),
                new ReportSheetColumnDto("item", "Document", "string", Width: 220),
                new ReportSheetColumnDto("due_on_utc", "Due On", "date", Width: 120),
                new ReportSheetColumnDto("received_on_utc", "Received On", "date", Width: 120),
                new ReportSheetColumnDto("charge_type", "Charge Type", "string", Width: 150),
                new ReportSheetColumnDto("original_amount", "Original", "decimal", Width: 120),
                new ReportSheetColumnDto("outstanding_amount", "Outstanding", "decimal", Width: 120),
                new ReportSheetColumnDto("available_credit", "Available Credit", "decimal", Width: 130)
            ],
            Rows: rows,
            Meta: new ReportSheetMetaDto(
                Title: definition.Name,
                Subtitle: $"Outstanding {page.TotalOutstanding:0.##} · Credit {page.TotalCredit:0.##}",
                Diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["executor"] = "canonical-pm-receivables-open-items-details"
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
                ["executor"] = "canonical-pm-receivables-open-items-details"
            });
    }

    private static ReportSheetRowDto ToDetailRow(OpenItemDetailsRow row)
        => new(
            ReportRowKind.Detail,
            Cells:
            [
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(row.Kind), row.Kind, "string"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(row.ItemDisplay ?? string.Empty), row.ItemDisplay ?? string.Empty, "string", Action: string.IsNullOrWhiteSpace(row.DocumentType) ? null : ReportCellActions.BuildDocumentAction(row.DocumentType, row.DocumentId)),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(row.DueOnUtc), row.DueOnUtc?.ToString("yyyy-MM-dd") ?? string.Empty, "date"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(row.ReceivedOnUtc), row.ReceivedOnUtc?.ToString("yyyy-MM-dd") ?? string.Empty, "date"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(row.ChargeTypeDisplay ?? string.Empty), row.ChargeTypeDisplay ?? string.Empty, "string"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(row.OriginalAmount), row.OriginalAmount.ToString("0.##"), "decimal"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(row.OutstandingAmount), row.OutstandingAmount?.ToString("0.##") ?? string.Empty, "decimal"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(row.AvailableCredit), row.AvailableCredit?.ToString("0.##") ?? string.Empty, "decimal")
            ]);

    private static ReportSheetRowDto ToTotalRow(decimal totalOutstanding, decimal totalCredit)
        => new(
            ReportRowKind.Total,
            Cells:
            [
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue("Total"), "Total", "string", SemanticRole: "label"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(string.Empty), string.Empty, "string"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(string.Empty), string.Empty, "string"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(string.Empty), string.Empty, "string"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(string.Empty), string.Empty, "string"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(string.Empty), string.Empty, "string"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(totalOutstanding), totalOutstanding.ToString("0.##"), "decimal", SemanticRole: "total"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(totalCredit), totalCredit.ToString("0.##"), "decimal", SemanticRole: "total")
            ],
            SemanticRole: "grand_total");

    private sealed record OpenItemDetailsRow(
        string Kind,
        string? ItemDisplay,
        DateOnly? DueOnUtc,
        DateOnly? ReceivedOnUtc,
        string? ChargeTypeDisplay,
        decimal OriginalAmount,
        decimal? OutstandingAmount,
        decimal? AvailableCredit,
        string? DocumentType,
        Guid DocumentId);
}

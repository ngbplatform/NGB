using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.PropertyManagement.Definitions;
using NGB.PropertyManagement.Runtime.Receivables;
using NGB.Runtime.Reporting.Canonical;
using NGB.Runtime.Reporting.Internal;

namespace NGB.PropertyManagement.Runtime.Reporting;

public sealed class ReceivablesOpenItemsCanonicalReportExecutor(IReceivablesOpenItemsService openItems)
    : IReportSpecializedPlanExecutor
{
    public string ReportCode => PropertyManagementSecurityDefaults.ReceivablesOpenItemsReport;

    public async Task<ReportDataPage> ExecuteAsync(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        CancellationToken ct)
    {
        var leaseId = CanonicalReportExecutionHelper.GetRequiredGuidFilter(definition, request, "lease_id");

        var offset = Math.Max(0, request.Offset);
        var limit = request.Limit <= 0 ? 50 : request.Limit;
        var open = await openItems.GetOpenItemsPageAsync(Guid.Empty, Guid.Empty, leaseId, offset, limit, ct);

        var rows = open.Rows.Select(x => ToDetailRow(new OpenItemRow(
            x.IsCharge ? "Charge" : "Credit",
            x.ItemDisplay,
            x.IsCharge ? x.Amount : null,
            x.IsCharge ? null : x.Amount,
            x.DocumentType,
            x.ItemId))).ToList();

        if (request.Layout?.ShowGrandTotals != false && open.Total > 0)
            rows.Add(ToTotalRow(open.TotalOutstanding, open.TotalCredit));

        var sheet = new ReportSheetDto(
            Columns:
            [
                new ReportSheetColumnDto("kind", "Kind", "string", Width: 100, IsFrozen: true),
                new ReportSheetColumnDto("item", "Document", "string", Width: 220),
                new ReportSheetColumnDto("outstanding_amount", "Outstanding", "decimal", Width: 120),
                new ReportSheetColumnDto("available_credit", "Available Credit", "decimal", Width: 130)
            ],
            Rows: rows,
            Meta: new ReportSheetMetaDto(
                Title: definition.Name,
                Subtitle: $"Outstanding {open.TotalOutstanding:0.##} · Credit {open.TotalCredit:0.##}",
                Diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["executor"] = "canonical-pm-receivables-open-items"
                }));

        return CanonicalReportExecutionHelper.CreatePrebuiltPage(
            sheet: sheet,
            offset: offset,
            limit: limit,
            total: open.Total,
            hasMore: offset + open.Rows.Count < open.Total,
            nextCursor: null,
            diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["executor"] = "canonical-pm-receivables-open-items"
            });
    }

    private static ReportSheetRowDto ToDetailRow(OpenItemRow row)
        => new(
            ReportRowKind.Detail,
            Cells:
            [
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(row.Kind), row.Kind, "string"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(row.ItemDisplay ?? string.Empty), row.ItemDisplay ?? string.Empty, "string", Action: string.IsNullOrWhiteSpace(row.DocumentType) ? null : ReportCellActions.BuildDocumentAction(row.DocumentType, row.DocumentId)),
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
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(totalOutstanding), totalOutstanding.ToString("0.##"), "decimal", SemanticRole: "total"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(totalCredit), totalCredit.ToString("0.##"), "decimal", SemanticRole: "total")
            ],
            SemanticRole: "grand_total");

    private sealed record OpenItemRow(
        string Kind,
        string? ItemDisplay,
        decimal? OutstandingAmount,
        decimal? AvailableCredit,
        string? DocumentType,
        Guid DocumentId);
}

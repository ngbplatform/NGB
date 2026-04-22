using System.Text.Json;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Reporting;
using NGB.Core.Catalogs.Exceptions;
using NGB.Core.Documents.Exceptions;
using NGB.Core.Reporting.Exceptions;
using NGB.PropertyManagement.Reporting;
using NGB.Runtime.Reporting.Canonical;
using NGB.Runtime.Reporting.Internal;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PropertyManagement.Runtime.Reporting;

public sealed class TenantStatementCanonicalReportExecutor(
    ITenantStatementReader reader,
    IDocumentService documents,
    ICatalogService catalogs)
    : IReportSpecializedPlanExecutor
{
    public string ReportCode => PropertyManagementCodes.TenantStatement;

    public async Task<ReportDataPage> ExecuteAsync(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        CancellationToken ct)
    {
        var leaseId = CanonicalReportExecutionHelper.GetRequiredGuidFilter(definition, request, "lease_id");
        var fromUtc = CanonicalReportExecutionHelper.GetOptionalDateOnlyParameter(definition, request, "from_utc");
        var toUtc = CanonicalReportExecutionHelper.GetOptionalDateOnlyParameter(definition, request, "to_utc")
                    ?? DateOnly.FromDateTime(DateTime.UtcNow);

        if (fromUtc is not null && fromUtc.Value > toUtc)
            throw Invalid(
                definition,
                "parameters.to_utc",
                $"{CanonicalReportExecutionHelper.GetParameterLabel(definition, "to_utc")} must be on or after {CanonicalReportExecutionHelper.GetParameterLabel(definition, "from_utc") }.");

        var query = new TenantStatementQuery(
            LeaseId: leaseId,
            FromUtc: fromUtc,
            ToUtc: toUtc,
            Offset: Math.Max(0, request.Offset),
            Limit: request.Limit <= 0 ? 100 : request.Limit);
        query.EnsureInvariant();

        var page = await reader.GetPageAsync(query, ct);
        page.EnsureInvariant();

        var subtitle = await BuildSubtitleAsync(leaseId, fromUtc, toUtc, ct);

        var rows = new List<ReportSheetRowDto>();
        if (fromUtc is not null && query.Offset == 0)
            rows.Add(ToOpeningBalanceRow(page.Totals.OpeningBalance));

        rows.AddRange(page.Rows.Select(ToDetailRow));

        if (request.Layout?.ShowGrandTotals != false)
            rows.Add(ToClosingBalanceRow(page.Totals));

        var sheet = new ReportSheetDto(
            Columns:
            [
                new ReportSheetColumnDto("occurred_on_utc", "Date", "date", Width: 120, IsFrozen: true),
                new ReportSheetColumnDto("document", "Document", "string", Width: 220, IsFrozen: true),
                new ReportSheetColumnDto("entry_type", "Type", "string", Width: 140),
                new ReportSheetColumnDto("description", "Description", "string", Width: 220),
                new ReportSheetColumnDto("charge_amount", "Charge", "decimal", Width: 120),
                new ReportSheetColumnDto("credit_amount", "Credit", "decimal", Width: 120),
                new ReportSheetColumnDto("running_balance", "Balance", "decimal", Width: 120)
            ],
            Rows: rows,
            Meta: new ReportSheetMetaDto(
                Title: definition.Name,
                Subtitle: subtitle,
                Diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["executor"] = "canonical-pm-tenant-statement"
                }));

        return CanonicalReportExecutionHelper.CreatePrebuiltPage(
            sheet: sheet,
            offset: query.Offset,
            limit: query.Limit,
            total: page.Total,
            hasMore: query.Offset + page.Rows.Count < page.Total,
            nextCursor: null,
            diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["executor"] = "canonical-pm-tenant-statement"
            });
    }

    private async Task<string> BuildSubtitleAsync(Guid leaseId, DateOnly? fromUtc, DateOnly toUtc, CancellationToken ct)
    {
        string? leaseDisplay = null;
        string? partyDisplay = null;
        string? propertyDisplay = null;

        try
        {
            var lease = await documents.GetByIdAsync(PropertyManagementCodes.Lease, leaseId, ct);
            leaseDisplay = lease.Display;
            var partyId = ReadPrimaryPartyIdRequired(lease.Payload);
            var propertyId = ReadGuidRequired(lease.Payload, "property_id");

            try
            {
                partyDisplay = (await catalogs.GetByIdAsync(PropertyManagementCodes.Party, partyId, ct)).Display;
            }
            catch (CatalogNotFoundException)
            {
                // keep null
            }

            try
            {
                propertyDisplay = (await catalogs.GetByIdAsync(PropertyManagementCodes.Property, propertyId, ct)).Display;
            }
            catch (CatalogNotFoundException)
            {
                // keep null
            }
        }
        catch (DocumentNotFoundException)
        {
            leaseDisplay = null;
        }

        var rangeText = fromUtc is null
            ? $"Through {toUtc:yyyy-MM-dd}"
            : $"{fromUtc:yyyy-MM-dd} – {toUtc:yyyy-MM-dd}";

        return string.Join(" · ", new[]
        {
            partyDisplay,
            propertyDisplay,
            leaseDisplay,
            rangeText
        }.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private static ReportSheetRowDto ToOpeningBalanceRow(decimal openingBalance)
        => new(
            ReportRowKind.Detail,
            Cells:
            [
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue<string?>(null), null, "date"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue("Opening balance"), "Opening balance", "string", SemanticRole: "label"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(string.Empty), string.Empty, "string"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(string.Empty), string.Empty, "string"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue<string?>(null), null, "decimal"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue<string?>(null), null, "decimal"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(openingBalance), openingBalance.ToString("0.##"), "decimal", SemanticRole: "total")
            ],
            SemanticRole: "opening_balance");

    private static ReportSheetRowDto ToDetailRow(TenantStatementRow row)
        => new(
            ReportRowKind.Detail,
            Cells:
            [
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(row.OccurredOnUtc), row.OccurredOnUtc.ToString("yyyy-MM-dd"), "date"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(row.DocumentDisplay), row.DocumentDisplay, "string", Action: ReportCellActions.BuildDocumentAction(row.DocumentType, row.DocumentId)),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(row.EntryTypeDisplay), row.EntryTypeDisplay, "string"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(row.Description ?? string.Empty), row.Description ?? string.Empty, "string"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(row.ChargeAmount == 0m ? (decimal?)null : row.ChargeAmount), row.ChargeAmount == 0m ? null : row.ChargeAmount.ToString("0.##"), "decimal"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(row.CreditAmount == 0m ? (decimal?)null : row.CreditAmount), row.CreditAmount == 0m ? null : row.CreditAmount.ToString("0.##"), "decimal"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(row.RunningBalance), row.RunningBalance.ToString("0.##"), "decimal")
            ]);

    private static ReportSheetRowDto ToClosingBalanceRow(TenantStatementTotals totals)
        => new(
            ReportRowKind.Total,
            Cells:
            [
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue<string?>(null), null, "date"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue("Closing balance"), "Closing balance", "string", SemanticRole: "label"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(string.Empty), string.Empty, "string"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(string.Empty), string.Empty, "string"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(totals.TotalCharges), totals.TotalCharges.ToString("0.##"), "decimal", SemanticRole: "total"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(totals.TotalCredits), totals.TotalCredits.ToString("0.##"), "decimal", SemanticRole: "total"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(totals.ClosingBalance), totals.ClosingBalance.ToString("0.##"), "decimal", SemanticRole: "total")
            ],
            SemanticRole: "grand_total");

    private static Guid ReadPrimaryPartyIdRequired(RecordPayload payload)
    {
        if (payload.Parts is null || !payload.Parts.TryGetValue("parties", out var parties))
            throw new NgbConfigurationViolationException("Lease payload must include parties.");

        var primary = parties.Rows.SingleOrDefault(r =>
            r.TryGetValue("is_primary", out var p) && p.ValueKind == JsonValueKind.True);

        if (primary is null || !primary.TryGetValue("party_id", out var idEl))
            throw new NgbConfigurationViolationException("Lease payload must include a primary party.");

        return idEl.ParseGuidOrRef();
    }

    private static Guid ReadGuidRequired(RecordPayload payload, string field)
    {
        if (payload.Fields is null || !payload.Fields.TryGetValue(field, out var el))
            throw new NgbConfigurationViolationException($"Lease payload must include '{field}'.");

        return el.ParseGuidOrRef();
    }

    private static ReportLayoutValidationException Invalid(
        ReportDefinitionDto definition,
        string fieldPath,
        string message)
        => new(
            message,
            fieldPath,
            errors: new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [fieldPath] = [message]
            },
            context: new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["reportCode"] = definition.ReportCode
            });
}

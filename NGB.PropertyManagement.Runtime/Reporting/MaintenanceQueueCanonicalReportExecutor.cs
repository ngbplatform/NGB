using System.Text.Json;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Reporting;
using NGB.Core.Reporting.Exceptions;
using NGB.PropertyManagement.Reporting;
using NGB.Runtime.Reporting;
using NGB.Runtime.Reporting.Canonical;
using NGB.Runtime.Reporting.Internal;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PropertyManagement.Runtime.Reporting;

public sealed class MaintenanceQueueCanonicalReportExecutor(IMaintenanceQueueReader reader)
    : IReportSpecializedPlanExecutor
{
    public string ReportCode => PropertyManagementCodes.MaintenanceQueue;

    public ReportExecutionRequestDto PrepareExecution(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        DateTimeOffset utcNow)
    {
        var parameters = new Dictionary<string, string>(request.Parameters ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        var date = CanonicalReportExecutionHelper.GetOptionalDateOnlyParameter(definition, request, "as_of_utc")
            ?? DateOnly.FromDateTime(utcNow.UtcDateTime);
        parameters["as_of_utc"] = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        return request with { Parameters = parameters };
    }


    public async Task<ReportDataPage> ExecuteAsync(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        CancellationToken ct)
    {
        var query = CreateQuery(definition, request);

        var cursorKind = BuildCursorKind(query);
        var cursor = request.DisablePaging || string.IsNullOrWhiteSpace(request.Cursor)
            ? null
            : SpecializedReportCursorCodec.Decode<MaintenanceQueuePageCursor>(cursorKind, request.Cursor);
        query = query with
        {
            Offset = cursor?.Offset ?? query.Offset,
            Limit = request.DisablePaging ? PagingLimits.MaxMaterializedRows + 1 : query.Limit
        };
        var page = cursor is not null
            ? await reader.GetCursorPageAsync(query, cursor, ct)
            : await reader.GetPageAsync(query, ct);
        page.EnsureInvariant();

        var sheet = CreateSheet(definition, query.AsOfUtc, page.Rows);

        var hasMore = page.HasMore || cursor is null && query.Offset + page.Rows.Count < page.Total;
        var nextCursor = !request.DisablePaging && hasMore
            ? SpecializedReportCursorCodec.Encode(
                cursorKind,
                new MaintenanceQueuePageCursor(
                    query.Offset + page.Rows.Count,
                    page.Total,
                    page.NextAfterRequestedAtUtc,
                    page.NextAfterRequestId,
                    page.NextAfterWorkOrderId))
            : null;

        return CanonicalReportExecutionHelper.CreatePrebuiltPage(
            sheet: sheet,
            offset: query.Offset,
            limit: query.Limit,
            total: page.Total,
            hasMore: hasMore,
            nextCursor: nextCursor,
            diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["executor"] = "canonical-pm-maintenance-queue"
            });
    }

    internal static MaintenanceQueueQuery CreateQuery(ReportDefinitionDto definition, ReportExecutionRequestDto request)
    {
        var query = new MaintenanceQueueQuery(
            AsOfUtc: CanonicalReportExecutionHelper.GetOptionalDateOnlyParameter(definition, request, "as_of_utc")
                     ?? DateOnly.FromDateTime(DateTime.UtcNow),
            BuildingId: CanonicalReportExecutionHelper.GetOptionalGuidFilter(definition, request, "building_id"),
            PropertyId: CanonicalReportExecutionHelper.GetOptionalGuidFilter(definition, request, "property_id"),
            CategoryId: CanonicalReportExecutionHelper.GetOptionalGuidFilter(definition, request, "category_id"),
            AssignedPartyId: CanonicalReportExecutionHelper.GetOptionalGuidFilter(definition, request, "assigned_party_id"),
            Priority: GetOptionalPriorityFilter(definition, request, "priority"),
            QueueState: GetOptionalQueueStateFilter(definition, request, "queue_state"),
            Offset: Math.Max(0, request.Offset),
            Limit: request.Limit <= 0 ? 100 : request.Limit,
            IncludeTotal: false);

        query.EnsureInvariant();

        return query;
    }

    internal static ReportSheetDto CreateSheet(ReportDefinitionDto definition, DateOnly asOf, IReadOnlyList<MaintenanceQueueRow> rows)
    {
        var sheet = new ReportSheetDto(
            Columns:
            [
                new ReportSheetColumnDto("queue_state", "Queue State", "string", Width: 130, IsFrozen: true),
                new ReportSheetColumnDto("request", "Request", "string", Width: 170, IsFrozen: true),
                new ReportSheetColumnDto("subject", "Subject", "string", Width: 220),
                new ReportSheetColumnDto("requested_at_utc", "Requested At", "date", Width: 120),
                new ReportSheetColumnDto("aging_days", "Aging Days", "int32", Width: 110),
                new ReportSheetColumnDto("building", "Building", "string", Width: 220),
                new ReportSheetColumnDto("property", "Property", "string", Width: 220),
                new ReportSheetColumnDto("category", "Category", "string", Width: 160),
                new ReportSheetColumnDto("priority", "Priority", "string", Width: 110),
                new ReportSheetColumnDto("requested_by", "Requested By", "string", Width: 180),
                new ReportSheetColumnDto("work_order", "Work Order", "string", Width: 170),
                new ReportSheetColumnDto("assigned_to", "Assigned To", "string", Width: 180),
                new ReportSheetColumnDto("due_by_utc", "Due By", "date", Width: 120)
            ],
            Rows: rows.Select(ToRow).ToArray(),
            Meta: new ReportSheetMetaDto(
                Title: definition.Name,
                Subtitle: $"As of {asOf:yyyy-MM-dd}",
                Diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["executor"] = "canonical-pm-maintenance-queue"
                }));

        return sheet;
    }

    private string BuildCursorKind(MaintenanceQueueQuery query)
        => SpecializedReportCursorCodec.BuildKind(
            ReportCode,
            query.AsOfUtc.ToString("yyyy-MM-dd"),
            query.BuildingId?.ToString("D"),
            query.PropertyId?.ToString("D"),
            query.CategoryId?.ToString("D"),
            query.AssignedPartyId?.ToString("D"),
            query.Priority,
            query.QueueState?.ToString());

    private static ReportSheetRowDto ToRow(MaintenanceQueueRow row)
        => new(
            ReportRowKind.Detail,
            Cells:
            [
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(row.QueueState.ToCode()), row.QueueState.ToDisplay(), "string"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(row.RequestDisplay), row.RequestDisplay, "string", Action: ReportCellActions.BuildDocumentAction(PropertyManagementCodes.MaintenanceRequest, row.RequestId)),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(row.Subject), row.Subject, "string"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(row.RequestedAtUtc), row.RequestedAtUtc.ToString("yyyy-MM-dd"), "date"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(row.AgingDays), row.AgingDays.ToString(), "int32"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(row.BuildingDisplay), row.BuildingDisplay, "string", Action: ReportCellActions.BuildCatalogAction(PropertyManagementCodes.Property, row.BuildingId)),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(row.PropertyDisplay), row.PropertyDisplay, "string", Action: ReportCellActions.BuildCatalogAction(PropertyManagementCodes.Property, row.PropertyId)),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(row.CategoryDisplay), row.CategoryDisplay, "string", Action: ReportCellActions.BuildCatalogAction(PropertyManagementCodes.MaintenanceCategory, row.CategoryId)),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(row.Priority), row.Priority, "string"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(row.RequestedByDisplay), row.RequestedByDisplay, "string", Action: ReportCellActions.BuildCatalogAction(PropertyManagementCodes.Party, row.RequestedByPartyId)),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(row.WorkOrderDisplay), row.WorkOrderDisplay, "string", Action: row.WorkOrderId is null ? null : ReportCellActions.BuildDocumentAction(PropertyManagementCodes.WorkOrder, row.WorkOrderId.Value)),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(row.AssignedPartyDisplay), row.AssignedPartyDisplay, "string", Action: row.AssignedPartyId is null ? null : ReportCellActions.BuildCatalogAction(PropertyManagementCodes.Party, row.AssignedPartyId.Value)),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(row.DueByUtc), row.DueByUtc?.ToString("yyyy-MM-dd"), "date")
            ]);

    private static string? GetOptionalTextFilter(ReportExecutionRequestDto request, string filterCode)
    {
        if (request.Filters is null)
            return null;

        foreach (var pair in request.Filters)
        {
            if (!string.Equals(pair.Key, filterCode, StringComparison.OrdinalIgnoreCase))
                continue;

            var value = pair.Value.Value;
            return value.ValueKind switch
            {
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetString()) ? null : value.GetString()!.Trim(),
                _ => throw new NgbArgumentInvalidException(filterCode, $"Filter '{filterCode}' must be a string.")
            };
        }

        return null;
    }

    private static string? GetOptionalPriorityFilter(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        string filterCode)
    {
        try
        {
            var raw = GetOptionalTextFilter(request, filterCode);
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            return raw.Trim().ToUpperInvariant() switch
            {
                "EMERGENCY" => "Emergency",
                "HIGH" => "High",
                "NORMAL" => "Normal",
                "LOW" => "Low",
                _ => throw Invalid(definition, $"filters.{filterCode}", $"Select a valid {CanonicalReportExecutionHelper.GetFilterLabel(definition, filterCode)}.")
            };
        }
        catch (NgbArgumentInvalidException)
        {
            throw Invalid(definition, $"filters.{filterCode}", $"Select a valid {CanonicalReportExecutionHelper.GetFilterLabel(definition, filterCode)}.");
        }
    }

    private static MaintenanceQueueState? GetOptionalQueueStateFilter(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        string filterCode)
    {
        try
        {
            var raw = GetOptionalTextFilter(request, filterCode);
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            if (MaintenanceQueueStateExtensions.TryParse(raw, out var state))
                return state;

            throw Invalid(definition, $"filters.{filterCode}", $"Select a valid {CanonicalReportExecutionHelper.GetFilterLabel(definition, filterCode)}.");
        }
        catch (NgbArgumentInvalidException)
        {
            throw Invalid(definition, $"filters.{filterCode}", $"Select a valid {CanonicalReportExecutionHelper.GetFilterLabel(definition, filterCode)}.");
        }
    }

    private static ReportLayoutValidationException Invalid(ReportDefinitionDto definition, string fieldPath, string message)
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

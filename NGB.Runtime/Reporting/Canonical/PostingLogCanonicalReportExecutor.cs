using System.Globalization;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Core.Reporting.Exceptions;
using NGB.Persistence.Documents;
using NGB.Runtime.Reporting.Internal;

namespace NGB.Runtime.Reporting.Canonical;

public sealed class PostingLogCanonicalReportExecutor(
    IPostingStateReportReader reader,
    IDocumentDisplayReader documentDisplayReader)
    : IReportSpecializedPlanExecutor
{
    public string ReportCode => "accounting.posting_log";

    public async Task<ReportDataPage> ExecuteAsync(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        CancellationToken ct)
    {
        var fromUtc = GetOptionalUtcParameter(definition, request, "from_utc");
        var toUtc = GetOptionalUtcParameter(definition, request, "to_utc");
        if (fromUtc.HasValue && toUtc.HasValue && toUtc.Value < fromUtc.Value)
        {
            throw Invalid(
                definition,
                "parameters.to_utc",
                $"{CanonicalReportExecutionHelper.GetParameterLabel(definition, "to_utc")} must be on or after {CanonicalReportExecutionHelper.GetParameterLabel(definition, "from_utc")}.");
        }

        var page = await reader.GetPageAsync(
            new PostingStatePageRequest
            {
                PageSize = request.Limit,
                Cursor = request.DisablePaging || string.IsNullOrWhiteSpace(request.Cursor) ? null : PostingLogCursorCodec.Decode(request.Cursor),
                DocumentId = CanonicalReportExecutionHelper.GetOptionalGuidFilter(definition, request, "document_id"),
                Operation = ParseOptionalEnum<PostingOperation>(definition, request, "operation"),
                Status = ParseOptionalEnum<PostingStateStatus>(definition, request, "status"),
                FromUtc = fromUtc ?? default,
                ToUtc = toUtc ?? default,
                DisablePaging = request.DisablePaging
            },
            ct);

        var documentRefs = await documentDisplayReader.ResolveRefsAsync(
            page.Records.Select(x => x.DocumentId).Distinct().ToArray(),
            ct);

        var rows = page.Records.Select(record => ToDetailRow(record, documentRefs)).ToList();

        var sheet = new ReportSheetDto(
            Columns:
            [
                new ReportSheetColumnDto("started_at_utc", "Started", "date_time_utc", Width: 170, IsFrozen: true),
                new ReportSheetColumnDto("document", "Document", "string", Width: 220),
                new ReportSheetColumnDto("operation", "Operation", "string", Width: 130),
                new ReportSheetColumnDto("status", "Status", "string", Width: 120),
                new ReportSheetColumnDto("completed_at_utc", "Completed", "date_time_utc", Width: 170),
                new ReportSheetColumnDto("duration_ms", "Duration (ms)", "int32", Width: 120),
                new ReportSheetColumnDto("age_seconds", "Age (s)", "int32", Width: 100)
            ],
            Rows: rows,
            Meta: new ReportSheetMetaDto(
                Title: definition.Name,
                Subtitle: BuildSubtitle(fromUtc, toUtc),
                Diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["executor"] = "canonical-posting-log"
                }));

        return CanonicalReportExecutionHelper.CreatePrebuiltPage(
            sheet: sheet,
            offset: 0,
            limit: request.DisablePaging ? rows.Count : request.Limit,
            total: null,
            hasMore: page.HasMore,
            nextCursor: page.NextCursor is null ? null : PostingLogCursorCodec.Encode(page.NextCursor),
            diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["executor"] = "canonical-posting-log"
            });
    }

    private static ReportSheetRowDto ToDetailRow(
        PostingStateRecord record,
        IReadOnlyDictionary<Guid, DocumentDisplayRef> documentRefs)
    {
        var documentRef = documentRefs.TryGetValue(record.DocumentId, out var doc)
            ? doc
            : new DocumentDisplayRef(record.DocumentId, string.Empty, ReportDisplayHelpers.ShortGuid(record.DocumentId));
        var documentDisplay = documentRef.Display;
        var documentAction = string.IsNullOrWhiteSpace(documentRef.TypeCode)
            ? null
            : ReportCellActions.BuildDocumentAction(documentRef.TypeCode, record.DocumentId);

        var durationMs = record.Duration is null
            ? (int?)null
            : (int)Math.Round(record.Duration.Value.TotalMilliseconds);
        var ageSeconds = (int)Math.Round(record.Age.TotalSeconds);

        return new ReportSheetRowDto(
            ReportRowKind.Detail,
            Cells:
            [
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(record.StartedAtUtc), record.StartedAtUtc.ToString("yyyy-MM-dd HH:mm:ss'Z'"), "date_time_utc"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(documentDisplay), documentDisplay, "string", Action: documentAction),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(record.Operation.ToString()), record.Operation.ToString(), "string"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(record.Status.ToString()), record.Status.ToString(), "string"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(record.CompletedAtUtc), record.CompletedAtUtc?.ToString("yyyy-MM-dd HH:mm:ss'Z'") ?? string.Empty, "date_time_utc"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(durationMs), durationMs?.ToString(CultureInfo.InvariantCulture) ?? string.Empty, "int32"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(ageSeconds), ageSeconds.ToString(CultureInfo.InvariantCulture), "int32")
            ]);
    }

    private static string? BuildSubtitle(DateTime? fromUtc, DateTime? toUtc)
    {
        if (fromUtc is null && toUtc is null)
            return null;

        var from = fromUtc?.ToString("yyyy-MM-dd HH:mm:ss'Z'") ?? "…";
        var to = toUtc?.ToString("yyyy-MM-dd HH:mm:ss'Z'") ?? "…";
        return $"{from} → {to}";
    }

    private static TEnum? ParseOptionalEnum<TEnum>(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        string filterCode)
        where TEnum : struct, Enum
    {
        if (request.Filters is null)
            return null;

        foreach (var pair in request.Filters)
        {
            if (!string.Equals(pair.Key, filterCode, StringComparison.OrdinalIgnoreCase))
                continue;

            var value = pair.Value.Value;
            if (value.ValueKind is System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined)
                return null;

            if (value.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var raw = value.GetString();
                if (string.IsNullOrWhiteSpace(raw))
                    return null;

                if (Enum.TryParse<TEnum>(raw.Trim(), true, out var parsedByName))
                    return parsedByName;

                if (int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedInt)
                    && Enum.IsDefined(typeof(TEnum), parsedInt))
                {
                    return (TEnum)Enum.ToObject(typeof(TEnum), parsedInt);
                }
            }
            else if (value.ValueKind == System.Text.Json.JsonValueKind.Number && value.TryGetInt32(out var number) && Enum.IsDefined(typeof(TEnum), number))
            {
                return (TEnum)Enum.ToObject(typeof(TEnum), number);
            }

            throw Invalid(definition, $"filters.{filterCode}", BuildInvalidEnumMessage(definition, filterCode));
        }

        return null;
    }

    private static DateTime? GetOptionalUtcParameter(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        string parameterCode)
    {
        var parameters = request.Parameters;
        if (parameters is null || !parameters.TryGetValue(parameterCode, out var raw) || string.IsNullOrWhiteSpace(raw))
            return null;

        if (!DateTime.TryParse(raw.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
        {
            throw Invalid(definition, $"parameters.{parameterCode}", $"Enter a valid UTC date and time for {CanonicalReportExecutionHelper.GetParameterLabel(definition, parameterCode)}, for example 2026-03-01T00:00:00Z.");
        }

        return DateTime.SpecifyKind(parsed.ToUniversalTime(), DateTimeKind.Utc);
    }

    private static string BuildInvalidEnumMessage(ReportDefinitionDto definition, string filterCode)
    {
        var label = CanonicalReportExecutionHelper.GetFilterLabel(definition, filterCode);
        var options = definition.Filters?
            .FirstOrDefault(x => string.Equals(x.FieldCode, filterCode, StringComparison.OrdinalIgnoreCase))?
            .Options?
            .Select(x => x.Label)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        if (options is { Length: > 0 })
            return $"Select a valid {label}. Allowed values: {string.Join(", ", options)}.";

        return $"Select a valid {label}.";
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

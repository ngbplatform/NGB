using System.Security.Cryptography;
using System.Text.Json;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Persistence.Reporting;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Reporting;

/// <summary>Request lifetime and continuation validation around the report planner/renderer.</summary>
public sealed class ReportQueryService(
    ReportEngine engine,
    IReportDefinitionProvider definitions,
    ReportVariantRequestResolver variants,
    IReportReadSession session,
    ReportCursorProtector cursors)
    : IReportEngine
{
    public async Task<ReportExecutionResponseDto> ExecuteAsync(
        string reportCode,
        ReportExecutionRequestDto request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var definition = await definitions.GetDefinitionAsync(reportCode, ct);
        var resolved = await variants.ResolveAsync(reportCode, request, ct);
        var fingerprint = Fingerprint(definition, resolved);
        var inner = string.IsNullOrWhiteSpace(request.Cursor)
            ? null
            : Decode(request.Cursor, fingerprint);

        await session.BeginAsync(ct);

        try
        {
            var result = await engine.ExecuteAsync(
                reportCode,
                resolved with
                {
                    VariantCode = null,
                    Cursor = inner
                },
                ct);

            if (result.HasMore && string.IsNullOrWhiteSpace(result.NextCursor))
                throw new NgbInvariantViolationException("A report with more rows must provide a continuation cursor.");

            // Older canonical readers include their global footer on every page and carry its values
            // in the cursor. Browsing is live: show that footer once, refreshed in this page's read session.
            if (!result.HasMore
                && inner is not null
                && definition.Mode == ReportExecutionMode.Canonical
                && result.Diagnostics!.GetValueOrDefault("paging") != "query"
                && result.Diagnostics!.GetValueOrDefault("totals") != "current"
                && result.Sheet.Rows.Any(IsGrandTotal))
            {
                var totals = await engine.ExecuteAsync(
                    reportCode,
                    resolved with
                    {
                        VariantCode = null,
                        Cursor = null,
                        Offset = 0,
                        Limit = 1
                    },
                    ct);

                if (totals.Sheet.Rows.Any(IsGrandTotal))
                {
                    result = result with
                    {
                        Total = totals.Total,
                        Sheet = result.Sheet with
                        {
                            Rows = result.Sheet.Rows
                                .Where(r => !IsGrandTotal(r))
                                .Concat(totals.Sheet.Rows.Where(IsGrandTotal))
                                .ToArray(),
                            Meta = totals.Sheet.Meta
                        }
                    };
                }
            }

            if (result.HasMore)
            {
                result = result with
                {
                    Total = null,
                    Sheet = result.Sheet with
                    {
                        Rows = result.Sheet.Rows.Where(r => !IsGrandTotal(r)).ToArray()
                    }
                };
            }

            return result with
            {
                NextCursor = result.HasMore && !string.IsNullOrEmpty(result.NextCursor)
                    ? cursors.Protect(JsonSerializer.Serialize(new Cursor(fingerprint, result.NextCursor)))
                    : null
            };
        }
        finally
        {
            await session.EndAsync(CancellationToken.None);
        }
    }

    public Task<ReportSheetDto> ExecuteExportSheetAsync(
        string reportCode,
        ReportExportRequestDto request,
        CancellationToken ct)
        => engine.ExecuteExportSheetAsync(reportCode, request, ct);

    private static bool IsGrandTotal(ReportSheetRowDto row)
        => row.RowKind == ReportRowKind.Total || row.SemanticRole is "grand_total" or "grand-total";

    private string Decode(string cursor, string fingerprint)
    {
        try
        {
            var decoded = JsonSerializer.Deserialize<Cursor>(cursors.Unprotect(cursor));
            if (decoded is null || decoded.Fingerprint != fingerprint || string.IsNullOrEmpty(decoded.Inner))
                throw InvalidCursor();

            return decoded.Inner;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or CryptographicException)
        {
            throw InvalidCursor();
        }
    }

    private static string Fingerprint(ReportDefinitionDto definition, ReportExecutionRequestDto request)
    {
        var value = JsonSerializer.SerializeToElement(new
        {
            definition,
            request.Layout,
            request.Filters,
            request.Parameters,
            request.GroupPath
        });

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer)) WriteCanonical(writer, value);
        return Convert.ToHexString(SHA256.HashData(buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length))));
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();

            foreach (var property in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteCanonical(writer, property.Value);
            }

            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();

            foreach (var item in value.EnumerateArray())
            {
                WriteCanonical(writer, item);
            }

            writer.WriteEndArray();
        }
        else
        {
            value.WriteTo(writer);
        }
    }

    private static Exception InvalidCursor()
        => new NgbArgumentInvalidException("cursor", "The cursor does not match this report, layout or filters. Run the report again.");

    private sealed record Cursor(string Fingerprint, string Inner);
}

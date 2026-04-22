using NGB.Application.Abstractions.Services;
using System.Globalization;
using System.Text;
using System.Text.Json;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;
using NGB.Core.Dimensions;
using NGB.Core.Reporting.Exceptions;
using NGB.Tools.Extensions;
using NGB.Tools.Normalization;

namespace NGB.Runtime.Reporting.Canonical;

public static class CanonicalReportExecutionHelper
{
    public static DateOnly GetRequiredDateOnlyParameter(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        string parameterCode)
    {
        var parameters = request.Parameters;
        if (parameters is null || !parameters.TryGetValue(parameterCode, out var raw) || string.IsNullOrWhiteSpace(raw))
            throw Invalid(definition, $"parameters.{parameterCode}", $"{GetParameterLabel(definition, parameterCode)} is required.");

        if (!DateOnly.TryParseExact(raw.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
        {
            throw Invalid(
                definition,
                $"parameters.{parameterCode}",
                $"Enter a valid date for {GetParameterLabel(definition, parameterCode)} in yyyy-MM-dd format.");
        }

        return value;
    }

    public static DateOnly? GetOptionalDateOnlyParameter(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        string parameterCode)
    {
        var parameters = request.Parameters;
        if (parameters is null || !parameters.TryGetValue(parameterCode, out var raw) || string.IsNullOrWhiteSpace(raw))
            return null;

        if (!DateOnly.TryParseExact(raw.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
        {
            throw Invalid(
                definition,
                $"parameters.{parameterCode}",
                $"Enter a valid date for {GetParameterLabel(definition, parameterCode)} in yyyy-MM-dd format.");
        }

        return value;
    }

    public static (DateOnly RawFromInclusive, DateOnly RawToInclusive, DateOnly PeriodFromInclusive, DateOnly PeriodToInclusive) GetRequiredDateRange(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        string fromParameterCode = "from_utc",
        string toParameterCode = "to_utc")
    {
        var rawFrom = GetRequiredDateOnlyParameter(definition, request, fromParameterCode);
        var rawTo = GetRequiredDateOnlyParameter(definition, request, toParameterCode);
        if (rawTo < rawFrom)
        {
            throw Invalid(
                definition,
                $"parameters.{toParameterCode}",
                $"{GetParameterLabel(definition, toParameterCode)} must be on or after {GetParameterLabel(definition, fromParameterCode)}.");
        }

        return (rawFrom, rawTo, NormalizeToPeriodMonth(rawFrom), NormalizeToPeriodMonth(rawTo));
    }

    public static DateOnly NormalizeToPeriodMonth(DateOnly value) => new(value.Year, value.Month, 1);

    public static Guid? GetOptionalGuidFilter(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        string filterCode)
    {
        if (!TryGetFilterValue(request, filterCode, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String when value.TryGetGuid(out var guid) && guid != Guid.Empty => guid,
            JsonValueKind.String => throw Invalid(definition, $"filters.{filterCode}", $"Select a valid {GetFilterLabel(definition, filterCode)}."),
            JsonValueKind.Array => ReadGuidList(definition, filterCode, value, allowMultiple: false).SingleOrDefault(),
            _ => throw Invalid(definition, $"filters.{filterCode}", $"Select a valid {GetFilterLabel(definition, filterCode)}.")
        };
    }

    public static IReadOnlyList<Guid> GetOptionalGuidFilters(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        string filterCode)
    {
        if (!TryGetFilterValue(request, filterCode, out var value))
            return [];

        return value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => [],
            JsonValueKind.String or JsonValueKind.Array => ReadGuidList(definition, filterCode, value, allowMultiple: true),
            _ => throw Invalid(definition, $"filters.{filterCode}", $"Select a valid {GetFilterLabel(definition, filterCode)}.")
        };
    }

    public static Guid GetRequiredGuidFilter(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        string filterCode)
    {
        var value = GetOptionalGuidFilter(definition, request, filterCode);
        if (value is { } guid && guid != Guid.Empty)
            return guid;

        throw Invalid(definition, $"filters.{filterCode}", $"{GetFilterLabel(definition, filterCode)} is required.");
    }

    public static bool? GetOptionalBoolFilter(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        string filterCode)
    {
        if (!TryGetFilterValue(request, filterCode, out var value))
            return null;

        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return value.GetBoolean();

        if (value.ValueKind == JsonValueKind.String)
        {
            var raw = value.GetString();
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            if (bool.TryParse(raw, out var parsedBool))
                return parsedBool;

            if (string.Equals(raw, "1", StringComparison.Ordinal))
                return true;

            if (string.Equals(raw, "0", StringComparison.Ordinal))
                return false;

            if (string.Equals(raw, "yes", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(raw, "no", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        throw Invalid(definition, $"filters.{filterCode}", $"Select Yes or No for {GetFilterLabel(definition, filterCode)}.");
    }

    public static DimensionScopeBag? BuildDimensionScopes(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request)
    {
        var scopes = new List<DimensionScope>();
        foreach (var filter in definition.Filters ?? [])
        {
            var dimensionCode = ResolveDimensionCode(filter.Lookup);
            if (string.IsNullOrWhiteSpace(dimensionCode))
                continue;

            if (!TryGetFilter(request, filter.FieldCode, out var filterValue))
                continue;

            var ids = ReadGuidList(definition, filter.FieldCode, filterValue.Value, allowMultiple: filter.IsMulti);
            if (ids.Count == 0)
                continue;

            scopes.Add(new DimensionScope(DimensionId(dimensionCode), ids, includeDescendants: filterValue.IncludeDescendants));
        }

        return scopes.Count == 0 ? null : new DimensionScopeBag(scopes);
    }

    public static ReportDataPage CreatePrebuiltPage(
        ReportSheetDto sheet,
        int offset,
        int limit,
        int? total,
        bool hasMore,
        string? nextCursor = null,
        IReadOnlyDictionary<string, string>? diagnostics = null)
        => new(
            Columns: [],
            Rows: [],
            Offset: offset,
            Limit: limit,
            Total: total,
            HasMore: hasMore,
            NextCursor: nextCursor,
            Diagnostics: diagnostics,
            PrebuiltSheet: sheet);

    public static JsonElement JsonValue<T>(T value) => JsonSerializer.SerializeToElement(value);

    public static string? GetExecutorVariantCode(ReportExecutionRequestDto request)
        => string.IsNullOrWhiteSpace(request.VariantCode) ? null : request.VariantCode.Trim();

    public static string GetParameterLabel(ReportDefinitionDto definition, string parameterCode)
        => definition.Parameters?.FirstOrDefault(x => string.Equals(x.Code, parameterCode, StringComparison.OrdinalIgnoreCase))?.Label
           ?? HumanizeCode(parameterCode);

    public static string GetFilterLabel(ReportDefinitionDto definition, string filterCode)
        => definition.Filters?.FirstOrDefault(x => string.Equals(x.FieldCode, filterCode, StringComparison.OrdinalIgnoreCase))?.Label
           ?? HumanizeCode(filterCode);

    private static string HumanizeCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return code;

        var parts = code.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (parts.Count > 0 && string.Equals(parts[^1], "utc", StringComparison.OrdinalIgnoreCase))
            parts.RemoveAt(parts.Count - 1);

        if (parts.Count > 0 && string.Equals(parts[^1], "id", StringComparison.OrdinalIgnoreCase))
            parts.RemoveAt(parts.Count - 1);

        if (parts.Count == 0)
            return code;

        var builder = new StringBuilder();
        for (var i = 0; i < parts.Count; i++)
        {
            if (i > 0)
                builder.Append(' ');

            var part = parts[i];
            builder.Append(char.ToUpperInvariant(part[0]));
            if (part.Length > 1)
                builder.Append(part[1..]);
        }

        return builder.ToString();
    }

    private static IReadOnlyList<Guid> ReadGuidList(
        ReportDefinitionDto definition,
        string filterCode,
        JsonElement value,
        bool allowMultiple)
    {
        try
        {
            if (value.ValueKind == JsonValueKind.String)
            {
                if (!value.TryGetGuid(out var guid) || guid == Guid.Empty)
                    throw Invalid(definition, $"filters.{filterCode}", $"Select a valid {GetFilterLabel(definition, filterCode)}.");

                return [guid];
            }

            if (value.ValueKind != JsonValueKind.Array)
                throw Invalid(definition, $"filters.{filterCode}", $"Select a valid {GetFilterLabel(definition, filterCode)}.");

            var list = new List<Guid>();
            foreach (var item in value.EnumerateArray())
            {
                if (!item.TryGetGuid(out var itemGuid) || itemGuid == Guid.Empty)
                    throw Invalid(definition, $"filters.{filterCode}", $"Select a valid {GetFilterLabel(definition, filterCode)}.");

                list.Add(itemGuid);
            }

            var distinct = list.Distinct().ToArray();
            if (!allowMultiple && distinct.Length > 1)
            {
                throw Invalid(
                    definition,
                    $"filters.{filterCode}",
                    $"Select a single {GetFilterLabel(definition, filterCode)}.");
            }

            return distinct;
        }
        catch (InvalidOperationException)
        {
            throw Invalid(definition, $"filters.{filterCode}", $"Select a valid {GetFilterLabel(definition, filterCode)}.");
        }
    }

    private static bool TryGetFilterValue(ReportExecutionRequestDto request, string filterCode, out JsonElement value)
    {
        value = default;
        return TryGetFilter(request, filterCode, out _ , out value);
    }

    private static bool TryGetFilter(ReportExecutionRequestDto request, string filterCode, out ReportFilterValueDto filterValue)
        => TryGetFilter(request, filterCode, out filterValue, out _);

    private static bool TryGetFilter(
        ReportExecutionRequestDto request,
        string filterCode,
        out ReportFilterValueDto filterValue,
        out JsonElement value)
    {
        filterValue = default!;
        value = default;
        if (request.Filters is null)
            return false;

        foreach (var pair in request.Filters)
        {
            if (!string.Equals(CodeNormalizer.NormalizeCodeNorm(pair.Key, nameof(filterCode)), CodeNormalizer.NormalizeCodeNorm(filterCode, nameof(filterCode)), StringComparison.OrdinalIgnoreCase))
                continue;

            filterValue = pair.Value;
            value = pair.Value.Value;
            return true;
        }

        return false;
    }

    private static string? ResolveDimensionCode(LookupSourceDto? lookup)
        => lookup switch
        {
            CatalogLookupSourceDto catalog => catalog.CatalogType,
            DocumentLookupSourceDto { DocumentTypes.Count: 1 } document => document.DocumentTypes[0],
            _ => null
        };

    private static Guid DimensionId(string dimensionCode)
        => DeterministicGuid.Create($"Dimension|{CodeNormalizer.NormalizeCodeNorm(dimensionCode, nameof(dimensionCode))}");

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
                ["reportCode"] = CodeNormalizer.NormalizeCodeNorm(definition.ReportCode, nameof(definition.ReportCode))
            });
}

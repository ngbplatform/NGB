using System.Text.Json;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;
using NGB.Core.Dimensions;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Reporting;

public sealed class ReportFilterScopeExpander(
    IDimensionScopeExpansionService? dimensionScopeExpansionService = null,
    IDimensionDefinitionReader? dimensionDefinitions = null)
{
    public async Task<ReportExecutionRequestDto> ExpandAsync(
        ReportDefinitionRuntimeModel runtime,
        ReportExecutionRequestDto request,
        CancellationToken ct)
    {
        if (runtime is null)
            throw new NgbArgumentRequiredException(nameof(runtime));

        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        if (dimensionScopeExpansionService is null || dimensionDefinitions is null)
            return request;

        if (request.Filters is null || request.Filters.Count == 0)
            return request;

        var candidates = new List<ScopeCandidate>();
        foreach (var pair in request.Filters)
        {
            if (!TryResolveDimensionCode(runtime, pair.Key, out var dimensionCode))
                continue;

            if (!TryReadGuidValues(pair.Value.Value, out var valueIds))
                continue;

            if (valueIds.Count == 0)
                continue;

            candidates.Add(new ScopeCandidate(pair.Key, dimensionCode, pair.Value.IncludeDescendants, valueIds));
        }

        if (candidates.Count == 0)
            return request;

        var dimensionIdsByCode = await dimensionDefinitions.GetDimensionIdsByCodesAsync(
            candidates.Select(x => x.DimensionCode).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            ct);

        var scopeCandidates = candidates
            .Where(x => x.IncludeDescendants)
            .Select(x => dimensionIdsByCode.TryGetValue(x.DimensionCode, out var dimensionId)
                ? new DimensionScope(dimensionId, x.ValueIds, includeDescendants: true)
                : null)
            .Where(x => x is not null)
            .Cast<DimensionScope>()
            .ToList();

        if (scopeCandidates.Count == 0)
            return request;

        var expanded = await dimensionScopeExpansionService.ExpandAsync(runtime.ReportCodeNorm, new DimensionScopeBag(scopeCandidates), ct);
        if (expanded is null || expanded.IsEmpty)
            return request;

        var fieldByDimensionId = candidates
            .Where(x => dimensionIdsByCode.TryGetValue(x.DimensionCode, out _))
            .GroupBy(x => dimensionIdsByCode[x.DimensionCode])
            .ToDictionary(x => x.Key, x => x.First().FieldCode, EqualityComparer<Guid>.Default);

        var filters = new Dictionary<string, ReportFilterValueDto>(request.Filters, StringComparer.OrdinalIgnoreCase);
        foreach (var scope in expanded)
        {
            if (!fieldByDimensionId.TryGetValue(scope.DimensionId, out var fieldCode))
                continue;

            filters[fieldCode] = new ReportFilterValueDto(
                JsonSerializer.SerializeToElement(scope.ValueIds),
                IncludeDescendants: false);
        }

        return request with { Filters = filters };
    }

    private static bool TryResolveDimensionCode(
        ReportDefinitionRuntimeModel runtime,
        string filterCode,
        out string dimensionCode)
    {
        dimensionCode = string.Empty;

        if (runtime.Dataset is not null
            && runtime.Dataset.TryGetField(filterCode, out var datasetField)
            && TryResolveDimensionCode(datasetField.Field, out dimensionCode))
        {
            return true;
        }

        var filter = runtime.Definition.Filters?
            .FirstOrDefault(x => string.Equals(x.FieldCode, filterCode, StringComparison.OrdinalIgnoreCase));

        return filter is not null && TryResolveDimensionCode(filter, out dimensionCode);
    }

    private static bool TryResolveDimensionCode(ReportFieldDto field, out string dimensionCode)
    {
        dimensionCode = string.Empty;
        if (field.Kind != ReportFieldKind.Dimension || field.Lookup is null)
            return false;

        switch (field.Lookup)
        {
            case CatalogLookupSourceDto catalog:
                dimensionCode = catalog.CatalogType;
                return !string.IsNullOrWhiteSpace(dimensionCode);

            case DocumentLookupSourceDto { DocumentTypes.Count: 1 } document:
                dimensionCode = document.DocumentTypes[0];
                return !string.IsNullOrWhiteSpace(dimensionCode);

            default:
                return false;
        }
    }

    private static bool TryResolveDimensionCode(ReportFilterFieldDto field, out string dimensionCode)
    {
        dimensionCode = string.Empty;
        if (field.Lookup is null)
            return false;

        switch (field.Lookup)
        {
            case CatalogLookupSourceDto catalog:
                dimensionCode = catalog.CatalogType;
                return !string.IsNullOrWhiteSpace(dimensionCode);

            case DocumentLookupSourceDto { DocumentTypes.Count: 1 } document:
                dimensionCode = document.DocumentTypes[0];
                return !string.IsNullOrWhiteSpace(dimensionCode);

            default:
                return false;
        }
    }

    private static bool TryReadGuidValues(JsonElement value, out IReadOnlyList<Guid> ids)
    {
        ids = [];

        try
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.String when value.TryGetGuid(out var guid):
                    ids = [guid];
                    return true;

                case JsonValueKind.Array:
                {
                    var list = new List<Guid>();
                    foreach (var item in value.EnumerateArray())
                    {
                        if (!item.TryGetGuid(out var itemGuid))
                            return false;

                        if (itemGuid != Guid.Empty)
                            list.Add(itemGuid);
                    }

                    ids = list.Distinct().ToArray();
                    return ids.Count > 0;
                }

                default:
                    return false;
            }
        }
        catch (InvalidOperationException)
        {
            ids = [];
            return false;
        }
    }

    private sealed record ScopeCandidate(
        string FieldCode,
        string DimensionCode,
        bool IncludeDescendants,
        IReadOnlyList<Guid> ValueIds);
}

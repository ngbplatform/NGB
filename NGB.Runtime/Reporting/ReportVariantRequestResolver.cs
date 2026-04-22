using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Core.Reporting.Exceptions;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Reporting;

public sealed class ReportVariantRequestResolver(IReportVariantService variants)
{
    public async Task<ReportExecutionRequestDto> ResolveAsync(
        string reportCode,
        ReportExecutionRequestDto request,
        CancellationToken ct)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        if (string.IsNullOrWhiteSpace(request.VariantCode))
            return request;

        var variant = await variants.GetAsync(reportCode, request.VariantCode, ct);
        if (variant is null)
            throw new ReportVariantNotFoundException(reportCode, request.VariantCode);

        return new ReportExecutionRequestDto(
            Layout: request.Layout ?? variant.Layout,
            Filters: Merge(variant.Filters, request.Filters),
            Parameters: Merge(variant.Parameters, request.Parameters),
            VariantCode: variant.VariantCode,
            Offset: request.Offset,
            Limit: request.Limit,
            Cursor: request.Cursor,
            DisablePaging: request.DisablePaging);
    }

    private static IReadOnlyDictionary<string, T>? Merge<T>(
        IReadOnlyDictionary<string, T>? baseline,
        IReadOnlyDictionary<string, T>? overrides)
    {
        if (baseline is null || baseline.Count == 0)
            return overrides;

        if (overrides is null || overrides.Count == 0)
            return baseline;

        var merged = new Dictionary<string, T>(baseline, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in overrides)
        {
            merged[pair.Key] = pair.Value;
        }

        return merged;
    }
}

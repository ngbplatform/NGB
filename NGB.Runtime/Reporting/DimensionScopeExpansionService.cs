using NGB.Application.Abstractions.Services;
using NGB.Core.Dimensions;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Reporting;

public sealed class DimensionScopeExpansionService(IEnumerable<IReportDimensionScopeExpander> expanders)
    : IDimensionScopeExpansionService
{
    public async Task<DimensionScopeBag?> ExpandAsync(
        string reportCode,
        DimensionScopeBag? scopes,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reportCode))
            throw new NgbArgumentRequiredException(nameof(reportCode));

        if (scopes is null || scopes.IsEmpty)
            return scopes;

        var current = scopes;
        foreach (var expander in expanders)
        {
            if (expander is null)
                continue;

            current = await expander.ExpandAsync(reportCode, current, ct);
        }

        return current;
    }
}

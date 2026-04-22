using NGB.Core.Dimensions;

namespace NGB.Application.Abstractions.Services;

/// <summary>
/// Expands parsed report dimension scopes into their effective value sets.
/// Multiple vertical contributors may participate.
/// </summary>
public interface IDimensionScopeExpansionService
{
    Task<DimensionScopeBag?> ExpandAsync(string reportCode, DimensionScopeBag? scopes, CancellationToken ct = default);
}

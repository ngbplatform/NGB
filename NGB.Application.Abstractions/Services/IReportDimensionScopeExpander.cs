using NGB.Core.Dimensions;

namespace NGB.Application.Abstractions.Services;

/// <summary>
/// Vertical/module hook that can expand report dimension scopes before report readers execute.
/// Intended for hierarchical semantics such as Property -> child Units.
/// </summary>
public interface IReportDimensionScopeExpander
{
    Task<DimensionScopeBag> ExpandAsync(string reportCode, DimensionScopeBag scopes, CancellationToken ct);
}

using NGB.Persistence.Common;

namespace NGB.Persistence.Catalogs.Universal;

/// <summary>
/// A universal catalog query.
/// - <see cref="Search"/> is applied to the display column via ILIKE.
/// - <see cref="Filters"/> are applied as equality checks (column::text = value).
///
/// Filter keys must be validated by the caller against the catalog metadata.
/// </summary>
public sealed record CatalogQuery(string? Search, IReadOnlyList<CatalogFilter> Filters)
{
    public SoftDeleteFilterMode SoftDeleteFilterMode { get; init; } = SoftDeleteFilterMode.All;
}

public sealed record CatalogFilter(string ColumnName, string Value);

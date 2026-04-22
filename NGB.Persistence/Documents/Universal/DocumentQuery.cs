using NGB.Persistence.Common;
using NGB.Metadata.Base;

namespace NGB.Persistence.Documents.Universal;

/// <summary>
/// A universal document query.
/// - <see cref="Search"/> is applied to the display column via ILIKE.
/// - <see cref="Filters"/> are validated by the runtime against document list metadata.
///
/// Filter keys must be validated by the caller against the document metadata.
/// </summary>
public sealed record DocumentQuery(string? Search, IReadOnlyList<DocumentFilter> Filters)
{
    public SoftDeleteFilterMode SoftDeleteFilterMode { get; init; } = SoftDeleteFilterMode.All;
    public DocumentPeriodFilter? PeriodFilter { get; init; }
}

public sealed record DocumentFilter(
    string Key,
    IReadOnlyList<string> Values,
    ColumnType ValueType,
    string? HeadColumnName = null);

/// <summary>
/// Optional generic period filter for document list pages.
/// - The runtime resolves a single “primary” date/date-time column for the document type.
/// - The reader applies inclusive range filtering using the column cast to <c>date</c>.
/// </summary>
public sealed record DocumentPeriodFilter(string ColumnName, DateOnly? FromInclusive, DateOnly? ToInclusive);

using NGB.Contracts.Common;
using NGB.Contracts.Reporting;

namespace NGB.Application.Abstractions.Services;

/// <summary>
/// Shared budget for the set of row keys used to fetch a pivot page's cells.
/// Key filters use the same scalar-value budget as user filters, independently
/// of the provider's parameter ceiling. Wide keys therefore require smaller pages.
/// </summary>
public static class ReportRowSelectionLimits
{
    public const int MaxFields = ReportLayoutLimits.MaxRowGroups + ReportLayoutLimits.MaxDetailFields;
    public const int MaxKeys = PagingLimits.MaxPageSize;
    public const int MaxValues = ReportLayoutLimits.MaxTotalFilterValues;

    /// <summary>Returns zero when the key width is outside the supported layout contract.</summary>
    public static int GetMaxKeyCount(int fieldCount)
        => fieldCount is < 1 or > MaxFields ? 0 : Math.Min(MaxKeys, MaxValues / fieldCount);
}

using NGB.Core.Dimensions;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.Accounting.Reports;

/// <summary>A complete account activity range, independent of interactive pagination.</summary>
public sealed record AccountActivityQuery(
    Guid AccountId,
    DateOnly FromInclusive,
    DateOnly ToInclusive,
    DimensionScopeBag? DimensionScopes)
{
    public void EnsureInvariant()
    {
        if (AccountId == Guid.Empty)
            throw new NgbArgumentRequiredException(nameof(AccountId));

        if (ToInclusive < FromInclusive)
            throw new NgbArgumentOutOfRangeException(nameof(ToInclusive), ToInclusive, "To must be on or after From.");

        FromInclusive.EnsureMonthStart(nameof(FromInclusive));
        ToInclusive.EnsureMonthStart(nameof(ToInclusive));
    }
}

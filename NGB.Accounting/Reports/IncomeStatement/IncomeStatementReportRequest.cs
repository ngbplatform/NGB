using NGB.Core.Dimensions;
using NGB.Tools.Extensions;
using NGB.Tools.Exceptions;

namespace NGB.Accounting.Reports.IncomeStatement;

public sealed class IncomeStatementReportRequest
{
    public DateOnly FromInclusive { get; init; }
    public DateOnly ToInclusive { get; init; }

    /// <summary>
    /// Optional multi-value dimension filter:
    /// OR within one dimension, AND across dimensions.
    /// </summary>
    public DimensionScopeBag? DimensionScopes { get; init; }

    /// <summary>
    /// If true, includes accounts with 0 amount (useful for templates).
    /// Default: false.
    /// </summary>
    public bool IncludeZeroLines { get; init; }

    public void Validate()
    {
        FromInclusive.EnsureMonthStart(nameof(FromInclusive));
        ToInclusive.EnsureMonthStart(nameof(ToInclusive));

        if (ToInclusive < FromInclusive)
            throw new NgbArgumentOutOfRangeException(
                paramName: nameof(ToInclusive),
                actualValue: ToInclusive,
                reason: "To must be on or after From.");
    }
}

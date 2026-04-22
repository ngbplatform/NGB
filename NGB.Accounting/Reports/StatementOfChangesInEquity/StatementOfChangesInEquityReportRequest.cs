using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.Accounting.Reports.StatementOfChangesInEquity;

public sealed class StatementOfChangesInEquityReportRequest
{
    public DateOnly FromInclusive { get; init; }
    public DateOnly ToInclusive { get; init; }

    public void Validate()
    {
        FromInclusive.EnsureMonthStart(nameof(FromInclusive));
        ToInclusive.EnsureMonthStart(nameof(ToInclusive));

        if (ToInclusive < FromInclusive)
            throw new NgbArgumentOutOfRangeException(nameof(ToInclusive), ToInclusive, "To must be on or after From.");
    }
}

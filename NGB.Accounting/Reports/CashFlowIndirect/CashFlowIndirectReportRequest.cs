using NGB.Tools.Exceptions;

namespace NGB.Accounting.Reports.CashFlowIndirect;

public sealed class CashFlowIndirectReportRequest
{
    public DateOnly FromInclusive { get; init; }
    public DateOnly ToInclusive { get; init; }

    public void Validate()
    {
        if (ToInclusive < FromInclusive)
            throw new NgbArgumentOutOfRangeException(nameof(ToInclusive), ToInclusive, "To must be on or after From.");
    }
}

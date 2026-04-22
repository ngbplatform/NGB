using NGB.Accounting.Reports.AccountingConsistency;

namespace NGB.Runtime.Maintenance;

public sealed class AccountingRebuildResult
{
    public required DateOnly Period { get; init; }

    public required int TurnoverRowsWritten { get; init; }
    public required int BalanceRowsWritten { get; init; }

    public required AccountingConsistencyReport VerifyReport { get; init; }
}

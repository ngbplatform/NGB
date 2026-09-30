using System.Runtime.CompilerServices;
using NGB.Accounting.Reports.TrialBalance;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Reporting.Internal;

namespace NGB.Runtime.Reporting;

internal static class TrialBalanceRows
{
    public static async IAsyncEnumerable<TrialBalanceReportRow> ReadAsync(
        IAsyncEnumerable<TrialBalanceAccountSummary> accounts,
        bool showSubtotals,
        [EnumeratorCancellation] CancellationToken ct)
    {
        string? group = null;
        decimal opening = 0, debit = 0, credit = 0;

        await foreach (var account in accounts.WithCancellation(ct))
        {
            var nextGroup = account.Type.ToString();
            if (group != nextGroup)
            {
                if (group is not null && showSubtotals)
                    yield return Subtotal(group, opening, debit, credit);

                group = nextGroup;
                opening = debit = credit = 0;

                yield return new(
                    TrialBalanceReportRowKind.Group,
                    group,
                    0,
                    0,
                    0,
                    0,
                    0,
                    $"group:{group}");
            }

            opening += account.OpeningBalance;
            debit += account.DebitAmount;
            credit += account.CreditAmount;

            yield return new(
                TrialBalanceReportRowKind.Detail,
                ReportDisplayHelpers.BuildAccountDisplay(account.Code, account.Name),
                account.OpeningBalance,
                account.DebitAmount,
                account.CreditAmount,
                account.OpeningBalance + account.DebitAmount - account.CreditAmount,
                1,
                $"detail:{account.AccountId}",
                account.AccountId);
        }

        if (group is not null && showSubtotals)
            yield return Subtotal(group, opening, debit, credit);
    }

    private static TrialBalanceReportRow Subtotal(string group, decimal opening, decimal debit, decimal credit)
        => new(
            TrialBalanceReportRowKind.Subtotal,
            $"{group} subtotal",
            opening,
            debit,
            credit,
            opening + debit - credit,
            0,
            $"subtotal:{group}");
}

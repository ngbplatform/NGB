using NGB.Accounting.Accounts;
using NGB.Accounting.Reports.TrialBalance;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Reporting.Internal;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Reporting;

/// <summary>
/// Runtime Trial Balance report reader for canonical TB.
/// Produces the full bounded logical row model for the requested period.
/// </summary>
public sealed class TrialBalanceReportService(
    ITrialBalanceSnapshotReader snapshotReader,
    IAccountByIdResolver accountByIdResolver)
    : ITrialBalanceReportReader
{
    public async Task<TrialBalanceReportPage> GetPageAsync(
        TrialBalanceReportPageRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        var snapshot = await snapshotReader.GetAsync(
            request.FromInclusive,
            request.ToInclusive,
            request.DimensionScopes,
            ct);

        var accountIds = snapshot.Rows
            .Select(x => x.AccountId)
            .Distinct()
            .ToArray();

        var accountsById = await accountByIdResolver.GetByIdsAsync(accountIds, ct);
        var accountRows = BuildAccountRows(snapshot, accountsById);
        var rows = BuildBoundedRows(accountRows, request.ShowSubtotals);

        return new TrialBalanceReportPage(
            Rows: rows,
            Total: rows.Count,
            HasMore: false,
            Totals: new TrialBalanceReportTotals(
                OpeningBalance: snapshot.Rows.Sum(x => x.OpeningBalance),
                DebitAmount: snapshot.Rows.Sum(x => x.DebitAmount),
                CreditAmount: snapshot.Rows.Sum(x => x.CreditAmount),
                ClosingBalance: snapshot.Rows.Sum(x => x.OpeningBalance + (x.DebitAmount - x.CreditAmount))));
    }

    private static IReadOnlyList<AccountBalanceRow> BuildAccountRows(
        TrialBalanceSnapshot snapshot,
        IReadOnlyDictionary<Guid, Account> accountsById)
    {
        var map = new Dictionary<Guid, AccountBalanceAccumulator>();

        foreach (var row in snapshot.Rows)
        {
            if (!map.TryGetValue(row.AccountId, out var acc))
            {
                accountsById.TryGetValue(row.AccountId, out var account);
                var code = account?.Code
                           ?? row.AccountCode;
                var display = ReportDisplayHelpers.BuildAccountDisplay(code, account?.Name);
                var group = account is null ? "Unknown" : account.Type.ToString();
                var groupOrder = account is null ? int.MaxValue : (int)account.Type;

                acc = new AccountBalanceAccumulator(
                    row.AccountId,
                    code,
                    display,
                    group,
                    groupOrder,
                    0m,
                    0m,
                    0m,
                    0m);
            }

            acc = acc with
            {
                OpeningBalance = acc.OpeningBalance + row.OpeningBalance,
                DebitAmount = acc.DebitAmount + row.DebitAmount,
                CreditAmount = acc.CreditAmount + row.CreditAmount,
                ClosingBalance = acc.ClosingBalance + row.OpeningBalance + (row.DebitAmount - row.CreditAmount)
            };

            map[row.AccountId] = acc;
        }

        return map.Values
            .OrderBy(x => x.GroupOrder)
            .ThenBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.AccountDisplay, StringComparer.OrdinalIgnoreCase)
            .Select(x => new AccountBalanceRow(
                x.AccountId,
                x.Code,
                x.AccountDisplay,
                x.GroupLabel,
                x.GroupOrder,
                x.OpeningBalance,
                x.DebitAmount,
                x.CreditAmount,
                x.ClosingBalance))
            .ToList();
    }

    private static IReadOnlyList<TrialBalanceReportRow> BuildBoundedRows(
        IReadOnlyList<AccountBalanceRow> accountRows,
        bool showSubtotals)
    {
        if (accountRows.Count == 0)
            return [];

        var distinctGroupCount = accountRows
            .Select(x => x.GroupLabel)
            .Distinct(StringComparer.Ordinal)
            .Count();

        var capacity = accountRows.Count + distinctGroupCount + (showSubtotals ? distinctGroupCount : 0);
        var result = new List<TrialBalanceReportRow>(capacity);

        string? currentGroup = null;
        var subtotalOpening = 0m;
        var subtotalDebit = 0m;
        var subtotalCredit = 0m;
        var subtotalClosing = 0m;

        foreach (var item in accountRows)
        {
            if (!string.Equals(currentGroup, item.GroupLabel, StringComparison.Ordinal))
            {
                if (currentGroup is not null && showSubtotals)
                {
                    result.Add(new TrialBalanceReportRow(
                        RowKind: TrialBalanceReportRowKind.Subtotal,
                        AccountDisplay: $"{currentGroup} subtotal",
                        OpeningBalance: subtotalOpening,
                        DebitAmount: subtotalDebit,
                        CreditAmount: subtotalCredit,
                        ClosingBalance: subtotalClosing,
                        OutlineLevel: 0,
                        GroupKey: $"subtotal:{currentGroup}"));
                }

                currentGroup = item.GroupLabel;
                subtotalOpening = 0m;
                subtotalDebit = 0m;
                subtotalCredit = 0m;
                subtotalClosing = 0m;

                result.Add(new TrialBalanceReportRow(
                    RowKind: TrialBalanceReportRowKind.Group,
                    AccountDisplay: currentGroup,
                    OpeningBalance: 0m,
                    DebitAmount: 0m,
                    CreditAmount: 0m,
                    ClosingBalance: 0m,
                    OutlineLevel: 0,
                    GroupKey: $"group:{currentGroup}"));
            }

            result.Add(new TrialBalanceReportRow(
                RowKind: TrialBalanceReportRowKind.Detail,
                AccountDisplay: item.AccountDisplay,
                OpeningBalance: item.OpeningBalance,
                DebitAmount: item.DebitAmount,
                CreditAmount: item.CreditAmount,
                ClosingBalance: item.ClosingBalance,
                OutlineLevel: 1,
                GroupKey: $"detail:{item.AccountId}",
                AccountId: item.AccountId));

            subtotalOpening += item.OpeningBalance;
            subtotalDebit += item.DebitAmount;
            subtotalCredit += item.CreditAmount;
            subtotalClosing += item.ClosingBalance;
        }

        if (currentGroup is not null && showSubtotals)
        {
            result.Add(new TrialBalanceReportRow(
                RowKind: TrialBalanceReportRowKind.Subtotal,
                AccountDisplay: $"{currentGroup} subtotal",
                OpeningBalance: subtotalOpening,
                DebitAmount: subtotalDebit,
                CreditAmount: subtotalCredit,
                ClosingBalance: subtotalClosing,
                OutlineLevel: 0,
                GroupKey: $"subtotal:{currentGroup}"));
        }

        return result;
    }

    private sealed record AccountBalanceAccumulator(
        Guid AccountId,
        string Code,
        string AccountDisplay,
        string GroupLabel,
        int GroupOrder,
        decimal OpeningBalance,
        decimal DebitAmount,
        decimal CreditAmount,
        decimal ClosingBalance);

    private sealed record AccountBalanceRow(
        Guid AccountId,
        string Code,
        string AccountDisplay,
        string GroupLabel,
        int GroupOrder,
        decimal OpeningBalance,
        decimal DebitAmount,
        decimal CreditAmount,
        decimal ClosingBalance);
}

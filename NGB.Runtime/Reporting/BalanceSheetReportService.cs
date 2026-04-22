using Microsoft.Extensions.Logging;
using NGB.Accounting.Accounts;
using NGB.Accounting.Reports.BalanceSheet;
using NGB.Persistence.Readers.Reports;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.Runtime.Reporting;

/// <summary>
/// Builds Balance Sheet (Assets, Liabilities, Equity) "as of" the end of a month.
///
/// Key points:
/// - Uses the latest closed balances snapshot less than or equal to <see cref="BalanceSheetReportRequest.AsOfPeriod"/>
///   and rolls forward subsequent turnovers to produce a true cumulative "as of" view.
/// - If no closed balances snapshot exists yet, falls back to inception-to-date turnovers so a fresh or unclosed DB
///   still produces a correct Balance Sheet.
/// - Aggregates by AccountId (dimensions are rolled up).
/// - Applies statement-section NormalBalance (without contra-flip) to present statement amounts.
/// - Uses active-only Chart of Accounts snapshot via IChartOfAccountsProvider,
///   but resolves inactive accounts with historical activity via IAccountByIdResolver.
/// - Optionally adds a synthetic Equity line "Net Income" computed from P&L sections.
/// </summary>
public sealed class BalanceSheetReportService(
    IBalanceSheetSnapshotReader snapshotReader,
    IChartOfAccountsProvider chartOfAccountsProvider,
    IAccountByIdResolver accountByIdResolver,
    ILogger<BalanceSheetReportService> logger)
    : IBalanceSheetReportReader
{
    private const int RollForwardWarningThresholdPeriods = 12;

    public async Task<BalanceSheetReport> GetAsync(BalanceSheetReportRequest request, CancellationToken ct = default)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        request.AsOfPeriod.EnsureMonthStart(nameof(request.AsOfPeriod));

        var chart = await chartOfAccountsProvider.GetAsync(ct);
        var snapshot = await snapshotReader.GetAsync(request.AsOfPeriod, request.DimensionScopes, ct);
        var snapshotRows = snapshot.Rows;

        // Presented balances aggregated by AccountId (dimensions are rolled up).
        var byAccount = new Dictionary<Guid, decimal>();

        // Runtime ChartOfAccountsProvider returns an active-only snapshot.
        // For reporting, we MUST also include inactive accounts that have historical activity,
        // otherwise statements can silently drop lines and become incorrect/unbalanced.
        var resolvedAccounts = new Dictionary<Guid, Account>();

        // Pre-resolve all accounts that are missing from the active snapshot in a single round-trip.
        // This avoids N+1 lookups for statements/closing on large datasets.
        var missingIds = new HashSet<Guid>();
        foreach (var row in snapshotRows)
        {
            if (row.AccountId == Guid.Empty)
                continue;

            if (chart.TryGet(row.AccountId, out var active) && active is not null)
                continue;

            missingIds.Add(row.AccountId);
        }

        var missingResolved = missingIds.Count == 0
            ? new Dictionary<Guid, Account>()
            : await accountByIdResolver.GetByIdsAsync(missingIds, ct);

        Account ResolveAccountOrThrow(Guid accountId)
        {
            if (resolvedAccounts.TryGetValue(accountId, out var known))
                return known;

            if (chart.TryGet(accountId, out var active) && active is not null)
            {
                resolvedAccounts[accountId] = active;
                return active;
            }

            if (missingResolved.TryGetValue(accountId, out var resolved))
            {
                resolvedAccounts[accountId] = resolved;
                return resolved;
            }

            throw new AccountNotFoundException(accountId);
        }

        foreach (var row in snapshotRows)
        {
            if (row.AccountId == Guid.Empty)
                continue;

            var account = ResolveAccountOrThrow(row.AccountId);

            // IMPORTANT:
            // Snapshot closing balances are stored as (debits - credits).
            // For statement presentation we normalize using the statement-section normal side.
            // We intentionally do NOT apply contra-flip here: contra accounts naturally carry
            // the opposite balance and therefore show up as negative values in statements.
            var sectionNb = NormalBalanceDefaults.FromStatementSection(account.StatementSection);
            var presented = sectionNb == NormalBalance.Debit
                ? row.ClosingBalance
                : -row.ClosingBalance;

            if (presented == 0m && !request.IncludeZeroAccounts)
                continue;

            byAccount.TryGetValue(account.Id, out var cur);
            byAccount[account.Id] = cur + presented;
        }

        List<BalanceSheetLine> BuildLinesForSection(StatementSection section)
        {
            var lines = new List<BalanceSheetLine>();

            foreach (var (accountId, amount) in byAccount)
            {
                if (!resolvedAccounts.TryGetValue(accountId, out var acc))
                    throw new AccountNotFoundException(accountId);

                if (acc.StatementSection != section)
                    continue;

                lines.Add(new BalanceSheetLine
                {
                    AccountId = acc.Id,
                    AccountCode = acc.Code,
                    AccountName = acc.Name,
                    Amount = amount
                });
            }

            // Stable ordering for UX: by code then name.
            lines.Sort(static (a, b) =>
            {
                var c = string.CompareOrdinal(a.AccountCode, b.AccountCode);
                return c != 0 ? c : string.CompareOrdinal(a.AccountName, b.AccountName);
            });

            return lines;
        }

        var assetsLines = BuildLinesForSection(StatementSection.Assets);
        var liabilitiesLines = BuildLinesForSection(StatementSection.Liabilities);
        var equityLines = BuildLinesForSection(StatementSection.Equity);

        decimal SumSection(IEnumerable<BalanceSheetLine> lines) => lines.Sum(x => x.Amount);

        var totalAssets = SumSection(assetsLines);
        var totalLiabilities = SumSection(liabilitiesLines);
        var totalEquity = SumSection(equityLines);

        if (request.IncludeNetIncomeInEquity)
        {
            decimal income = 0m;
            decimal expenses = 0m;

            foreach (var (accountId, amount) in byAccount)
            {
                if (!resolvedAccounts.TryGetValue(accountId, out var acc))
                    throw new AccountNotFoundException(accountId);

                switch (acc.StatementSection)
                {
                    case StatementSection.Income:
                    case StatementSection.OtherIncome:
                        income += amount;
                        break;

                    case StatementSection.CostOfGoodsSold:
                    case StatementSection.Expenses:
                    case StatementSection.OtherExpense:
                        expenses += amount;
                        break;
                }
            }

            var ni = income - expenses;

            if (ni != 0m)
            {
                equityLines.Add(new BalanceSheetLine
                {
                    AccountId = Guid.Empty,
                    AccountCode = "NET",
                    AccountName = "Net Income",
                    Amount = ni
                });

                // Keep ordering: put synthetic line at the end.
                // (We intentionally don't reorder equityLines again.)
                totalEquity += ni;
            }
        }

        var totalLe = totalLiabilities + totalEquity;
        var diff = totalAssets - totalLe;

        if (snapshot.LatestClosedPeriod is null)
        {
            logger.LogWarning(
                "Balance Sheet is using inception-to-date turnovers because no closed balances snapshot exists. asOfPeriod={AsOfPeriod}",
                request.AsOfPeriod);
        }
        else if (snapshot.RollForwardPeriods > RollForwardWarningThresholdPeriods)
        {
            logger.LogWarning(
                "Balance Sheet roll-forward is spanning many periods. asOfPeriod={AsOfPeriod} latestClosedPeriod={LatestClosedPeriod} rollForwardPeriods={RollForwardPeriods}",
                request.AsOfPeriod,
                snapshot.LatestClosedPeriod,
                snapshot.RollForwardPeriods);
        }

        var sections = new[]
        {
            new BalanceSheetSection
            {
                Section = StatementSection.Assets,
                Title = "Assets",
                Lines = assetsLines,
                Total = totalAssets
            },
            new BalanceSheetSection
            {
                Section = StatementSection.Liabilities,
                Title = "Liabilities",
                Lines = liabilitiesLines,
                Total = totalLiabilities
            },
            new BalanceSheetSection
            {
                Section = StatementSection.Equity,
                Title = "Equity",
                Lines = equityLines,
                Total = totalEquity
            }
        };

        return new BalanceSheetReport
        {
            AsOfPeriod = request.AsOfPeriod,
            Sections = sections,
            TotalAssets = totalAssets,
            TotalLiabilities = totalLiabilities,
            TotalEquity = totalEquity,
            TotalLiabilitiesAndEquity = totalLe,
            Difference = diff,
            IsBalanced = diff == 0m
        };
    }
}

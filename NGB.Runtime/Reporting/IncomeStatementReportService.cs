using NGB.Accounting.Accounts;
using NGB.Accounting.Reports.IncomeStatement;
using NGB.Persistence.Readers.Reports;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Reporting;

/// <summary>
/// Builds a Profit & Loss (Income Statement) report using a specialized
/// income-statement snapshot reader over the requested range.
///
/// Conventions:
/// - Income / OtherIncome: amount = Credit - Debit (positive means net income for that line)
/// - Expenses / OtherExpense: amount = Debit - Credit (positive means net expense)
/// - Contra accounts naturally appear as negative amounts when they carry the opposite balance.
/// - Inactive accounts with historical activity are still included.
/// </summary>
public sealed class IncomeStatementReportService(IIncomeStatementSnapshotReader snapshotReader)
    : IIncomeStatementReportReader
{
    public async Task<IncomeStatementReport> GetAsync(
        IncomeStatementReportRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        request.Validate();

        var snapshot = await snapshotReader.GetAsync(
            request.FromInclusive,
            request.ToInclusive,
            request.DimensionScopes,
            request.IncludeZeroLines,
            ct);

        static NormalBalance GetNormalBalanceForSection(StatementSection section) => section switch
        {
            StatementSection.Income => NormalBalance.Credit,
            StatementSection.OtherIncome => NormalBalance.Credit,
            StatementSection.Expenses => NormalBalance.Debit,
            StatementSection.CostOfGoodsSold => NormalBalance.Debit,
            StatementSection.OtherExpense => NormalBalance.Debit,
            // Balance sheet sections are not expected here.
            _ => throw new NgbInvariantViolationException(
                message: "Income statement cannot compute amount for a non-P&L statement section.",
                context: new Dictionary<string, object?>
                {
                    ["statementSection"] = section.ToString()
                })
        };

        foreach (var row in snapshot.Rows)
        {
            _ = GetNormalBalanceForSection(row.StatementSection);
        }

        decimal ComputeAmount(StatementSection section, decimal debit, decimal credit)
        {
            var nb = GetNormalBalanceForSection(section);
            var amount = nb == NormalBalance.Credit
                ? credit - debit
                : debit - credit;

            return amount;
        }

        IncomeStatementSection BuildSection(StatementSection section)
        {
            var accounts = snapshot.Rows
                .Where(x => x.StatementSection == section)
                .OrderBy(x => x.AccountCode)
                .ToList();

            var lines = new List<IncomeStatementLine>(accounts.Count);
            var total = 0m;

            foreach (var a in accounts)
            {
                var amount = ComputeAmount(a.StatementSection, a.DebitAmount, a.CreditAmount);

                if (!request.IncludeZeroLines && amount == 0m)
                    continue;

                total += amount;
                lines.Add(new IncomeStatementLine
                {
                    AccountId = a.AccountId,
                    AccountCode = a.AccountCode,
                    AccountName = a.AccountName,
                    Amount = amount
                });
            }

            return new IncomeStatementSection
            {
                Section = section,
                Lines = lines,
                Total = total
            };
        }

        var income = BuildSection(StatementSection.Income);
        var cogs = BuildSection(StatementSection.CostOfGoodsSold);
        var expenses = BuildSection(StatementSection.Expenses);
        var otherIncome = BuildSection(StatementSection.OtherIncome);
        var otherExpense = BuildSection(StatementSection.OtherExpense);

        var totalIncome = income.Total + otherIncome.Total;
        var totalExpenses = cogs.Total + expenses.Total + otherExpense.Total;
        var netIncome = totalIncome - totalExpenses;

        var sections = new List<IncomeStatementSection>
        {
            income,
            cogs,
            expenses,
            otherIncome,
            otherExpense
        };

        // Drop empty sections if IncludeZeroLines == false (more compact output)
        if (!request.IncludeZeroLines)
            sections = sections.Where(s => s.Lines.Count > 0).ToList();

        return new IncomeStatementReport
        {
            FromInclusive = request.FromInclusive,
            ToInclusive = request.ToInclusive,
            Sections = sections,
            TotalIncome = totalIncome,
            TotalExpenses = totalExpenses,
            NetIncome = netIncome,
            TotalOtherIncome = otherIncome.Total,
            TotalOtherExpense = otherExpense.Total
        };
    }
}

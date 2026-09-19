using System.Text.Json;
using NGB.Accounting.Accounts;
using NGB.Accounting.Reports.BalanceSheet;
using NGB.Accounting.Reports.IncomeStatement;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Core.Reporting;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Reporting.Canonical;
using NGB.Runtime.Reporting.Internal;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Reporting;

public sealed class AccountingSummaryPagedExecutor(IAccountingSummaryPageReader reader)
{
    public static bool Supports(string code)
        => code is AccountingReportCodes.TrialBalance
            or AccountingReportCodes.BalanceSheet
            or AccountingReportCodes.IncomeStatement
            or AccountingReportCodes.StatementOfChangesInEquity;

    public async Task<ReportExecutionResult> ExecuteAsync(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        CancellationToken ct)
    {
        var kind = definition.ReportCode switch
        {
            AccountingReportCodes.TrialBalance => AccountingSummaryKind.TrialBalance,
            AccountingReportCodes.BalanceSheet => AccountingSummaryKind.BalanceSheet,
            AccountingReportCodes.IncomeStatement => AccountingSummaryKind.IncomeStatement,
            _ => AccountingSummaryKind.Equity
        };

        DateOnly rawFrom, rawTo, from, to;
        if (kind == AccountingSummaryKind.BalanceSheet)
        {
            rawTo = CanonicalReportExecutionHelper.GetRequiredDateOnlyParameter(definition, request, "as_of_utc");
            rawFrom = from = to = new(rawTo.Year, rawTo.Month, 1);
        }

        else (rawFrom, rawTo, from, to) = CanonicalReportExecutionHelper.GetRequiredDateRange(definition, request);
        var query = new AccountingSummaryQuery(kind, from, to, CanonicalReportExecutionHelper.BuildDimensionScopes(definition, request));
        var equity = kind == AccountingSummaryKind.Equity;
        var trial = kind == AccountingSummaryKind.TrialBalance;
        var path = request.GroupPath ?? [];
        var group = equity ? 3 : -1;

        if (path.Count > (equity ? 0 : 1) || path.Count == 1 
            && (path[0].ValueKind != JsonValueKind.Number || !path[0].TryGetInt32(out group) || !ValidGroup(kind, group)))
        {
            throw new NgbArgumentInvalidException("groupPath", "Invalid accounting report section.");
        }

        var root = !equity && path.Count == 0;
        var limit = Math.Clamp(request.Limit, 1, 500);
        var rows = new List<ReportSheetRowDto>();
        var more = false;
        string? next = null;
        IReadOnlyList<AccountingSummaryValue> groups = [];

        if (root)
        {
            if (!string.IsNullOrEmpty(request.Cursor))
                throw new NgbArgumentInvalidException("cursor", "Sections fit on one page.");

            groups = await reader.ReadGroupsAsync(query, ct);
            foreach (var value in groups.Where(g => ValidGroup(kind, g.Group)))
            {
                var amount = trial ? 0 : Presented(kind, value);
                if (kind == AccountingSummaryKind.BalanceSheet && value.Group == 3)
                    amount -= groups.Where(g => g.Group >= 4).Sum(g => g.Closing);

                var title = trial
                    ? ((AccountType)value.Group).ToString()
                    : IncomeStatementCanonicalReportExecutor.HumanizeSection((StatementSection)value.Group);

                rows.Add(new(
                    ReportRowKind.Group,
                    trial
                        ? [Label(title), Number(value.Debit), Number(value.Credit)]
                        : [Label(title), Number(amount)],
                    ChildrenPath: [JsonSerializer.SerializeToElement(value.Group)],
                    GroupKey: value.Group.ToString()));
            }

            // Equity can consist exclusively of synthetic earnings with no equity account observations.
            if (kind == AccountingSummaryKind.BalanceSheet
                && groups.All(g => g.Group != 3)
                && groups.Any(g => g.Group >= 4 && g.Closing != 0))
            {
                rows.Add(new(
                    ReportRowKind.Group,
                    [Label("Equity"), Number(-groups.Where(g => g.Group >= 4).Sum(g => g.Closing))],
                    ChildrenPath: [JsonSerializer.SerializeToElement(3)],
                    GroupKey: "3"));
            }
        }
        else
        {
            AccountingSummaryKey? after = null;
            if (!string.IsNullOrEmpty(request.Cursor))
            {
                try
                {
                    after = JsonSerializer.Deserialize<AccountingSummaryKey>(Convert.FromBase64String(request.Cursor));
                }
                catch (Exception ex) when (ex is FormatException or JsonException)
                {
                    throw new NgbArgumentInvalidException("cursor", "Invalid accounting page cursor.");
                }

                if (after is null || after.Code is null || after.Code.Length > 500)
                    throw new NgbArgumentInvalidException("cursor", "Invalid accounting page cursor.");
            }

            var page = await reader.ReadAccountsAsync(query, group, after, limit, ct);
            rows.AddRange(page.Rows.Select(Detail));
            more = page.HasMore;
            next = page.Next is null
                ? null
                : Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(page.Next));

            if (!more
                && (request.Layout?.ShowGrandTotals != false
                    || group == 3
                    && (kind is AccountingSummaryKind.BalanceSheet or AccountingSummaryKind.Equity)))
            {
                groups = await reader.ReadGroupsAsync(query, ct);
            }

            if (!more && group == 3 && (kind is AccountingSummaryKind.BalanceSheet or AccountingSummaryKind.Equity))
            {
                var earnings = new AccountingSummaryValue(
                    3,
                    Guid.Empty,
                    equity ? "CURR_EARNINGS" : "NET",
                    equity ? "Current Earnings (Unclosed)" : "Net Income",
                    groups
                        .Where(g => g.Group >= 4)
                        .Sum(g => g.Opening),
                    0,
                    0,
                    groups
                        .Where(g => g.Group >= 4)
                        .Sum(g => g.Closing));

                if (earnings.Closing != 0 || equity && earnings.Opening != 0)
                    rows.Add(Detail(earnings));
            }
        }
        if (!more && request.Layout?.ShowGrandTotals != false && groups.Count > 0)
        {
            if (root)
            {
                rows.AddRange(GrandTotals(kind, groups, from, to));
            }
            else if (equity)
            {
                var opening = -groups
                    .Where(g => g.Group >= 3)
                    .Sum(g => g.Opening);
                var closing = -groups
                    .Where(g => g.Group >= 3)
                    .Sum(g => g.Closing);

                rows.Add(new(
                    ReportRowKind.Total,
                    [Label("Total Equity"), Number(opening), Number(closing - opening), Number(closing)],
                    SemanticRole: "grand_total"));
            }
            else
            {
                var subtotal = groups.SingleOrDefault(g => g.Group == group);
                var amount = trial || subtotal is null ? 0 : Presented(kind, subtotal);

                if (kind == AccountingSummaryKind.BalanceSheet && group == 3)
                    amount -= groups.Where(g => g.Group >= 4).Sum(g => g.Closing);

                rows.Add(new(
                    ReportRowKind.Total,
                    trial 
                        ? [Label("Total"), Number(subtotal?.Debit ?? 0), Number(subtotal?.Credit ?? 0)]
                        : [Label("Total"), Number(amount)],
                    SemanticRole: "grand_total"));
            }
        }

        ReportSheetColumnDto[] columns = trial
            ? [
                new("account", "Account", "string"),
                new("debit_amount", "Debit", "decimal"),
                new("credit_amount", "Credit", "decimal")]
            : equity
                ? [
                    new("component", "Component", "string"),
                    new("opening", "Opening", "decimal"),
                    new("change", "Change", "decimal"),
                    new("closing", "Closing", "decimal")]
                : [
                    new("account", "Account", "string"),
                    new("amount", "Amount", "decimal")];

        var sheet = new ReportSheetDto(
            columns, rows,
            new(
                Title: definition.Name,
                Subtitle: kind == AccountingSummaryKind.BalanceSheet
                    ? $"As of {rawTo:yyyy-MM-dd}"
                    : $"{rawFrom:yyyy-MM-dd} → {rawTo:yyyy-MM-dd}"));

        return new(
            sheet,
            0,
            limit,
            null,
            more,
            next,
            new Dictionary<string, string> { ["paging"] = "query", ["executor"] = "accounting-summary" });

        ReportSheetRowDto Detail(AccountingSummaryValue value)
        {
            var label = Label(value.Id == Guid.Empty && equity
                    ? value.Name 
                    : ReportDisplayHelpers.BuildAccountDisplay(value.Code, value.Name)) with
                        { 
                            Action = value.Id == Guid.Empty 
                                ? null
                                : ReportCellActions.BuildAccountCardAction(value.Id, rawFrom, rawTo, request.Filters)
                        };

            return new(ReportRowKind.Detail, trial
                ? [label, Number(value.Debit), Number(value.Credit)]
                : equity
                    ? [label, Number(-value.Opening), Number(value.Opening - value.Closing), Number(-value.Closing)]
                    : [label, Number(Presented(kind, value))]);
        }
    }

    private static bool ValidGroup(AccountingSummaryKind kind, int group) => kind switch
    {
        AccountingSummaryKind.TrialBalance => group is >= 0 and <= 4,
        AccountingSummaryKind.BalanceSheet => group is >= 1 and <= 3,
        AccountingSummaryKind.IncomeStatement => group is >= 4 and <= 8,
        _ => group == 3
    };

    private static decimal Presented(AccountingSummaryKind kind, AccountingSummaryValue value)
    {
        var amount = kind == AccountingSummaryKind.IncomeStatement
            ? value.Debit - value.Credit
            : value.Closing;

        return NormalBalanceDefaults.FromStatementSection((StatementSection)value.Group) == NormalBalance.Debit
            ? amount
            : -amount;
    }

    private static IEnumerable<ReportSheetRowDto> GrandTotals(
        AccountingSummaryKind kind,
        IReadOnlyList<AccountingSummaryValue> groups,
        DateOnly from,
        DateOnly to)
    {
        if (kind == AccountingSummaryKind.TrialBalance)
        {
            return [TrialBalanceCanonicalReportExecutor.ToTotalRow(new(
                0, 
                groups.Sum(g => g.Debit),
                groups.Sum(g => g.Credit),
                0))];
        }

        decimal Total(int group)
            => groups
                .Where(g => g.Group == group)
                .Sum(g => Presented(kind, g));
        
        if (kind == AccountingSummaryKind.IncomeStatement)
            return IncomeStatementCanonicalReportExecutor.ToGrandTotalRows(new IncomeStatementReport
            {
                FromInclusive = from, ToInclusive = to, Sections = [], TotalIncome = Total(4) + Total(7), TotalExpenses = Total(5) + Total(6) + Total(8),
                TotalOtherIncome = Total(7), TotalOtherExpense = Total(8), NetIncome = Total(4) + Total(7) - Total(5) - Total(6) - Total(8)
            });

        var equity = Total(3)
            - groups
                .Where(g => g.Group >= 4)
                .Sum(g => g.Closing);

        return BalanceSheetCanonicalReportExecutor.ToGrandTotalRows(new BalanceSheetReport
        {
            TotalAssets = Total(1), TotalLiabilities = Total(2), TotalEquity = equity,
            TotalLiabilitiesAndEquity = Total(2) + equity, Difference = Total(1) - Total(2) - equity,
            IsBalanced = Total(1) == Total(2) + equity
        });
    }
    
    private static ReportCellDto Label(string value)
        => new(JsonSerializer.SerializeToElement(value), value, "string");

    private static ReportCellDto Number(decimal value)
        => new(JsonSerializer.SerializeToElement(value), value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), "decimal");
}

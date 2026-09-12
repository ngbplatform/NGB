using System.Runtime.CompilerServices;
using System.Text.Json;
using NGB.Accounting.Accounts;
using NGB.Accounting.Reports.BalanceSheet;
using NGB.Accounting.Reports.IncomeStatement;
using NGB.Accounting.Reports.StatementOfChangesInEquity;
using NGB.Contracts.Reporting;
using NGB.Core.Reporting;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Reporting.Canonical;
using NGB.Runtime.Reporting.Internal;

namespace NGB.Runtime.Reporting.Runs;

public abstract class FinancialStatementStreamingExecutor(IAccountingStatementAccountReader reader)
    : IStreamingReportExecutor
{
    public abstract string ReportCode { get; }
    protected abstract string ExecutorName { get; }
    
    protected sealed record Input(
        ReportDefinitionDto Definition,
        ReportExecutionRequestDto Request,
        DateOnly RawFrom,
        DateOnly RawTo,
        DateOnly From,
        DateOnly To);

    public string Prepare(ReportDefinitionDto definition, ReportExecutionRequestDto request)
    {
        if (ReportCode == AccountingReportCodes.BalanceSheet)
        {
            var raw = CanonicalReportExecutionHelper.GetRequiredDateOnlyParameter(definition, request, "as_of_utc");
            var month = new DateOnly(raw.Year, raw.Month, 1);
            return JsonSerializer.Serialize(new Input(definition, request, month, raw, month, month));
        }

        var (rawFrom, rawTo, from, to) = CanonicalReportExecutionHelper.GetRequiredDateRange(definition, request);
        return JsonSerializer.Serialize(new Input(definition, request, rawFrom, rawTo, from, to));
    }

    public ReportSheetDto Template(string preparedJson)
    {
        var input = JsonSerializer.Deserialize<Input>(preparedJson)!;
        var equity = ReportCode == AccountingReportCodes.StatementOfChangesInEquity;
        ReportSheetColumnDto[] columns = equity
            ? [
                new("component", "Component", "string", Width: 360, IsFrozen: true),
                new("opening", "Opening", "decimal", Width: 140),
                new("change", "Change", "decimal", Width: 140),
                new("closing", "Closing", "decimal", Width: 140)
            ]
            : [
                new("account", "Account", "string", Width: 360, IsFrozen: true),
                new("amount", "Amount", "decimal", Width: 140)
            ];

        return new(
            columns, [],
            new(
                Title: input.Definition.Name,
                Subtitle: ReportCode == AccountingReportCodes.BalanceSheet
                    ? $"As of {input.RawTo:yyyy-MM-dd}"
                    : $"{input.RawFrom:yyyy-MM-dd} → {input.RawTo:yyyy-MM-dd}",
                HasRowOutline: !equity,
                Diagnostics: new Dictionary<string, string> { ["executor"] = ExecutorName }));
    }

    public async IAsyncEnumerable<ReportRowWrite> ReadAsync(
        string preparedJson,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var input = JsonSerializer.Deserialize<Input>(preparedJson)!;
        var accounts = reader.ReadAsync(
            input.From,
            input.To,
            CanonicalReportExecutionHelper.BuildDimensionScopes(input.Definition, input.Request), ct);

        var ordinal = 0;
        await foreach (var row in RenderAsync(input, accounts, ct))
        {
            yield return new(ordinal++, row);
        }
    }

    protected abstract IAsyncEnumerable<ReportSheetRowDto> RenderAsync(
        Input input,
        IAsyncEnumerable<AccountingStatementAccount> accounts,
        CancellationToken ct);

    protected static ReportCellDto Label(string text)
        => new(JsonSerializer.SerializeToElement(text), text, "string");

    protected static ReportCellDto Amount(
        decimal amount,
        string? role = null)
        => new(JsonSerializer.SerializeToElement(amount), amount.ToString("0.##"), "decimal", SemanticRole: role);

    protected static ReportSheetRowDto Group(StatementSection section, string title)
        => new(
            ReportRowKind.Group,
            [Label(title), new(Value: null, Display: "", ValueType: "decimal")],
            GroupKey: section.ToString(),
            SemanticRole: "section");

    protected static ReportSheetRowDto Subtotal(string title, decimal amount)
        => new(
            ReportRowKind.Subtotal,
            [Label(title + " total") with { SemanticRole = "label" }, Amount(amount, "subtotal")],
            SemanticRole: "section_total");

    protected static ReportSheetRowDto Detail(Input input, Guid id, string code, string name, decimal amount)
        => new(
            ReportRowKind.Detail,
            [
                Label(ReportDisplayHelpers.BuildAccountDisplay(code, name)) with
                    {
                        Action = id == Guid.Empty
                            ? null
                            : ReportCellActions.BuildAccountCardAction(id, input.RawFrom, input.RawTo, input.Request.Filters)
                    },
                Amount(amount)],
            OutlineLevel: 1,
            GroupKey: $"detail:{id}");

    protected static decimal Presented(StatementSection section, decimal signed)
        => NormalBalanceDefaults.FromStatementSection(section) == NormalBalance.Debit ? signed : -signed;
}

public sealed class BalanceSheetStreamingExecutor(IAccountingStatementAccountReader reader)
    : FinancialStatementStreamingExecutor(reader)
{
    public override string ReportCode => AccountingReportCodes.BalanceSheet;
    protected override string ExecutorName => "canonical-balance-sheet";

    protected override async IAsyncEnumerable<ReportSheetRowDto> RenderAsync(
        Input input,
        IAsyncEnumerable<AccountingStatementAccount> accounts,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await using var rows = accounts.GetAsyncEnumerator(ct);

        var has = await rows.MoveNextAsync();
        var totals = new decimal[4];

        for (var section = StatementSection.Assets; section <= StatementSection.Equity; section++)
        {
            yield return Group(section, section.ToString());

            while (has && rows.Current.Section == section)
            {
                var row = rows.Current;
                var value = Presented(section, row.Closing);

                if (value != 0)
                {
                    totals[(int)section] += value;
                    yield return Detail(input, row.AccountId, row.Code, row.Name, value);
                }

                has = await rows.MoveNextAsync();
            }

            if (section == StatementSection.Equity)
            {
                decimal earnings = 0;
                while (has)
                {
                    // Net income equals the credit-side net of all P&L balances.
                    earnings -= rows.Current.Closing;
                    has = await rows.MoveNextAsync();
                }

                if (earnings != 0)
                {
                    totals[3] += earnings;
                    yield return Detail(input, Guid.Empty, "NET", "Net Income", earnings);
                }
            }

            if (input.Request.Layout?.ShowSubtotals != false)
                yield return Subtotal(section.ToString(), totals[(int)section]);
        }

        if (input.Request.Layout?.ShowGrandTotals != false)
        {
            
            foreach (var row in BalanceSheetCanonicalReportExecutor.ToGrandTotalRows(new BalanceSheetReport
                {
                    AsOfPeriod = input.To,
                    TotalAssets = totals[1],
                    TotalLiabilities = totals[2],
                    TotalEquity = totals[3],
                    TotalLiabilitiesAndEquity = totals[2] + totals[3],
                    Difference = totals[1] - totals[2] - totals[3],
                    IsBalanced = totals[1] == totals[2] + totals[3]
                }))
            {
                yield return row;
            }
        }
    }
}

public sealed class IncomeStatementStreamingExecutor(IAccountingStatementAccountReader reader)
    : FinancialStatementStreamingExecutor(reader)
{
    public override string ReportCode => AccountingReportCodes.IncomeStatement;
    protected override string ExecutorName => "canonical-income-statement";
    
    protected override async IAsyncEnumerable<ReportSheetRowDto> RenderAsync(
        Input input, IAsyncEnumerable<AccountingStatementAccount> accounts,
        [EnumeratorCancellation] CancellationToken ct)
    {
        StatementSection? current = null;
        var totals = new decimal[9];

        await foreach (var account in accounts.WithCancellation(ct))
        {
            if (account.Section < StatementSection.Income)
                continue;

            var amount = Presented(account.Section, account.Debit - account.Credit);
            if (amount == 0)
                continue;

            if (current != account.Section)
            {
                if (current is { } previous && input.Request.Layout?.ShowSubtotals != false)
                    yield return Subtotal(IncomeStatementCanonicalReportExecutor.HumanizeSection(previous), totals[(int)previous]);

                current = account.Section;
                yield return Group(account.Section, IncomeStatementCanonicalReportExecutor.HumanizeSection(account.Section));
            }
            
            totals[(int)account.Section] += amount;
            yield return Detail(input, account.AccountId, account.Code, account.Name, amount);
        }

        if (current is { } last && input.Request.Layout?.ShowSubtotals != false)
            yield return Subtotal(IncomeStatementCanonicalReportExecutor.HumanizeSection(last), totals[(int)last]);

        if (input.Request.Layout?.ShowGrandTotals != false)
            foreach (var row in IncomeStatementCanonicalReportExecutor.ToGrandTotalRows(new IncomeStatementReport
                {
                    FromInclusive = input.From,
                    ToInclusive = input.To,
                    Sections = [],
                    TotalIncome = totals[4] + totals[7],
                    TotalExpenses = totals[5] + totals[6] + totals[8],
                    TotalOtherIncome = totals[7],
                    TotalOtherExpense = totals[8],
                    NetIncome = totals[4] + totals[7] - totals[5] - totals[6] - totals[8]
                }))
            {
                yield return row;
            }
    }
}

public sealed class EquityStatementStreamingExecutor(IAccountingStatementAccountReader reader)
    : FinancialStatementStreamingExecutor(reader)
{
    public override string ReportCode => AccountingReportCodes.StatementOfChangesInEquity;
    protected override string ExecutorName => "canonical-statement-of-changes-in-equity";
    
    protected override async IAsyncEnumerable<ReportSheetRowDto> RenderAsync(
        Input input,
        IAsyncEnumerable<AccountingStatementAccount> accounts,
        [EnumeratorCancellation] CancellationToken ct)
    {
        decimal opening = 0, closing = 0, earningsOpening = 0, earningsClosing = 0;

        await foreach (var account in accounts.WithCancellation(ct))
        {
            if (account.Section >= StatementSection.Income)
            {
                earningsOpening -= account.Opening;
                earningsClosing -= account.Closing;
                continue;
            }

            if (account.Section != StatementSection.Equity || account is { Opening: 0, Closing: 0 })
                continue;

            opening -= account.Opening;
            closing -= account.Closing;

            yield return Row(account.AccountId, account.Code, account.Name, false, -account.Opening, -account.Closing);
        }

        if (earningsOpening != 0 || earningsClosing != 0)
            yield return Row(Guid.Empty, "CURR_EARNINGS", "Current Earnings (Unclosed)", true, earningsOpening, earningsClosing);

        opening += earningsOpening;
        closing += earningsClosing;

        if (input.Request.Layout?.ShowGrandTotals != false)
        {
            
            yield return new(
                ReportRowKind.Total,
                [
                    Label("Total Equity") with { SemanticRole = "label" },
                    Amount(opening, "total"), Amount(closing - opening, "total"), Amount(closing, "total")
                ],
                SemanticRole: "grand_total");
        }
        ReportSheetRowDto Row(Guid id, string code, string name, bool synthetic, decimal start, decimal end)
            => StatementOfChangesInEquityCanonicalReportExecutor.ToDetailRow(new StatementOfChangesInEquityLine
                {
                    AccountId = id,
                    ComponentCode = code,
                    ComponentName = name,
                    IsSynthetic = synthetic,
                    OpeningAmount = start,
                    ChangeAmount = end - start,
                    ClosingAmount = end
                },
                input.RawFrom,
                input.RawTo,
                input.Request.Filters);
    }
}

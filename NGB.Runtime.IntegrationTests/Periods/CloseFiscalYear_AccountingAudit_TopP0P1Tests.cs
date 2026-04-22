using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.Reports.TrialBalance;
using NGB.Core.Dimensions;
using NGB.Persistence.Dimensions;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.Periods;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

/// <summary>
/// Top P0/P1 accounting-audit tests for CloseFiscalYear.
/// Focus: US-style semantics (profit/loss -> retained earnings), sign correctness, P&L section coverage,
/// balance-sheet isolation, contra behavior, dates, and dimension policy.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CloseFiscalYear_AccountingAudit_TopP0P1_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateOnly EndPeriod = new(2026, 1, 1); // month start
    private static readonly DateTime ActivityDayUtc = new(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task P0_LossScenario_DebitsRetainedEarnings_AndZerosAllPLBalances()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var retainedEarningsId = await SeedCoaAsync(host,
            includePayable: false,
            includeExtraSections: false,
            includeContraIncome: false,
            includeZeroNetExpense: false);

        // Net loss: Expense 120, Revenue 100 => P&L sum = +20 (debit side dominates).
        await PostAsync(host, Guid.CreateVersion7(), ActivityDayUtc, debitCode: "50", creditCode: "90.1", amount: 100m);
        await PostAsync(host, Guid.CreateVersion7(), ActivityDayUtc, debitCode: "91", creditCode: "50", amount: 120m);

        await CloseFiscalYearAsync(host, retainedEarningsId);

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        var tb = sp.GetRequiredService<ITrialBalanceReader>();

        var closeDocId = DeterministicGuid.Create($"CloseFiscalYear|{EndPeriod:yyyy-MM-dd}");
        var entries = await entryReader.GetByDocumentAsync(closeDocId, CancellationToken.None);

        // We expect two closing entries: one for revenue, one for expense.
        entries.Should().HaveCount(2);

        // Loss must debit retained earnings at least once.
        entries.Should().Contain(e => e.Debit.Code == "300");

        var rows = await tb.GetAsync(EndPeriod, EndPeriod, CancellationToken.None);
        rows.Single(r => r.AccountCode == "90.1").ClosingBalance.Should().Be(0m);
        rows.Single(r => r.AccountCode == "91").ClosingBalance.Should().Be(0m);

        // Retained earnings becomes a net debit balance (+20) due to loss.
        rows.Single(r => r.AccountCode == "300").ClosingBalance.Should().Be(20m);
    }

    [Fact]
    public async Task P0_ClosesAllProfitAndLossSections_Income_Cogs_OtherIncome_OtherExpense()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var retainedEarningsId = await SeedCoaAsync(host,
            includePayable: false,
            includeExtraSections: true,
            includeContraIncome: false,
            includeZeroNetExpense: false);

        // Income: 100 (credit-normal => closingBalance -100)
        await PostAsync(host, Guid.CreateVersion7(), ActivityDayUtc, debitCode: "50", creditCode: "90.1", amount: 100m);

        // COGS: 30 (debit-normal => closingBalance +30)
        await PostAsync(host, Guid.CreateVersion7(), ActivityDayUtc, debitCode: "90.2", creditCode: "50", amount: 30m);

        // Other income: 10 (credit-normal => closingBalance -10)
        await PostAsync(host, Guid.CreateVersion7(), ActivityDayUtc, debitCode: "50", creditCode: "92", amount: 10m);

        // Other expense: 5 (debit-normal => closingBalance +5)
        await PostAsync(host, Guid.CreateVersion7(), ActivityDayUtc, debitCode: "93", creditCode: "50", amount: 5m);

        // P&L sum = -100 + 30 - 10 + 5 = -75 => profit 75 => retained earnings closingBalance = -75.
        await CloseFiscalYearAsync(host, retainedEarningsId);

        await using var scope = host.Services.CreateAsyncScope();
        var tb = scope.ServiceProvider.GetRequiredService<ITrialBalanceReader>();
        var rows = await tb.GetAsync(EndPeriod, EndPeriod, CancellationToken.None);

        rows.Single(r => r.AccountCode == "90.1").ClosingBalance.Should().Be(0m);
        rows.Single(r => r.AccountCode == "90.2").ClosingBalance.Should().Be(0m);
        rows.Single(r => r.AccountCode == "92").ClosingBalance.Should().Be(0m);
        rows.Single(r => r.AccountCode == "93").ClosingBalance.Should().Be(0m);

        rows.Single(r => r.AccountCode == "300").ClosingBalance.Should().Be(-75m);
    }

    [Fact]
    public async Task P0_DoesNotTouchBalanceSheetAccounts_AndDoesNotReferenceThemInClosingEntries()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var retainedEarningsId = await SeedCoaAsync(host,
            includePayable: true,
            includeExtraSections: false,
            includeContraIncome: false,
            includeZeroNetExpense: false);

        // Create a liability balance. Payable gets credit balance.
        await PostAsync(host, Guid.CreateVersion7(), ActivityDayUtc, debitCode: "50", creditCode: "60", amount: 50m);

        // Create P&L activity so FY close posts entries.
        await PostAsync(host, Guid.CreateVersion7(), ActivityDayUtc, debitCode: "50", creditCode: "90.1", amount: 10m);

        await CloseFiscalYearAsync(host, retainedEarningsId);

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var tb = sp.GetRequiredService<ITrialBalanceReader>();
        var rows = await tb.GetAsync(EndPeriod, EndPeriod, CancellationToken.None);
        rows.Single(r => r.AccountCode == "60").ClosingBalance.Should().Be(-50m, "FY close must not affect balance sheet accounts");

        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        var closeDocId = DeterministicGuid.Create($"CloseFiscalYear|{EndPeriod:yyyy-MM-dd}");
        var entries = await entryReader.GetByDocumentAsync(closeDocId, CancellationToken.None);

        entries.Should().NotBeEmpty();
        entries.Should().NotContain(e => e.Debit.Code == "60" || e.Credit.Code == "60");
        entries.Should().NotContain(e => e.Debit.Code == "50" || e.Credit.Code == "50",
            "closing entries should involve only P&L accounts and retained earnings");
    }

    [Fact]
    public async Task P0_ContraProfitAndLoss_UsesSignedClosingBalance_NotNormalBalance()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var retainedEarningsId = await SeedCoaAsync(host,
            includePayable: false,
            includeExtraSections: false,
            includeContraIncome: true,
            includeZeroNetExpense: false);

        // Contra income (normal balance is Debit), but we intentionally create a CREDIT balance.
        // Correct behavior: CloseFiscalYear must look at signed ClosingBalance (negative),
        // therefore it must DEBIT the contra income and CREDIT retained earnings.
        await PostAsync(host, Guid.CreateVersion7(), ActivityDayUtc, debitCode: "50", creditCode: "90.15", amount: 20m);

        await CloseFiscalYearAsync(host, retainedEarningsId);

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        var closeDocId = DeterministicGuid.Create($"CloseFiscalYear|{EndPeriod:yyyy-MM-dd}");
        var entries = await entryReader.GetByDocumentAsync(closeDocId, CancellationToken.None);

        entries.Should().ContainSingle(e => e.Debit.Code == "90.15" && e.Credit.Code == "300" && e.Amount == 20m);

        var tb = sp.GetRequiredService<ITrialBalanceReader>();
        var rows = await tb.GetAsync(EndPeriod, EndPeriod, CancellationToken.None);
        rows.Single(r => r.AccountCode == "90.15").ClosingBalance.Should().Be(0m);
    }

    [Fact]
    public async Task P0_RetainedEarningsClosingBalance_EqualsSumOfPLClosingBalancesBeforeClose_AndEqualsEntryImpact()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var retainedEarningsId = await SeedCoaAsync(host,
            includePayable: false,
            includeExtraSections: false,
            includeContraIncome: false,
            includeZeroNetExpense: false);

        // Revenue 250, Expense 40, Expense 15 => P&L sum = -250 + 55 = -195.
        await PostAsync(host, Guid.CreateVersion7(), ActivityDayUtc, debitCode: "50", creditCode: "90.1", amount: 250m);
        await PostAsync(host, Guid.CreateVersion7(), ActivityDayUtc, debitCode: "91", creditCode: "50", amount: 40m);
        await PostAsync(host, Guid.CreateVersion7(), ActivityDayUtc, debitCode: "91", creditCode: "50", amount: 15m);

        decimal plSumBefore;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var tb = scope.ServiceProvider.GetRequiredService<ITrialBalanceReader>();
            var rows = await tb.GetAsync(EndPeriod, EndPeriod, CancellationToken.None);
            plSumBefore = SumClosingBalances(rows, new[] { "90.1", "91" });
            plSumBefore.Should().Be(-195m);
        }

        await CloseFiscalYearAsync(host, retainedEarningsId);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var tb = sp.GetRequiredService<ITrialBalanceReader>();
            var rowsAfter = await tb.GetAsync(EndPeriod, EndPeriod, CancellationToken.None);

            // P&L is zeroed.
            rowsAfter.Single(r => r.AccountCode == "90.1").ClosingBalance.Should().Be(0m);
            rowsAfter.Single(r => r.AccountCode == "91").ClosingBalance.Should().Be(0m);

            // Retained earnings absorbs the prior P&L sum.
            rowsAfter.Single(r => r.AccountCode == "300").ClosingBalance.Should().Be(plSumBefore);

            // And that retained balance is exactly the cumulative impact of closing entries against retained earnings.
            var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
            var closeDocId = DeterministicGuid.Create($"CloseFiscalYear|{EndPeriod:yyyy-MM-dd}");
            var entries = await entryReader.GetByDocumentAsync(closeDocId, CancellationToken.None);

            var retainedImpact = entries.Sum(e =>
                (e.Debit.Code == "300" ? e.Amount : 0m) +
                (e.Credit.Code == "300" ? -e.Amount : 0m));

            retainedImpact.Should().Be(plSumBefore);
        }
    }

    private static async Task<Guid> SeedCoaAsync(
        IHost host,
        bool includePayable,
        bool includeExtraSections,
        bool includeContraIncome,
        bool includeZeroNetExpense)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        if (includePayable)
        {
            await accounts.CreateAsync(new CreateAccountRequest(
                Code: "60",
                Name: "Accounts Payable",
                Type: AccountType.Liability,
                StatementSection: StatementSection.Liabilities,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow
            ), CancellationToken.None);
        }

        var retainedEarningsId = await accounts.CreateAsync(new CreateAccountRequest(
            Code: "300",
            Name: "Retained Earnings",
            Type: AccountType.Equity,
            StatementSection: StatementSection.Equity,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Revenue",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "91",
            Name: "Expenses",
            Type: AccountType.Expense,
            StatementSection: StatementSection.Expenses,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        if (includeZeroNetExpense)
        {
            await accounts.CreateAsync(new CreateAccountRequest(
                Code: "91.1",
                Name: "Misc Expense",
                Type: AccountType.Expense,
                StatementSection: StatementSection.Expenses,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow
            ), CancellationToken.None);
        }

        if (includeExtraSections)
        {
            await accounts.CreateAsync(new CreateAccountRequest(
                Code: "90.2",
                Name: "COGS",
                Type: AccountType.Expense,
                StatementSection: StatementSection.CostOfGoodsSold,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow
            ), CancellationToken.None);

            await accounts.CreateAsync(new CreateAccountRequest(
                Code: "92",
                Name: "Other Income",
                Type: AccountType.Income,
                StatementSection: StatementSection.OtherIncome,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow
            ), CancellationToken.None);

            await accounts.CreateAsync(new CreateAccountRequest(
                Code: "93",
                Name: "Other Expense",
                Type: AccountType.Expense,
                StatementSection: StatementSection.OtherExpense,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow
            ), CancellationToken.None);
        }

        if (includeContraIncome)
        {
            await accounts.CreateAsync(new CreateAccountRequest(
                Code: "90.15",
                Name: "Contra Income",
                Type: AccountType.Income,
                StatementSection: StatementSection.Income,
                IsContra: true,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow
            ), CancellationToken.None);
        }

        return retainedEarningsId;
    }

    private static async Task PostAsync(
        IHost host,
        Guid documentId,
        DateTime periodUtc,
        string debitCode,
        string creditCode,
        decimal amount,
        DimensionBag? debitDimensions = null,
        DimensionBag? creditDimensions = null)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            PostingOperation.Post,
            async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(
                    documentId,
                    periodUtc,
                    chart.Get(debitCode),
                    chart.Get(creditCode),
                    amount,
                    debitDimensions: debitDimensions ?? DimensionBag.Empty,
                    creditDimensions: creditDimensions ?? DimensionBag.Empty);
            },
            CancellationToken.None);
    }

    private static async Task CloseFiscalYearAsync(IHost host, Guid retainedEarningsAccountId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();

        await closing.CloseFiscalYearAsync(
            fiscalYearEndPeriod: EndPeriod,
            retainedEarningsAccountId: retainedEarningsAccountId,
            closedBy: "tests",
            ct: CancellationToken.None);
    }

    private static decimal SumClosingBalances(IReadOnlyList<TrialBalanceRow> rows, IReadOnlyList<string> codes)
    {
        var set = new HashSet<string>(codes);
        return rows.Where(r => set.Contains(r.AccountCode)).Sum(r => r.ClosingBalance);
    }
}

[Collection(PostgresCollection.Name)]
public sealed class CloseFiscalYear_AccountingAudit_TopP0P1_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateOnly EndPeriod = new(2026, 1, 1);
    private static readonly DateTime ActivityDayUtc = new(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ExpectedClosingDayUtc = new(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task P1_DoesNotPostForZeroNetPLAccount_WhenOtherAccountsNeedClosing()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var retainedEarningsId = await SeedCoaAsync(host, includeZeroNetExpense: true);

        // Revenue non-zero.
        await PostAsync(host, Guid.CreateVersion7(), ActivityDayUtc, debitCode: "50", creditCode: "90.1", amount: 50m);

        // Expense account nets to zero.
        await PostAsync(host, Guid.CreateVersion7(), ActivityDayUtc, debitCode: "91.1", creditCode: "50", amount: 10m);
        await PostAsync(host, Guid.CreateVersion7(), ActivityDayUtc, debitCode: "50", creditCode: "91.1", amount: 10m);

        await CloseFiscalYearAsync(host, retainedEarningsId);

        await using var scope = host.Services.CreateAsyncScope();
        var entryReader = scope.ServiceProvider.GetRequiredService<IAccountingEntryReader>();

        var closeDocId = DeterministicGuid.Create($"CloseFiscalYear|{EndPeriod:yyyy-MM-dd}");
        var entries = await entryReader.GetByDocumentAsync(closeDocId, CancellationToken.None);

        entries.Should().HaveCount(1);
        entries.Single().Debit.Code.Should().Be("90.1");
        entries.Single().Credit.Code.Should().Be("300");
        entries.Should().NotContain(e => e.Debit.Code == "91.1" || e.Credit.Code == "91.1");
    }

    [Fact]
    public async Task P1_AllClosingEntriesUseClosingDayUtc_LastDayOfMonth_AtMidnight()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var retainedEarningsId = await SeedCoaAsync(host, includeZeroNetExpense: false);

        await PostAsync(host, Guid.CreateVersion7(), ActivityDayUtc, debitCode: "50", creditCode: "90.1", amount: 10m);
        await PostAsync(host, Guid.CreateVersion7(), ActivityDayUtc, debitCode: "91", creditCode: "50", amount: 3m);

        await CloseFiscalYearAsync(host, retainedEarningsId);

        await using var scope = host.Services.CreateAsyncScope();
        var entryReader = scope.ServiceProvider.GetRequiredService<IAccountingEntryReader>();
        var closeDocId = DeterministicGuid.Create($"CloseFiscalYear|{EndPeriod:yyyy-MM-dd}");
        var entries = await entryReader.GetByDocumentAsync(closeDocId, CancellationToken.None);

        entries.Should().NotBeEmpty();
        entries.All(e => e.Period == ExpectedClosingDayUtc).Should().BeTrue(
            "all FY closing entries must be posted on the computed closing day");
        ExpectedClosingDayUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task P1_RetainedEarningsNeverCarriesDimensions_OnEitherSide_ForAllClosingEntries()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var retainedEarningsId = await SeedCoaWithDimensionedPlAccountsAsync(host);

        var dimBuilding = DeterministicGuid.Create("Dimension|buildings");
        var dimCounterparty = DeterministicGuid.Create("Dimension|counterparties");

        var buildingId = Guid.CreateVersion7();
        var cpId = Guid.CreateVersion7();

        var bag = new DimensionBag(new[]
        {
            new DimensionValue(dimBuilding, buildingId),
            new DimensionValue(dimCounterparty, cpId)
        });

        // Revenue with dims on credit.
        await PostAsync(host, Guid.CreateVersion7(), ActivityDayUtc,
            debitCode: "50",
            creditCode: "90.11",
            amount: 100m,
            debitDimensions: DimensionBag.Empty,
            creditDimensions: bag);

        // Expense with dims on debit.
        await PostAsync(host, Guid.CreateVersion7(), ActivityDayUtc,
            debitCode: "91.11",
            creditCode: "50",
            amount: 40m,
            debitDimensions: bag,
            creditDimensions: DimensionBag.Empty);

        await CloseFiscalYearAsync(host, retainedEarningsId);

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        var dimReader = sp.GetRequiredService<IDimensionSetReader>();

        var closeDocId = DeterministicGuid.Create($"CloseFiscalYear|{EndPeriod:yyyy-MM-dd}");
        var entries = await entryReader.GetByDocumentAsync(closeDocId, CancellationToken.None);

        entries.Should().HaveCount(2);

        // Retained earnings dimension set id must always be empty.
        foreach (var e in entries)
        {
            if (e.Debit.Code == "300")
                e.DebitDimensionSetId.Should().Be(Guid.Empty);

            if (e.Credit.Code == "300")
                e.CreditDimensionSetId.Should().Be(Guid.Empty);
        }

        // And the P&L side should preserve dimensions (non-empty set).
        var plSetIds = entries.Select(e =>
                e.Debit.Code != "300" ? e.DebitDimensionSetId : e.CreditDimensionSetId)
            .ToList();

        plSetIds.Should().NotContain(Guid.Empty);

        var bags = await dimReader.GetBagsByIdsAsync(plSetIds, CancellationToken.None);
        bags.Values.All(b => b.Items.Any(x => x.DimensionId == dimBuilding && x.ValueId == buildingId)
                             && b.Items.Any(x => x.DimensionId == dimCounterparty && x.ValueId == cpId))
            .Should().BeTrue();
    }

    [Fact]
    public async Task P1_CloseFiscalYear_DoesNotMarkEndMonthAsClosed()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var retainedEarningsId = await SeedCoaAsync(host, includeZeroNetExpense: false);

        // Ensure there is P&L activity.
        await PostAsync(host, Guid.CreateVersion7(), ActivityDayUtc, debitCode: "50", creditCode: "90.1", amount: 10m);

        await CloseFiscalYearAsync(host, retainedEarningsId);

        await using var scope = host.Services.CreateAsyncScope();
        var closedReader = scope.ServiceProvider.GetRequiredService<IClosedPeriodReader>();
        var closed = await closedReader.GetClosedAsync(EndPeriod, EndPeriod, CancellationToken.None);

        closed.Should().BeEmpty("CloseFiscalYear must not implicitly CloseMonth for the end period");
    }

    [Fact]
    public async Task P1_CloseFiscalYear_DoesNotWriteMonthlyBalances_ForEndMonth()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var retainedEarningsId = await SeedCoaAsync(host, includeZeroNetExpense: false);

        // Ensure there is P&L activity.
        await PostAsync(host, Guid.CreateVersion7(), ActivityDayUtc, debitCode: "50", creditCode: "90.1", amount: 10m);

        await CloseFiscalYearAsync(host, retainedEarningsId);

        await using var scope = host.Services.CreateAsyncScope();
        var balances = scope.ServiceProvider.GetRequiredService<IAccountingBalanceReader>();
        (await balances.GetForPeriodAsync(EndPeriod, CancellationToken.None))
            .Should()
            .BeEmpty("monthly balances are produced by CloseMonth, not by CloseFiscalYear");
    }

    private static async Task<Guid> SeedCoaAsync(IHost host, bool includeZeroNetExpense)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        var retainedEarningsId = await accounts.CreateAsync(new CreateAccountRequest(
            Code: "300",
            Name: "Retained Earnings",
            Type: AccountType.Equity,
            StatementSection: StatementSection.Equity,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Revenue",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "91",
            Name: "Expenses",
            Type: AccountType.Expense,
            StatementSection: StatementSection.Expenses,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        if (includeZeroNetExpense)
        {
            await accounts.CreateAsync(new CreateAccountRequest(
                Code: "91.1",
                Name: "Misc Expense",
                Type: AccountType.Expense,
                StatementSection: StatementSection.Expenses,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow
            ), CancellationToken.None);
        }

        return retainedEarningsId;
    }

    private static async Task<Guid> SeedCoaWithDimensionedPlAccountsAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        var retainedEarningsId = await accounts.CreateAsync(new CreateAccountRequest(
            Code: "300",
            Name: "Retained Earnings",
            Type: AccountType.Equity,
            StatementSection: StatementSection.Equity,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        // Dimensioned revenue (credit side).
        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "90.11",
            Name: "Revenue (Dimensioned)",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow,
            DimensionRules: new[]
            {
                new AccountDimensionRuleRequest("Buildings", IsRequired: true, Ordinal: 10),
                new AccountDimensionRuleRequest("Counterparties", IsRequired: true, Ordinal: 20)
            }
        ), CancellationToken.None);

        // Dimensioned expense (debit side).
        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "91.11",
            Name: "Expenses (Dimensioned)",
            Type: AccountType.Expense,
            StatementSection: StatementSection.Expenses,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow,
            DimensionRules: new[]
            {
                new AccountDimensionRuleRequest("Buildings", IsRequired: true, Ordinal: 10),
                new AccountDimensionRuleRequest("Counterparties", IsRequired: true, Ordinal: 20)
            }
        ), CancellationToken.None);

        return retainedEarningsId;
    }

    private static async Task PostAsync(
        IHost host,
        Guid documentId,
        DateTime periodUtc,
        string debitCode,
        string creditCode,
        decimal amount,
        DimensionBag? debitDimensions = null,
        DimensionBag? creditDimensions = null)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            PostingOperation.Post,
            async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(
                    documentId,
                    periodUtc,
                    chart.Get(debitCode),
                    chart.Get(creditCode),
                    amount,
                    debitDimensions: debitDimensions ?? DimensionBag.Empty,
                    creditDimensions: creditDimensions ?? DimensionBag.Empty);
            },
            CancellationToken.None);
    }

    private static async Task CloseFiscalYearAsync(IHost host, Guid retainedEarningsAccountId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();

        await closing.CloseFiscalYearAsync(
            fiscalYearEndPeriod: EndPeriod,
            retainedEarningsAccountId: retainedEarningsAccountId,
            closedBy: "tests",
            ct: CancellationToken.None);
    }
}

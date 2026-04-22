using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.Reports.IncomeStatement;
using NGB.Accounting.Reports.AccountCard;
using NGB.Accounting.Reports.BalanceSheet;
using NGB.Core.Dimensions;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(PostgresCollection.Name)]
public sealed class AccountingReports_DimensionScopes_Semantics_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task AccountCardPaged_ORWithinDimension_ANDAcrossDimensions_Work()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var seeded = await SeedAccountWithKnownDimensionsAsync(host);
        await SeedThreeCashDebitsAsync(host, seeded);

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IAccountCardEffectivePagedReportReader>();

        var all = await reader.GetPageAsync(new AccountCardReportPageRequest
        {
            AccountId = seeded.cashId,
            FromInclusive = new DateOnly(2026, 1, 1),
            ToInclusive = new DateOnly(2026, 1, 1),
            DimensionScopes = null,
            PageSize = 50
        }, CancellationToken.None);

        all.Lines.Should().HaveCount(3);

        var orWithinDimension = await reader.GetPageAsync(new AccountCardReportPageRequest
        {
            AccountId = seeded.cashId,
            FromInclusive = new DateOnly(2026, 1, 1),
            ToInclusive = new DateOnly(2026, 1, 1),
            DimensionScopes = new DimensionScopeBag([
                new DimensionScope(seeded.dim1, [seeded.a1, seeded.a3])
            ]),
            PageSize = 50
        }, CancellationToken.None);

        orWithinDimension.Lines.Should().HaveCount(2);

        var andAcrossDimensions = await reader.GetPageAsync(new AccountCardReportPageRequest
        {
            AccountId = seeded.cashId,
            FromInclusive = new DateOnly(2026, 1, 1),
            ToInclusive = new DateOnly(2026, 1, 1),
            DimensionScopes = new DimensionScopeBag([
                new DimensionScope(seeded.dim1, [seeded.a1, seeded.a2]),
                new DimensionScope(seeded.dim2, [seeded.b2])
            ]),
            PageSize = 50
        }, CancellationToken.None);

        andAcrossDimensions.Lines.Should().HaveCount(1);
        andAcrossDimensions.Lines[0].Dimensions.Items.Should().Contain(new DimensionValue(seeded.dim1, seeded.a2));
        andAcrossDimensions.Lines[0].Dimensions.Items.Should().Contain(new DimensionValue(seeded.dim2, seeded.b2));
    }

    [Fact]
    public async Task GeneralLedgerAggregated_ORWithinDimension_ANDAcrossDimensions_Work()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var seeded = await SeedAccountWithKnownDimensionsAsync(host);
        await SeedThreeCashDebitsAsync(host, seeded);

        await using var scope = host.Services.CreateAsyncScope();
        var all = await ReportingTestHelpers.ReadAllGeneralLedgerAggregatedLinesAsync(
            scope.ServiceProvider,
            seeded.cashId,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 1),
            dimensionScopes: null,
            ct: CancellationToken.None);

        all.Should().HaveCount(3, "each document produces a distinct aggregated line");

        var orWithinDimension = await ReportingTestHelpers.ReadAllGeneralLedgerAggregatedLinesAsync(
            scope.ServiceProvider,
            seeded.cashId,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 1),
            dimensionScopes: new DimensionScopeBag([
                new DimensionScope(seeded.dim1, [seeded.a1, seeded.a3])
            ]),
            ct: CancellationToken.None);

        orWithinDimension.Should().HaveCount(2);

        var andAcrossDimensions = await ReportingTestHelpers.ReadAllGeneralLedgerAggregatedLinesAsync(
            scope.ServiceProvider,
            seeded.cashId,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 1),
            dimensionScopes: new DimensionScopeBag([
                new DimensionScope(seeded.dim1, [seeded.a1, seeded.a2]),
                new DimensionScope(seeded.dim2, [seeded.b2])
            ]),
            ct: CancellationToken.None);

        andAcrossDimensions.Should().HaveCount(1);
        andAcrossDimensions[0].Dimensions.Items.Should().Contain(new DimensionValue(seeded.dim1, seeded.a2));
        andAcrossDimensions[0].Dimensions.Items.Should().Contain(new DimensionValue(seeded.dim2, seeded.b2));
    }

    [Fact]
    public async Task TrialBalance_ORWithinDimension_ANDAcrossDimensions_Work()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var seeded = await SeedAccountWithKnownDimensionsAsync(host);
        await SeedThreeCashDebitsAsync(host, seeded);

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<ITrialBalanceReader>();

        var all = await reader.GetAsync(
            fromInclusive: new DateOnly(2026, 1, 1),
            toInclusive: new DateOnly(2026, 1, 1),
            CancellationToken.None);

        all.Should().HaveCount(4);
        all.Count(x => x.AccountCode == "1012").Should().Be(3);
        all.Count(x => x.AccountCode == "9012").Should().Be(1);

        var orWithinDimension = await reader.GetAsync(
            fromInclusive: new DateOnly(2026, 1, 1),
            toInclusive: new DateOnly(2026, 1, 1),
            dimensionScopes: new DimensionScopeBag([
                new DimensionScope(seeded.dim1, [seeded.a1, seeded.a3])
            ]),
            ct: CancellationToken.None);

        orWithinDimension.Should().HaveCount(2);
        orWithinDimension.Should().OnlyContain(x => x.AccountCode == "1012");

        var andAcrossDimensions = await reader.GetAsync(
            fromInclusive: new DateOnly(2026, 1, 1),
            toInclusive: new DateOnly(2026, 1, 1),
            dimensionScopes: new DimensionScopeBag([
                new DimensionScope(seeded.dim1, [seeded.a1, seeded.a2]),
                new DimensionScope(seeded.dim2, [seeded.b2])
            ]),
            ct: CancellationToken.None);

        andAcrossDimensions.Should().HaveCount(1);
        andAcrossDimensions[0].AccountCode.Should().Be("1012");
        andAcrossDimensions[0].DebitAmount.Should().Be(20m);
        andAcrossDimensions[0].Dimensions.Items.Should().Contain(new DimensionValue(seeded.dim1, seeded.a2));
        andAcrossDimensions[0].Dimensions.Items.Should().Contain(new DimensionValue(seeded.dim2, seeded.b2));
    }

    [Fact]
    public async Task TrialBalance_OpeningFromClosedSnapshot_RespectsDimensionScopes()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var seeded = await SeedAccountWithKnownDimensionsAsync(host);
        await SeedThreeCashDebitsAsync(host, seeded);
        await ReportingTestHelpers.CloseMonthAsync(host, new DateOnly(2026, 1, 1));

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<ITrialBalanceReader>();

        var rows = await reader.GetAsync(
            fromInclusive: new DateOnly(2026, 2, 1),
            toInclusive: new DateOnly(2026, 2, 1),
            dimensionScopes: new DimensionScopeBag([
                new DimensionScope(seeded.dim1, [seeded.a1, seeded.a2]),
                new DimensionScope(seeded.dim2, [seeded.b2])
            ]),
            ct: CancellationToken.None);

        rows.Should().HaveCount(1);
        rows[0].AccountCode.Should().Be("1012");
        rows[0].OpeningBalance.Should().Be(20m);
        rows[0].DebitAmount.Should().Be(0m);
        rows[0].CreditAmount.Should().Be(0m);
        rows[0].ClosingBalance.Should().Be(20m);
        rows[0].Dimensions.Items.Should().Contain(new DimensionValue(seeded.dim1, seeded.a2));
        rows[0].Dimensions.Items.Should().Contain(new DimensionValue(seeded.dim2, seeded.b2));
    }

    [Fact]
    public async Task IncomeStatement_ORWithinDimension_ANDAcrossDimensions_Work()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var seeded = await SeedIncomeStatementAccountsWithKnownDimensionsAsync(host);
        await SeedThreeRevenueCreditsAsync(host, seeded);

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IIncomeStatementReportReader>();

        var all = await reader.GetAsync(new IncomeStatementReportRequest
        {
            FromInclusive = new DateOnly(2026, 1, 1),
            ToInclusive = new DateOnly(2026, 1, 1),
            IncludeZeroLines = false
        }, CancellationToken.None);

        all.TotalIncome.Should().Be(60m);
        all.TotalExpenses.Should().Be(0m);
        all.NetIncome.Should().Be(60m);
        all.Sections.SelectMany(x => x.Lines).Should().ContainSingle(l => l.AccountCode == "9032" && l.Amount == 60m);

        var orWithinDimension = await reader.GetAsync(new IncomeStatementReportRequest
        {
            FromInclusive = new DateOnly(2026, 1, 1),
            ToInclusive = new DateOnly(2026, 1, 1),
            DimensionScopes = new DimensionScopeBag([
                new DimensionScope(seeded.dim1, [seeded.a1, seeded.a3])
            ]),
            IncludeZeroLines = false
        }, CancellationToken.None);

        orWithinDimension.TotalIncome.Should().Be(40m);
        orWithinDimension.NetIncome.Should().Be(40m);
        orWithinDimension.Sections.SelectMany(x => x.Lines)
            .Should().ContainSingle(l => l.AccountCode == "9032" && l.Amount == 40m);

        var andAcrossDimensions = await reader.GetAsync(new IncomeStatementReportRequest
        {
            FromInclusive = new DateOnly(2026, 1, 1),
            ToInclusive = new DateOnly(2026, 1, 1),
            DimensionScopes = new DimensionScopeBag([
                new DimensionScope(seeded.dim1, [seeded.a1, seeded.a2]),
                new DimensionScope(seeded.dim2, [seeded.b2])
            ]),
            IncludeZeroLines = false
        }, CancellationToken.None);

        andAcrossDimensions.TotalIncome.Should().Be(20m);
        andAcrossDimensions.NetIncome.Should().Be(20m);
        andAcrossDimensions.Sections.SelectMany(x => x.Lines)
            .Should().ContainSingle(l => l.AccountCode == "9032" && l.Amount == 20m);
    }

    [Fact]
    public async Task BalanceSheet_ORWithinDimension_ANDAcrossDimensions_Work()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var seeded = await SeedBalanceSheetAccountsWithKnownDimensionsAsync(host);
        await SeedThreeBalancedBalanceSheetPostingsAsync(host, seeded);

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IBalanceSheetReportReader>();

        var all = await reader.GetAsync(new BalanceSheetReportRequest
        {
            AsOfPeriod = new DateOnly(2026, 1, 1),
            IncludeZeroAccounts = false,
            IncludeNetIncomeInEquity = false
        }, CancellationToken.None);

        all.TotalAssets.Should().Be(60m);
        all.TotalEquity.Should().Be(60m);
        all.IsBalanced.Should().BeTrue();

        var orWithinDimension = await reader.GetAsync(new BalanceSheetReportRequest
        {
            AsOfPeriod = new DateOnly(2026, 1, 1),
            DimensionScopes = new DimensionScopeBag([
                new DimensionScope(seeded.dim1, [seeded.a1, seeded.a3])
            ]),
            IncludeZeroAccounts = false,
            IncludeNetIncomeInEquity = false
        }, CancellationToken.None);

        orWithinDimension.TotalAssets.Should().Be(40m);
        orWithinDimension.TotalEquity.Should().Be(40m);
        orWithinDimension.IsBalanced.Should().BeTrue();

        var andAcrossDimensions = await reader.GetAsync(new BalanceSheetReportRequest
        {
            AsOfPeriod = new DateOnly(2026, 1, 1),
            DimensionScopes = new DimensionScopeBag([
                new DimensionScope(seeded.dim1, [seeded.a1, seeded.a2]),
                new DimensionScope(seeded.dim2, [seeded.b2])
            ]),
            IncludeZeroAccounts = false,
            IncludeNetIncomeInEquity = false
        }, CancellationToken.None);

        andAcrossDimensions.TotalAssets.Should().Be(20m);
        andAcrossDimensions.TotalEquity.Should().Be(20m);
        andAcrossDimensions.IsBalanced.Should().BeTrue();
    }

    private static async Task<(Guid cashId, Guid revenueId, Guid dim1, Guid dim2, Guid a1, Guid a2, Guid a3, Guid b1, Guid b2)>
        SeedIncomeStatementAccountsWithKnownDimensionsAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var coa = sp.GetRequiredService<IChartOfAccountsManagementService>();

        await coa.CreateAsync(
            new CreateAccountRequest(
                Code: "1032",
                Name: "Cash (IS Scope Filter)",
                Type: AccountType.Asset,
                IsContra: false,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow),
            CancellationToken.None);

        await coa.CreateAsync(
            new CreateAccountRequest(
                Code: "9032",
                Name: "Revenue (IS Scope Filter)",
                Type: AccountType.Income,
                IsContra: false,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow,
                DimensionRules:
                [
                    new AccountDimensionRuleRequest(DimensionCode: "dim_is_scope_1", IsRequired: true, Ordinal: 10),
                    new AccountDimensionRuleRequest(DimensionCode: "dim_is_scope_2", IsRequired: true, Ordinal: 20)
                ]),
            CancellationToken.None);

        var chart = await sp.GetRequiredService<IChartOfAccountsProvider>().GetAsync(CancellationToken.None);
        var cash = chart.Get("1032");
        var revenue = chart.Get("9032");

        var dim1 = revenue.DimensionRules[0].DimensionId;
        var dim2 = revenue.DimensionRules[1].DimensionId;

        var a1 = Guid.CreateVersion7();
        var a2 = Guid.CreateVersion7();
        var a3 = Guid.CreateVersion7();
        var b1 = Guid.CreateVersion7();
        var b2 = Guid.CreateVersion7();

        return (cash.Id, revenue.Id, dim1, dim2, a1, a2, a3, b1, b2);
    }

    private static async Task SeedThreeRevenueCreditsAsync(
        IHost host,
        (Guid cashId, Guid revenueId, Guid dim1, Guid dim2, Guid a1, Guid a2, Guid a3, Guid b1, Guid b2) seeded)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var posting = sp.GetRequiredService<PostingEngine>();
        var chart = await sp.GetRequiredService<IChartOfAccountsProvider>().GetAsync(CancellationToken.None);

        var cash = chart.Get(seeded.cashId);
        var revenue = chart.Get(seeded.revenueId);
        var dt = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        await posting.PostAsync(
            NGB.Accounting.PostingState.PostingOperation.Post,
            (ctx, ct) =>
            {
                ctx.Post(
                    Guid.CreateVersion7(),
                    dt,
                    cash,
                    revenue,
                    amount: 10m,
                    debitDimensions: DimensionBag.Empty,
                    creditDimensions: new DimensionBag([
                        new DimensionValue(seeded.dim1, seeded.a1),
                        new DimensionValue(seeded.dim2, seeded.b1)
                    ]));
                return Task.CompletedTask;
            },
            CancellationToken.None);

        await posting.PostAsync(
            NGB.Accounting.PostingState.PostingOperation.Post,
            (ctx, ct) =>
            {
                ctx.Post(
                    Guid.CreateVersion7(),
                    dt.AddMinutes(1),
                    cash,
                    revenue,
                    amount: 20m,
                    debitDimensions: DimensionBag.Empty,
                    creditDimensions: new DimensionBag([
                        new DimensionValue(seeded.dim1, seeded.a2),
                        new DimensionValue(seeded.dim2, seeded.b2)
                    ]));
                return Task.CompletedTask;
            },
            CancellationToken.None);

        await posting.PostAsync(
            NGB.Accounting.PostingState.PostingOperation.Post,
            (ctx, ct) =>
            {
                ctx.Post(
                    Guid.CreateVersion7(),
                    dt.AddMinutes(2),
                    cash,
                    revenue,
                    amount: 30m,
                    debitDimensions: DimensionBag.Empty,
                    creditDimensions: new DimensionBag([
                        new DimensionValue(seeded.dim1, seeded.a3),
                        new DimensionValue(seeded.dim2, seeded.b1)
                    ]));
                return Task.CompletedTask;
            },
            CancellationToken.None);
    }

    private static async Task<(Guid cashId, Guid revenueId, Guid dim1, Guid dim2, Guid a1, Guid a2, Guid a3, Guid b1, Guid b2)>
        SeedAccountWithKnownDimensionsAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var coa = sp.GetRequiredService<IChartOfAccountsManagementService>();

        await coa.CreateAsync(
            new CreateAccountRequest(
                Code: "1012",
                Name: "Cash (Scope Filter)",
                Type: AccountType.Asset,
                IsContra: false,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow,
                DimensionRules:
                [
                    new AccountDimensionRuleRequest(DimensionCode: "dim_scope_1", IsRequired: true, Ordinal: 10),
                    new AccountDimensionRuleRequest(DimensionCode: "dim_scope_2", IsRequired: true, Ordinal: 20)
                ]),
            CancellationToken.None);

        await coa.CreateAsync(
            new CreateAccountRequest(
                Code: "9012",
                Name: "Revenue (Scope Filter)",
                Type: AccountType.Income,
                IsActive: true),
            CancellationToken.None);

        var chart = await sp.GetRequiredService<IChartOfAccountsProvider>().GetAsync(CancellationToken.None);
        var cash = chart.Get("1012");
        var revenue = chart.Get("9012");

        var dim1 = cash.DimensionRules[0].DimensionId;
        var dim2 = cash.DimensionRules[1].DimensionId;

        var a1 = Guid.CreateVersion7();
        var a2 = Guid.CreateVersion7();
        var a3 = Guid.CreateVersion7();
        var b1 = Guid.CreateVersion7();
        var b2 = Guid.CreateVersion7();

        return (cash.Id, revenue.Id, dim1, dim2, a1, a2, a3, b1, b2);
    }

    private static async Task SeedThreeCashDebitsAsync(
        IHost host,
        (Guid cashId, Guid revenueId, Guid dim1, Guid dim2, Guid a1, Guid a2, Guid a3, Guid b1, Guid b2) seeded)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var posting = sp.GetRequiredService<PostingEngine>();
        var chart = await sp.GetRequiredService<IChartOfAccountsProvider>().GetAsync(CancellationToken.None);

        var cash = chart.Get(seeded.cashId);
        var revenue = chart.Get(seeded.revenueId);
        var dt = new DateTime(2026, 1, 10, 10, 0, 0, DateTimeKind.Utc);

        await posting.PostAsync(
            NGB.Accounting.PostingState.PostingOperation.Post,
            (_, ct) =>
            {
                _.Post(
                    Guid.CreateVersion7(),
                    dt,
                    cash,
                    revenue,
                    amount: 10m,
                    debitDimensions: new DimensionBag([
                        new DimensionValue(seeded.dim1, seeded.a1),
                        new DimensionValue(seeded.dim2, seeded.b1)
                    ]),
                    creditDimensions: DimensionBag.Empty);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        await posting.PostAsync(
            NGB.Accounting.PostingState.PostingOperation.Post,
            (_, ct) =>
            {
                _.Post(
                    Guid.CreateVersion7(),
                    dt.AddMinutes(1),
                    cash,
                    revenue,
                    amount: 20m,
                    debitDimensions: new DimensionBag([
                        new DimensionValue(seeded.dim1, seeded.a2),
                        new DimensionValue(seeded.dim2, seeded.b2)
                    ]),
                    creditDimensions: DimensionBag.Empty);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        await posting.PostAsync(
            NGB.Accounting.PostingState.PostingOperation.Post,
            (_, ct) =>
            {
                _.Post(
                    Guid.CreateVersion7(),
                    dt.AddMinutes(2),
                    cash,
                    revenue,
                    amount: 30m,
                    debitDimensions: new DimensionBag([
                        new DimensionValue(seeded.dim1, seeded.a3),
                        new DimensionValue(seeded.dim2, seeded.b1)
                    ]),
                    creditDimensions: DimensionBag.Empty);
                return Task.CompletedTask;
            },
            CancellationToken.None);
    }

    private static async Task<(Guid assetId, Guid equityId, Guid dim1, Guid dim2, Guid a1, Guid a2, Guid a3, Guid b1, Guid b2)>
        SeedBalanceSheetAccountsWithKnownDimensionsAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var coa = sp.GetRequiredService<IChartOfAccountsManagementService>();

        await coa.CreateAsync(
            new CreateAccountRequest(
                Code: "1312",
                Name: "Cash (BS Scope Filter)",
                Type: AccountType.Asset,
                IsContra: false,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow,
                DimensionRules:
                [
                    new AccountDimensionRuleRequest(DimensionCode: "dim_bs_scope_1", IsRequired: true, Ordinal: 10),
                    new AccountDimensionRuleRequest(DimensionCode: "dim_bs_scope_2", IsRequired: true, Ordinal: 20)
                ]),
            CancellationToken.None);

        await coa.CreateAsync(
            new CreateAccountRequest(
                Code: "3312",
                Name: "Owner Equity (BS Scope Filter)",
                Type: AccountType.Equity,
                IsContra: false,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow,
                DimensionRules:
                [
                    new AccountDimensionRuleRequest(DimensionCode: "dim_bs_scope_1", IsRequired: true, Ordinal: 10),
                    new AccountDimensionRuleRequest(DimensionCode: "dim_bs_scope_2", IsRequired: true, Ordinal: 20)
                ]),
            CancellationToken.None);

        var chart = await sp.GetRequiredService<IChartOfAccountsProvider>().GetAsync(CancellationToken.None);
        var asset = chart.Get("1312");
        var equity = chart.Get("3312");

        var dim1 = asset.DimensionRules[0].DimensionId;
        var dim2 = asset.DimensionRules[1].DimensionId;

        return (
            asset.Id,
            equity.Id,
            dim1,
            dim2,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7());
    }

    private static async Task SeedThreeBalancedBalanceSheetPostingsAsync(
        IHost host,
        (Guid assetId, Guid equityId, Guid dim1, Guid dim2, Guid a1, Guid a2, Guid a3, Guid b1, Guid b2) seeded)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var posting = sp.GetRequiredService<PostingEngine>();
        var chart = await sp.GetRequiredService<IChartOfAccountsProvider>().GetAsync(CancellationToken.None);

        var asset = chart.Get(seeded.assetId);
        var equity = chart.Get(seeded.equityId);
        var dt = new DateTime(2026, 1, 20, 10, 0, 0, DateTimeKind.Utc);

        async Task PostAsync(DateTime whenUtc, decimal amount, Guid dim1ValueId, Guid dim2ValueId)
        {
            var bag = new DimensionBag([
                new DimensionValue(seeded.dim1, dim1ValueId),
                new DimensionValue(seeded.dim2, dim2ValueId)
            ]);

            await posting.PostAsync(
                NGB.Accounting.PostingState.PostingOperation.Post,
                (ctx, ct) =>
                {
                    ctx.Post(
                        Guid.CreateVersion7(),
                        whenUtc,
                        asset,
                        equity,
                        amount,
                        debitDimensions: bag,
                        creditDimensions: bag);
                    return Task.CompletedTask;
                },
                CancellationToken.None);
        }

        await PostAsync(dt, 10m, seeded.a1, seeded.b1);
        await PostAsync(dt.AddMinutes(1), 20m, seeded.a2, seeded.b2);
        await PostAsync(dt.AddMinutes(2), 30m, seeded.a3, seeded.b1);
    }
}

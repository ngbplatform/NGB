using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Core.Dimensions;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

/// <summary>
/// P1: Scope filter semantics that UI relies on.
/// - AND across dimensions.
/// - empty scope bag must not restrict results.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AccountingReports_DimensionFilterSemantics_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task AccountCard_AndSemantics_And_EmptyScopeDoesNotFilter()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, revenueId, dim1, dim2, a1, b1, b2) = await SeedAccountWith2RequiredDimsAsync(host);
        await SeedThreeCashDebitsAsync(host, cashId, revenueId, dim1, dim2, a1, b1, b2);

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IAccountCardReader>();

        var all = await reader.GetAsync(
            cashId,
            fromInclusive: new DateOnly(2026, 1, 1),
            toInclusive: new DateOnly(2026, 1, 1),
            dimensionScopes: null,
            ct: CancellationToken.None);

        all.Should().HaveCount(3);

        var all2 = await ReportingTestHelpers.ReadAllGeneralLedgerAggregatedLinesAsync(
            scope.ServiceProvider,
            cashId,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 1),
            dimensionScopes: DimensionScopeBag.Empty,
            ct: CancellationToken.None);

        all2.Should().HaveCount(3);

        var strict = await ReportingTestHelpers.ReadAllGeneralLedgerAggregatedLinesAsync(
            scope.ServiceProvider,
            cashId,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 1),
            dimensionScopes: new DimensionScopeBag([
                new DimensionScope(dim1, [a1]),
                new DimensionScope(dim2, [b1])
            ]),
            ct: CancellationToken.None);

        strict.Should().HaveCount(1);
        strict[0].Dimensions.Items.Should().Contain(new DimensionValue(dim1, a1));
        strict[0].Dimensions.Items.Should().Contain(new DimensionValue(dim2, b1));

        var dim1Only = await reader.GetAsync(
            cashId,
            fromInclusive: new DateOnly(2026, 1, 1),
            toInclusive: new DateOnly(2026, 1, 1),
            dimensionScopes: new DimensionScopeBag([new DimensionScope(dim1, [a1])]),
            ct: CancellationToken.None);

        dim1Only.Should().HaveCount(2);
        dim1Only.All(x => x.Dimensions.Items.Contains(new DimensionValue(dim1, a1))).Should().BeTrue();
    }

    [Fact]
    public async Task GeneralLedgerAggregated_AndSemantics_And_EmptyScopeDoesNotFilter()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, revenueId, dim1, dim2, a1, b1, b2) = await SeedAccountWith2RequiredDimsAsync(host);
        await SeedThreeCashDebitsAsync(host, cashId, revenueId, dim1, dim2, a1, b1, b2);

        await using var scope = host.Services.CreateAsyncScope();
        var all = await ReportingTestHelpers.ReadAllGeneralLedgerAggregatedLinesAsync(
            scope.ServiceProvider,
            cashId,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 1),
            dimensionScopes: null,
            ct: CancellationToken.None);

        all.Should().HaveCount(3, "each document produces a distinct aggregated line");

        var all2 = await ReportingTestHelpers.ReadAllGeneralLedgerAggregatedLinesAsync(
            scope.ServiceProvider,
            cashId,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 1),
            dimensionScopes: DimensionScopeBag.Empty,
            ct: CancellationToken.None);

        all2.Should().HaveCount(3);

        var strict = await ReportingTestHelpers.ReadAllGeneralLedgerAggregatedLinesAsync(
            scope.ServiceProvider,
            cashId,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 1),
            dimensionScopes: new DimensionScopeBag([
                new DimensionScope(dim1, [a1]),
                new DimensionScope(dim2, [b1])
            ]),
            ct: CancellationToken.None);

        strict.Should().HaveCount(1);
        strict[0].Dimensions.Items.Should().Contain(new DimensionValue(dim1, a1));
        strict[0].Dimensions.Items.Should().Contain(new DimensionValue(dim2, b1));
    }

    private static async Task<(Guid cashId, Guid revenueId, Guid dim1, Guid dim2, Guid a1, Guid b1, Guid b2)>
        SeedAccountWith2RequiredDimsAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var coa = sp.GetRequiredService<IChartOfAccountsManagementService>();

        await coa.CreateAsync(
            new CreateAccountRequest(
                Code: "1011",
                Name: "Cash (Dim2)",
                Type: AccountType.Asset,
                IsContra: false,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow,
                DimensionRules:
                [
                    new AccountDimensionRuleRequest(DimensionCode: "dim_test_1", IsRequired: true, Ordinal: 10),
                    new AccountDimensionRuleRequest(DimensionCode: "dim_test_2", IsRequired: true, Ordinal: 20)
                ]),
            CancellationToken.None);

        await coa.CreateAsync(
            new CreateAccountRequest(
                Code: "9011",
                Name: "Revenue (Dim2)",
                Type: AccountType.Income,
                IsActive: true),
            CancellationToken.None);

        var chart = await sp.GetRequiredService<IChartOfAccountsProvider>().GetAsync(CancellationToken.None);
        var cash = chart.Get("1011");
        var revenue = chart.Get("9011");

        var dim1 = cash.DimensionRules[0].DimensionId;
        var dim2 = cash.DimensionRules[1].DimensionId;

        var a1 = Guid.CreateVersion7();
        var b1 = Guid.CreateVersion7();
        var b2 = Guid.CreateVersion7();

        return (cash.Id, revenue.Id, dim1, dim2, a1, b1, b2);
    }

    private static async Task SeedThreeCashDebitsAsync(IHost host, Guid cashId, Guid revenueId, Guid dim1, Guid dim2, Guid a1, Guid b1, Guid b2)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var posting = sp.GetRequiredService<PostingEngine>();
        var chart = await sp.GetRequiredService<IChartOfAccountsProvider>().GetAsync(CancellationToken.None);

        var cash = chart.Get(cashId);
        var revenue = chart.Get(revenueId);
        var dt = new DateTime(2026, 1, 10, 10, 0, 0, DateTimeKind.Utc);

        // 1) dim1=a1, dim2=b1
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
                    debitDimensions: new DimensionBag([
                        new DimensionValue(dim1, a1),
                        new DimensionValue(dim2, b1)
                    ]),
                    creditDimensions: DimensionBag.Empty);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        // 2) dim1=a1, dim2=b2
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
                    debitDimensions: new DimensionBag([
                        new DimensionValue(dim1, a1),
                        new DimensionValue(dim2, b2)
                    ]),
                    creditDimensions: DimensionBag.Empty);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        // 3) dim1=other, dim2=b1
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
                    debitDimensions: new DimensionBag([
                        new DimensionValue(dim1, Guid.CreateVersion7()),
                        new DimensionValue(dim2, b1)
                    ]),
                    creditDimensions: DimensionBag.Empty);
                return Task.CompletedTask;
            },
            CancellationToken.None);
    }
}

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Core.Dimensions;
using NGB.Persistence.Readers;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Posting;

[Collection(PostgresCollection.Name)]
public sealed class AccountingReaders_Dimensions_ByRuleOrder_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateOnly Period = new(2026, 1, 1);
    private static readonly DateTime DayUtc = new(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Turnovers_And_Balances_PersistAllDimensions_ByOrdinalOrder()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var coa = sp.GetRequiredService<IChartOfAccountsManagementService>();

        // Debit account: 4 dimension rules, ordinals are intentionally NOT 1/2/3.
        await coa.CreateAsync(new CreateAccountRequest(
            Code: "1010",
            Name: "Cash",
            Type: AccountType.Asset,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow,
            DimensionRules:
            [
                new AccountDimensionRuleRequest(DimensionCode: "counterparty", IsRequired: true, Ordinal: 10),
                new AccountDimensionRuleRequest(DimensionCode: "building", IsRequired: false, Ordinal: 20),
                new AccountDimensionRuleRequest(DimensionCode: "unit", IsRequired: false, Ordinal: 30),
                new AccountDimensionRuleRequest(DimensionCode: "contract", IsRequired: false, Ordinal: 40)
            ]));

        // Credit account: no dimensions.
        await coa.CreateAsync(new CreateAccountRequest(
            Code: "9010",
            Name: "Revenue",
            Type: AccountType.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow));

        var chart = await sp.GetRequiredService<IChartOfAccountsProvider>().GetAsync();
        var debit = chart.Get("1010");
        var credit = chart.Get("9010");

        var rules = debit.DimensionRules
            .OrderBy(x => x.Ordinal)
            .ToArray();

        rules.Should().HaveCount(4);

        var v1 = Guid.CreateVersion7();
        var v2 = Guid.CreateVersion7();
        var v3 = Guid.CreateVersion7();
        var v4 = Guid.CreateVersion7();

        var bag = new DimensionBag([
            new DimensionValue(rules[0].DimensionId, v1),
            new DimensionValue(rules[1].DimensionId, v2),
            new DimensionValue(rules[2].DimensionId, v3),
            new DimensionValue(rules[3].DimensionId, v4)
        ]);

        var engine = sp.GetRequiredService<PostingEngine>();
        var docId = Guid.CreateVersion7();

        await engine.PostAsync(PostingOperation.Post, (ctx, ct) =>
        {
            ctx.Post(
                documentId: docId,
                period: DayUtc,
                debit: debit,
                credit: credit,
                amount: 100m,
                debitDimensions: bag,
                creditDimensions: DimensionBag.Empty);
            return Task.CompletedTask;
        }, manageTransaction: true, CancellationToken.None);

        var turnoverReader = sp.GetRequiredService<IAccountingTurnoverReader>();
        var turnovers = await turnoverReader.GetForPeriodAsync(Period, CancellationToken.None);

        // Pick the debit turnover row (it must have a non-empty DimensionSetId).
        var t = turnovers.Single(x => x.AccountId == debit.Id && x.DimensionSetId != Guid.Empty);

        // Dimensions are resolved from platform_dimension_set_items.
        t.Dimensions.Should().Contain(new DimensionValue(rules[0].DimensionId, v1));
        t.Dimensions.Should().Contain(new DimensionValue(rules[1].DimensionId, v2));
        t.Dimensions.Should().Contain(new DimensionValue(rules[2].DimensionId, v3));
        t.Dimensions.Should().Contain(new DimensionValue(rules[3].DimensionId, v4));

        // IMPORTANT: dimension rules are ordered by Ordinal. Do not assume ordinals 1/2/3.
        t.Dimensions.Should().Contain(x => x.ValueId == v1);
        t.Dimensions.Should().Contain(x => x.ValueId == v2);
        t.Dimensions.Should().Contain(x => x.ValueId == v3);

        var closing = sp.GetRequiredService<IPeriodClosingService>();
        await closing.CloseMonthAsync(Period, closedBy: "tests", ct: CancellationToken.None);

        var balanceReader = sp.GetRequiredService<IAccountingBalanceReader>();
        var balances = await balanceReader.GetForPeriodAsync(Period, CancellationToken.None);

        var b = balances.Single(x => x.AccountId == debit.Id && x.DimensionSetId == t.DimensionSetId);

        b.Dimensions.Should().Contain(new DimensionValue(rules[0].DimensionId, v1));
        b.Dimensions.Should().Contain(new DimensionValue(rules[1].DimensionId, v2));
        b.Dimensions.Should().Contain(new DimensionValue(rules[2].DimensionId, v3));
        b.Dimensions.Should().Contain(new DimensionValue(rules[3].DimensionId, v4));



        b.Dimensions.Should().Contain(x => x.ValueId == v1);
        b.Dimensions.Should().Contain(x => x.ValueId == v2);
        b.Dimensions.Should().Contain(x => x.ValueId == v3);
    }
}

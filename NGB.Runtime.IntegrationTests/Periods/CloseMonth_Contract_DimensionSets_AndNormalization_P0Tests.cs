using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Core.Dimensions;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.Periods;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

/// <summary>
/// P0: CloseMonth contract around period normalization and DimensionSet-based aggregation.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CloseMonth_Contract_DimensionSets_AndNormalization_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CloseMonthAsync_ComputesBalances_PerDimensionSet_AndOpeningPlusTurnoverEqualsClosing()
    {
        // Arrange
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var coa = sp.GetRequiredService<IChartOfAccountsManagementService>();

        // Both accounts require the same dimension, so turnovers/balances are keyed by DimensionSetId.
        await coa.CreateAsync(new CreateAccountRequest(
            Code: "1010",
            Name: "Cash",
            Type: AccountType.Asset,
            DimensionRules: new[]
            {
                new AccountDimensionRuleRequest(DimensionCode: "counterparty", IsRequired: true, Ordinal: 1)
            }), CancellationToken.None);

        await coa.CreateAsync(new CreateAccountRequest(
            Code: "9010",
            Name: "Revenue",
            Type: AccountType.Income,
            DimensionRules: new[]
            {
                new AccountDimensionRuleRequest(DimensionCode: "counterparty", IsRequired: true, Ordinal: 1)
            }), CancellationToken.None);

        var chart = await sp.GetRequiredService<IChartOfAccountsProvider>().GetAsync(CancellationToken.None);
        var cash = chart.Get("1010");
        var revenue = chart.Get("9010");

        var dimId = cash.DimensionRules.Single().DimensionId;
        var valueA = Guid.CreateVersion7();
        var valueB = Guid.CreateVersion7();

        var bagA = new DimensionBag(new[] { new DimensionValue(dimId, valueA) });
        var bagB = new DimensionBag(new[] { new DimensionValue(dimId, valueB) });

        var engine = sp.GetRequiredService<PostingEngine>();
        await PostAsync(engine, Guid.CreateVersion7(), new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc), cash, revenue, 100m, bagA);
        await PostAsync(engine, Guid.CreateVersion7(), new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc), cash, revenue, 250m, bagB);

        var closing = sp.GetRequiredService<IPeriodClosingService>();
        var period = new DateOnly(2026, 1, 1);

        // Act
        await closing.CloseMonthAsync(period, closedBy: "test", CancellationToken.None);

        // Assert
        var turnoverReader = sp.GetRequiredService<IAccountingTurnoverReader>();
        var balanceReader = sp.GetRequiredService<IAccountingBalanceReader>();

        var turnovers = await turnoverReader.GetForPeriodAsync(period, CancellationToken.None);
        var balances = await balanceReader.GetForPeriodAsync(period, CancellationToken.None);

        turnovers.Should().HaveCount(4);
        balances.Should().HaveCount(4);

        var cashTurnovers = turnovers.Where(x => x.AccountId == cash.Id).ToArray();
        cashTurnovers.Should().HaveCount(2);
        cashTurnovers.Select(x => x.DimensionSetId).Distinct().Should().HaveCount(2);
        cashTurnovers.Select(x => x.DimensionSetId).Should().NotContain(Guid.Empty);

        var dsA = cashTurnovers.Single(x => x.Dimensions.Contains(new DimensionValue(dimId, valueA))).DimensionSetId;
        var dsB = cashTurnovers.Single(x => x.Dimensions.Contains(new DimensionValue(dimId, valueB))).DimensionSetId;
        dsA.Should().NotBe(dsB);

        // DimensionSetId should be stable across accounts for the same DimensionBag.
        turnovers.Should().ContainSingle(x => x.AccountId == revenue.Id && x.DimensionSetId == dsA);
        turnovers.Should().ContainSingle(x => x.AccountId == revenue.Id && x.DimensionSetId == dsB);

        // Contract: Opening + (Debit - Credit) == Closing for each (AccountId, DimensionSetId).
        foreach (var b in balances)
        {
            var t = turnovers.Single(x => x.AccountId == b.AccountId && x.DimensionSetId == b.DimensionSetId);
            b.ClosingBalance.Should().Be(b.OpeningBalance + (t.DebitAmount - t.CreditAmount));
        }

        // And balances must keep the analytical dimensions (read-model projection).
        balances.Should().ContainSingle(x => x.AccountId == cash.Id && x.DimensionSetId == dsA && x.Dimensions.Contains(new DimensionValue(dimId, valueA)));
        balances.Should().ContainSingle(x => x.AccountId == cash.Id && x.DimensionSetId == dsB && x.Dimensions.Contains(new DimensionValue(dimId, valueB)));
        balances.Should().ContainSingle(x => x.AccountId == revenue.Id && x.DimensionSetId == dsA && x.Dimensions.Contains(new DimensionValue(dimId, valueA)));
        balances.Should().ContainSingle(x => x.AccountId == revenue.Id && x.DimensionSetId == dsB && x.Dimensions.Contains(new DimensionValue(dimId, valueB)));
    }

    [Fact]
    public async Task CloseMonthAsync_WhenNonMonthStartPeriodProvided_NormalizesToMonthStart_AndMarksClosed()
    {
        // Arrange
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var coa = sp.GetRequiredService<IChartOfAccountsManagementService>();
        await coa.CreateAsync(new CreateAccountRequest("50", "Cash", AccountType.Asset), CancellationToken.None);
        await coa.CreateAsync(new CreateAccountRequest("90.1", "Revenue", AccountType.Income), CancellationToken.None);

        var chart = await sp.GetRequiredService<IChartOfAccountsProvider>().GetAsync(CancellationToken.None);
        var cash = chart.Get("50");
        var revenue = chart.Get("90.1");

        var engine = sp.GetRequiredService<PostingEngine>();
        await engine.PostAsync(PostingOperation.Post, (ctx, ct) =>
        {
            ctx.Post(
                documentId: Guid.CreateVersion7(),
                period: new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc),
                debit: cash,
                credit: revenue,
                amount: 10m);
            return Task.CompletedTask;
        }, manageTransaction: true, CancellationToken.None);

        var closing = sp.GetRequiredService<IPeriodClosingService>();
        var closedReader = sp.GetRequiredService<IClosedPeriodReader>();

        // Act: pass a non-month-start DateOnly.
        await closing.CloseMonthAsync(new DateOnly(2026, 1, 15), closedBy: "test", CancellationToken.None);

        // Assert: period must be normalized to month start.
        var normalized = new DateOnly(2026, 1, 1);
        var closed = await closedReader.GetClosedAsync(normalized, normalized, CancellationToken.None);
        closed.Should().ContainSingle(x => x.Period == normalized);
    }

    private static async Task PostAsync(
        PostingEngine engine,
        Guid documentId,
        DateTime periodUtc,
        Account debit,
        Account credit,
        decimal amount,
        DimensionBag bag)
    {
        await engine.PostAsync(PostingOperation.Post, (ctx, ct) =>
        {
            ctx.Post(
                documentId: documentId,
                period: periodUtc,
                debit: debit,
                credit: credit,
                amount: amount,
                debitDimensions: bag,
                creditDimensions: bag);
            return Task.CompletedTask;
        }, manageTransaction: true, CancellationToken.None);
    }
}

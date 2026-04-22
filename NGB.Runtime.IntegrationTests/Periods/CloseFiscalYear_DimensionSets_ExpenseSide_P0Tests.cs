using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Core.Dimensions;
using NGB.Persistence.Dimensions;
using NGB.Persistence.Readers;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

/// <summary>
/// P0: Dimension policy for CloseFiscalYear.
/// - P&amp;L account keeps its dimensions on the P&amp;L side of the closing entry.
/// - Retained earnings always uses empty dimensions.
///
/// This test validates the EXPENSE branch where the closing entry is:
///   Dr Retained Earnings (empty dims)
///   Cr Expense (expense dims)
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CloseFiscalYear_DimensionSets_ExpenseSide_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CloseFiscalYearAsync_WhenExpenseHasDimensions_ShouldPreserveOnCreditSide_AndRetainedEarningsEmpty()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var endPeriod = new DateOnly(2026, 1, 1); // month start
        var periodUtc = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);

        var retainedEarningsId = await SeedCoaAsync(host);

        var dimBuilding = DeterministicGuid.Create("Dimension|buildings");
        var dimCounterparty = DeterministicGuid.Create("Dimension|counterparties");

        var buildingId = Guid.CreateVersion7();
        var counterpartyId = Guid.CreateVersion7();

        var expenseBag = new DimensionBag(new[]
        {
            new DimensionValue(dimBuilding, buildingId),
            new DimensionValue(dimCounterparty, counterpartyId)
        });

        // Create Expense activity carrying a non-empty dimension set.
        await PostExpenseAsync(host, Guid.CreateVersion7(), periodUtc, amount: 40m, expenseDimensions: expenseBag);

        var expectedCloseDocumentId = DeterministicGuid.Create($"CloseFiscalYear|{endPeriod:yyyy-MM-dd}");

        // Act
        await CloseFiscalYearAsync(host, endPeriod, retainedEarningsId);

        // Assert
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
            var dimReader = sp.GetRequiredService<IDimensionSetReader>();

            var entries = await entryReader.GetByDocumentAsync(expectedCloseDocumentId, CancellationToken.None);

            // Only one P&L account exists in this test => a single closing entry.
            entries.Should().ContainSingle();
            var e = entries.Single();

            e.Amount.Should().Be(40m);
            e.Debit.Code.Should().Be("300");
            e.Credit.Code.Should().Be("91");

            e.DebitDimensionSetId.Should().Be(Guid.Empty, "retained earnings must not carry dimensions");
            e.CreditDimensionSetId.Should().NotBe(Guid.Empty, "expense dimensions must be preserved");

            var bags = await dimReader.GetBagsByIdsAsync(new[] { e.CreditDimensionSetId }, CancellationToken.None);
            bags.Should().ContainKey(e.CreditDimensionSetId);
            var bag = bags[e.CreditDimensionSetId];

            bag.Items.Should().BeEquivalentTo(expenseBag.Items);
        }
    }

    private static async Task<Guid> SeedCoaAsync(IHost host)
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

        // Expense with dimension rules (required), so posting enforces dimensions and platform_dimensions rows exist.
        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "91",
            Name: "Expenses",
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

    private static async Task PostExpenseAsync(
        IHost host,
        Guid documentId,
        DateTime periodUtc,
        decimal amount,
        DimensionBag expenseDimensions)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            postingAction: async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                var debit = chart.Get("91");
                var credit = chart.Get("50");

                ctx.Post(
                    documentId,
                    periodUtc,
                    debit,
                    credit,
                    amount,
                    debitDimensions: expenseDimensions,
                    creditDimensions: DimensionBag.Empty);
            },
            ct: CancellationToken.None);
    }

    private static async Task CloseFiscalYearAsync(IHost host, DateOnly endPeriod, Guid retainedEarningsAccountId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();

        await closing.CloseFiscalYearAsync(
            fiscalYearEndPeriod: endPeriod,
            retainedEarningsAccountId: retainedEarningsAccountId,
            closedBy: "test",
            ct: CancellationToken.None);
    }
}

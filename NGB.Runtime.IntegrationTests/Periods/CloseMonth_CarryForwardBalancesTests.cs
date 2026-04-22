using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.Periods;
using NGB.Accounting.PostingState;
using NGB.Persistence.Readers;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(PostgresCollection.Name)]
public sealed class CloseMonth_CarryForwardBalancesTests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CloseMonthAsync_WhenNextMonthHasNoTurnovers_CarriesForwardPreviousClosingBalances()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await SeedMinimalCoaAsync(host);

        // December: create a non-zero balance (Cash +100, Equity -100).
        var decPeriod = new DateOnly(2025, 12, 1);
        var decPostingUtc = new DateTime(2025, 12, 15, 12, 0, 0, DateTimeKind.Utc);
        await PostAsync(host, Guid.CreateVersion7(), decPostingUtc, debitCode: "50", creditCode: "80", amount: 100m);

        // Close December to materialize balances.
        await CloseMonthAsync(host, decPeriod);

        // January: no postings at all.
        var janPeriod = new DateOnly(2026, 1, 1);
        await CloseMonthAsync(host, janPeriod);

        // Assert: January balances must carry-forward from December closing.
        await using var scope = host.Services.CreateAsyncScope();
        var balanceReader = scope.ServiceProvider.GetRequiredService<IAccountingBalanceReader>();

        var janBalances = await balanceReader.GetForPeriodAsync(janPeriod, CancellationToken.None);
        janBalances.Should().NotBeEmpty();

        var cash = janBalances.SingleOrDefault(b => b.AccountCode == "50");
        cash.Should().NotBeNull("cash account balance should be present after month close");
        cash!.OpeningBalance.Should().Be(100m);
        cash.ClosingBalance.Should().Be(100m);

        // Sanity: period on rows must match requested period.
        janBalances.Should().OnlyContain(b => b.Period == AccountingPeriod.FromDateOnly(janPeriod));
    }

    private static async Task SeedMinimalCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        await coa.CreateAsync(
            new CreateAccountRequest(
                Code: "50",
                Name: "Cash",
                Type: AccountType.Asset,
                IsContra: false,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow
            ),
            CancellationToken.None);

        await coa.CreateAsync(
            new CreateAccountRequest(
                Code: "80",
                Name: "Owner's Equity",
                Type: AccountType.Equity,
                IsContra: false,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow
            ),
            CancellationToken.None);
    }

    private static async Task PostAsync(
        IHost host,
        Guid documentId,
        DateTime periodUtc,
        string debitCode,
        string creditCode,
        decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            PostingOperation.Post,
            async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                var debit = chart.Get(debitCode);
                var credit = chart.Get(creditCode);

                ctx.Post(documentId, periodUtc, debit: debit, credit: credit, amount: amount);
            },
            manageTransaction: true,
            CancellationToken.None);
    }

    private static async Task CloseMonthAsync(IHost host, DateOnly period)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
        await closing.CloseMonthAsync(period, closedBy: "test", CancellationToken.None);
    }
}

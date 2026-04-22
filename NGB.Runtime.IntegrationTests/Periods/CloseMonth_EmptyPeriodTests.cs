using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.Periods;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(PostgresCollection.Name)]
public sealed class CloseMonth_EmptyPeriodTests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task CloseMonthAsync_EmptyPeriod_WritesNoBalances_AndMarksPeriodClosed()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var period = new DateOnly(2026, 1, 1);

        // Optional: seed a minimal CoA to mimic a realistic environment.
        // CloseMonth itself does not require a CoA, but having it ensures the platform is in a typical state.
        await SeedMinimalCoaAsync(host);

        // Act
        await CloseMonthAsync(host, period);

        // Assert
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            var turnoverReader = sp.GetRequiredService<IAccountingTurnoverReader>();
            var balanceReader = sp.GetRequiredService<IAccountingBalanceReader>();
            var closedReader = sp.GetRequiredService<IClosedPeriodReader>();

            var turnovers = await turnoverReader.GetForPeriodAsync(period, CancellationToken.None);
            turnovers.Should().BeEmpty("no postings were made in the period");

            var balances = await balanceReader.GetForPeriodAsync(period, CancellationToken.None);
            balances.Should().BeEmpty("no prior balances and no turnovers => nothing to carry-forward");

            var closed = await closedReader.GetClosedAsync(period, period, CancellationToken.None);
            closed.Should().ContainSingle(p => p.Period == period && p.ClosedBy == "test");
            closed.Single().ClosedAtUtc.Should().BeAfter(DateTime.UtcNow.AddMinutes(-5));
        }
    }

    private static async Task SeedMinimalCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var accounts = sp.GetRequiredService<IChartOfAccountsManagementService>();

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Revenue",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);
    }

    private static async Task CloseMonthAsync(IHost host, DateOnly period)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();

        await closing.CloseMonthAsync(period, closedBy: "test", CancellationToken.None);
    }
}

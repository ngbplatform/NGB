using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Persistence.Locks;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

/// <summary>
/// P1: CloseFiscalYear must take an advisory lock on the fiscal year end period.
/// If the period lock is held by another transaction, CloseFiscalYear must block until it is released.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CloseFiscalYear_PeriodLockBlocking_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CloseFiscalYearAsync_WhenEndPeriodLockIsHeld_Blocks_UntilReleased()
    {
        await Fixture.ResetDatabaseAsync();

        var endPeriod = new DateOnly(2041, 1, 1); // January -> no "prior months closed" precondition.
        const string closedBy = "test";

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var retainedEarningsId = await SeedCoaAsync(host);

        var gate = new Barrier(2);

        var holdingTask = Task.Run(async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var locks = scope.ServiceProvider.GetRequiredService<IAdvisoryLockManager>();

            await uow.BeginTransactionAsync(CancellationToken.None);
            await locks.LockPeriodAsync(endPeriod, CancellationToken.None);

            gate.SignalAndWait(); // lock is now held

            // Keep the transaction open long enough to ensure the closing transaction blocks.
            await Task.Delay(TimeSpan.FromSeconds(2));

            await uow.CommitAsync(CancellationToken.None);
        });

        var blockedTask = Task.Run(async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();

            gate.SignalAndWait();

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            var act = () => closing.CloseFiscalYearAsync(
                fiscalYearEndPeriod: endPeriod,
                retainedEarningsAccountId: retainedEarningsId,
                closedBy: closedBy,
                ct: cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>(
                "CloseFiscalYear must block on the period lock while it is held by another transaction");
        });

        await Task.WhenAll(holdingTask, blockedTask);

        // After the first transaction commits, CloseFiscalYear should proceed.
        await using var scope3 = host.Services.CreateAsyncScope();
        var closing3 = scope3.ServiceProvider.GetRequiredService<IPeriodClosingService>();

        using var cts3 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await closing3.Invoking(s => s.CloseFiscalYearAsync(endPeriod, retainedEarningsId, closedBy, cts3.Token))
            .Should().NotThrowAsync();
    }

    private static async Task<Guid> SeedCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        await svc.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow), CancellationToken.None);

        var reId = await svc.CreateAsync(new CreateAccountRequest(
            Code: "300",
            Name: "Retained Earnings",
            Type: AccountType.Equity,
            StatementSection: StatementSection.Equity,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow), CancellationToken.None);

        // P&L accounts (not strictly required for this blocking semantics test, but keep close path realistic)
        await svc.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Revenue",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow), CancellationToken.None);

        await svc.CreateAsync(new CreateAccountRequest(
            Code: "91",
            Name: "Expenses",
            Type: AccountType.Expense,
            StatementSection: StatementSection.Expenses,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow), CancellationToken.None);

        return reId;
    }
}

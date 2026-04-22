using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.Periods;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.Periods;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(PostgresCollection.Name)]
public sealed class CloseMonthConcurrencyTests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task CloseMonthAsync_TwoConcurrentCalls_OneSucceeds_OtherThrowsAlreadyClosed_AndNoDuplicates()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var periodUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var period = DateOnly.FromDateTime(periodUtc);

        await SeedMinimalCoaAsync(host, cashPolicy: NegativeBalancePolicy.Allow);
        await PostOnceAsync(host, Guid.CreateVersion7(), periodUtc, amount: 100m);

        // Release both closers at the same time to maximize the race window.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<Exception?> RunCloseAsync()
        {
            try
            {
                await gate.Task;
                await CloseMonthAsync(host, period);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        var t1 = RunCloseAsync();
        var t2 = RunCloseAsync();
        gate.SetResult();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var results = await Task.WhenAll(t1, t2).WaitAsync(cts.Token);

        results.Count(e => e is null).Should().Be(1, "exactly one concurrent closer should succeed");
        results.Count(e => e is PeriodAlreadyClosedException).Should().Be(1, "the second closer must observe the period as already closed");

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var balanceReader = sp.GetRequiredService<IAccountingBalanceReader>();
        var closedReader = sp.GetRequiredService<IClosedPeriodReader>();

        var balances = await balanceReader.GetForPeriodAsync(period, CancellationToken.None);
        balances.Should().HaveCount(2);

        var closed = await closedReader.GetClosedAsync(period, period, CancellationToken.None);
        closed.Should().HaveCount(1);
    }

    private static async Task SeedMinimalCoaAsync(IHost host, NegativeBalancePolicy cashPolicy)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: cashPolicy
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
    }

    private static async Task CloseMonthAsync(IHost host, DateOnly period)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();

        await closing.CloseMonthAsync(period, closedBy: "test", CancellationToken.None);
    }

    private static async Task PostOnceAsync(IHost host, Guid documentId, DateTime periodUtc, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            postingAction: async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);

                var debit = chart.Get("50");
                var credit = chart.Get("90.1");

                ctx.Post(
                    documentId: documentId,
                    period: periodUtc,
                    debit: debit,
                    credit: credit,
                    amount: amount);
            },
            ct: CancellationToken.None);
    }
}

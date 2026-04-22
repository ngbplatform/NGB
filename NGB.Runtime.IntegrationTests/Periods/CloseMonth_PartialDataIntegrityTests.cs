using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.Periods;
using NGB.Persistence.Writers;
using NGB.PostgreSql.Writers;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(PostgresCollection.Name)]
public sealed class CloseMonth_PartialDataIntegrityTests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task CloseMonthAsync_WhenBalanceWriterFails_RollsBackBalances_AndDoesNotMarkClosed()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(
            fixture.ConnectionString,
            configureTestServices: services =>
            {
                // Replace balance writer with a fault-injecting wrapper that fails AFTER it writes balances.
                services.RemoveAll<IAccountingBalanceWriter>();
                services.AddScoped<PostgresAccountingBalanceWriter>();
                services.AddScoped<IAccountingBalanceWriter>(sp =>
                    new FailAfterSaveBalanceWriter(sp.GetRequiredService<PostgresAccountingBalanceWriter>()));
            });

        var periodUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var period = DateOnly.FromDateTime(periodUtc);

        await SeedMinimalCoaAsync(host);

        // Create some turnovers so CloseMonth will attempt to write balances.
        await PostOnceAsync(host, Guid.CreateVersion7(), periodUtc, amount: 100m);

        // Act
        Func<Task> act = () => CloseMonthAsync(host, period);

        // Assert: CloseMonth fails, but MUST rollback all balance writes + not mark the period closed.
        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*Simulated failure after balances save*");

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var balanceReader = sp.GetRequiredService<IAccountingBalanceReader>();
        var closedReader = sp.GetRequiredService<IClosedPeriodReader>();

        (await balanceReader.GetForPeriodAsync(period, CancellationToken.None))
            .Should().BeEmpty("balances must rollback if CloseMonth fails mid-transaction");

        (await closedReader.GetClosedAsync(period, period, CancellationToken.None))
            .Should().BeEmpty("period must not be marked closed if CloseMonth fails");
    }

    private static async Task CloseMonthAsync(IHost host, DateOnly period)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
        await closing.CloseMonthAsync(period, closedBy: "test", CancellationToken.None);
    }

    private static async Task SeedMinimalCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        await accounts.CreateAsync(new CreateAccountRequest(
            "50",
            "Cash",
            AccountType.Asset,
            StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            "90.1",
            "Revenue",
            AccountType.Income,
            StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);
    }

    private static async Task PostOnceAsync(IHost host, Guid documentId, DateTime periodUtc, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            operation: PostingOperation.Post,
            postingAction: async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(documentId, periodUtc, chart.Get("50"), chart.Get("90.1"), amount);
            },
            manageTransaction: true,
            CancellationToken.None);
    }

    private sealed class FailAfterSaveBalanceWriter(IAccountingBalanceWriter inner) : IAccountingBalanceWriter
    {
        private bool _failed;

        public Task DeleteForPeriodAsync(DateOnly period, CancellationToken ct = default) =>
            inner.DeleteForPeriodAsync(period, ct);

        public async Task SaveAsync(IEnumerable<NGB.Accounting.Balances.AccountingBalance> balances, CancellationToken ct = default)
        {
            await inner.SaveAsync(balances, ct);

            if (_failed)
                return;

            _failed = true;
            throw new NotSupportedException("Simulated failure after balances save");
        }
    }
}

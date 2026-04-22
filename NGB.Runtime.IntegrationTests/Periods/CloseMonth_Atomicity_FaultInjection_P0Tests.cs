using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Persistence.Checkers;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.Periods;
using NGB.Persistence.Periods;
using NGB.PostgreSql.Periods;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(PostgresCollection.Name)]
public sealed class CloseMonth_Atomicity_FaultInjection_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CloseMonthAsync_WhenMarkClosedFails_RollsBackBalances_AndDoesNotMarkClosed()
    {
        // Arrange
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            configureTestServices: services =>
            {
                // Replace closed-period repository with a wrapper that fails AFTER it marks the period closed.
                services.RemoveAll<IClosedPeriodRepository>();
                services.AddScoped<PostgresClosedPeriodRepository>();
                services.AddScoped<IClosedPeriodRepository>(sp =>
                    new FailAfterMarkClosedRepository(sp.GetRequiredService<PostgresClosedPeriodRepository>()));
            });

        var periodUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var period = DateOnly.FromDateTime(periodUtc);

        await SeedMinimalCoaAsync(host);
        await PostOnceAsync(host, Guid.CreateVersion7(), periodUtc, amount: 100m);

        // Act
        Func<Task> act = () => CloseMonthAsync(host, period);

        // Assert
        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*Simulated failure after MarkClosedAsync*");

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        (await sp.GetRequiredService<IAccountingBalanceReader>().GetForPeriodAsync(period, CancellationToken.None))
            .Should().BeEmpty("balances must rollback if CloseMonth fails after MarkClosedAsync");

        (await sp.GetRequiredService<IClosedPeriodReader>().GetClosedAsync(period, period, CancellationToken.None))
            .Should().BeEmpty("period must not be marked closed if CloseMonth fails");
    }

    [Fact]
    public async Task CloseMonthAsync_WhenIntegrityCheckerFails_RollsBack_AndDoesNotMarkClosed()
    {
        // Arrange
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            configureTestServices: services =>
            {
                // Replace integrity checker with a deterministic failure.
                services.RemoveAll<IAccountingIntegrityChecker>();
                services.AddScoped<IAccountingIntegrityChecker, ThrowingIntegrityChecker>();
            });

        var periodUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var period = DateOnly.FromDateTime(periodUtc);

        await SeedMinimalCoaAsync(host);
        await PostOnceAsync(host, Guid.CreateVersion7(), periodUtc, amount: 100m);

        // Act
        Func<Task> act = () => CloseMonthAsync(host, period);

        // Assert
        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*Simulated integrity failure*");

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        (await sp.GetRequiredService<IAccountingBalanceReader>().GetForPeriodAsync(period, CancellationToken.None))
            .Should().BeEmpty();

        (await sp.GetRequiredService<IClosedPeriodReader>().GetClosedAsync(period, period, CancellationToken.None))
            .Should().BeEmpty();
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
            ct: CancellationToken.None);
    }

    private sealed class FailAfterMarkClosedRepository(IClosedPeriodRepository inner) : IClosedPeriodRepository
    {
        private bool _failed;

        public Task<bool> IsClosedAsync(DateOnly period, CancellationToken ct = default) =>
            inner.IsClosedAsync(period, ct);

        public async Task MarkClosedAsync(DateOnly period, string closedBy, DateTime closedAtUtc, CancellationToken ct = default)
        {
            await inner.MarkClosedAsync(period, closedBy, closedAtUtc, ct);

            if (_failed)
                return;

            _failed = true;
            throw new NotSupportedException("Simulated failure after MarkClosedAsync");
        }

        public Task ReopenAsync(DateOnly period, CancellationToken ct = default) =>
            inner.ReopenAsync(period, ct);
    }

    private sealed class ThrowingIntegrityChecker : IAccountingIntegrityChecker
    {
        public Task AssertPeriodIsBalancedAsync(DateOnly period, CancellationToken ct = default) =>
            throw new NotSupportedException("Simulated integrity failure");
    }
}

using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.Turnovers;
using NGB.Persistence.UnitOfWork;
using NGB.Persistence.Writers;
using NGB.PostgreSql.Writers;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Maintenance;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Concurrency;

/// <summary>
/// P2 coverage: Rebuild operations must not deadlock with Posting or Period Closing for the same period.
/// Both Rebuild and other operations acquire the same period-level advisory lock.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RebuildConcurrency_NoDeadlocks_EndToEndTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{

    [Fact(Timeout = 30_000)]
    public async Task RebuildMonth_And_Post_SamePeriod_AreSerialized_NoDeadlock()
    {
        await Fixture.ResetDatabaseAsync();

        var probe = new SleepProbe();

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            configureTestServices: services =>
            {
                // Make rebuild hold the period lock for a while.
                services.RemoveAll<IAccountingTurnoverWriter>();

                services.AddSingleton(probe);
                services.AddScoped<PostgresAccountingTurnoverWriter>();

                services.AddScoped<IAccountingTurnoverWriter>(sp =>
                    new SleepOnceTurnoverWriter(
                        sp.GetRequiredService<SleepProbe>(),
                        sp.GetRequiredService<IUnitOfWork>(),
                        sp.GetRequiredService<PostgresAccountingTurnoverWriter>()));
            });

        await SeedMinimalCoaAsync(host);

        var period = new DateOnly(2026, 1, 1);
        var periodUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var rebuildTask = RebuildMonthAsync(host, period, CancellationToken.None);
        await probe.SleepIssued.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var postTask = PostOnceAsync(host, Guid.CreateVersion7(), periodUtc, amount: 100m, CancellationToken.None);

        var completedQuickly = await Task.WhenAny(postTask, Task.Delay(TimeSpan.FromSeconds(1))) == postTask;
        completedQuickly.Should().BeFalse(
            "posting in the same period must wait for rebuild to release the period advisory lock");

        await rebuildTask.WaitAsync(TimeSpan.FromSeconds(10));
        await postTask.WaitAsync(TimeSpan.FromSeconds(10));
    }


    [Fact(Timeout = 30_000)]
    public async Task RebuildMonth_And_CloseMonth_SamePeriod_AreSerialized_NoDeadlock()
    {
        await Fixture.ResetDatabaseAsync();

        var probe = new SleepProbe();

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            configureTestServices: services =>
            {
                // Make rebuild hold the period lock for a while.
                services.RemoveAll<IAccountingTurnoverWriter>();

                services.AddSingleton(probe);
                services.AddScoped<PostgresAccountingTurnoverWriter>();

                services.AddScoped<IAccountingTurnoverWriter>(sp =>
                    new SleepOnceTurnoverWriter(
                        sp.GetRequiredService<SleepProbe>(),
                        sp.GetRequiredService<IUnitOfWork>(),
                        sp.GetRequiredService<PostgresAccountingTurnoverWriter>()));
            });

        await SeedMinimalCoaAsync(host);

        var period = new DateOnly(2026, 1, 1);

        var rebuildTask = RebuildMonthAsync(host, period, CancellationToken.None);
        await probe.SleepIssued.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var closeTask = CloseMonthAsync(host, period, closedBy: "tests", CancellationToken.None);

        var completedQuickly = await Task.WhenAny(closeTask, Task.Delay(TimeSpan.FromSeconds(1))) == closeTask;
        completedQuickly.Should().BeFalse(
            "closing the same period must wait for rebuild to release the period advisory lock");

        await rebuildTask.WaitAsync(TimeSpan.FromSeconds(10));
        await closeTask.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static async Task SeedMinimalCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Revenue",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow), CancellationToken.None);
    }

    private static async Task RebuildMonthAsync(IHost host, DateOnly period, CancellationToken ct)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var rebuild = scope.ServiceProvider.GetRequiredService<IAccountingRebuildService>();

        await rebuild.RebuildMonthAsync(period, previousPeriodForChainCheck: null, ct);
    }

    private static async Task CloseMonthAsync(IHost host, DateOnly period, string closedBy, CancellationToken ct)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();

        await closing.CloseMonthAsync(period, closedBy, ct);
    }

    private static async Task PostOnceAsync(IHost host, Guid documentId, DateTime period, decimal amount,
        CancellationToken ct)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var engine = sp.GetRequiredService<PostingEngine>();

        await engine.PostAsync(PostingOperation.Post, async (ctx, ct2) =>
        {
            var chart = await ctx.GetChartOfAccountsAsync(ct2);
            ctx.Post(documentId, period, chart.Get("50"), chart.Get("90.1"), amount);
        }, manageTransaction: true, ct);
    }

    private sealed class SleepProbe
    {
        public TaskCompletionSource SleepIssued { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls;
    }

    private sealed class SleepOnceTurnoverWriter(
        SleepProbe probe,
        IUnitOfWork uow,
        PostgresAccountingTurnoverWriter inner) : IAccountingTurnoverWriter
    {
        public async Task DeleteForPeriodAsync(DateOnly period, CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref probe.Calls) == 1)
            {
                probe.SleepIssued.TrySetResult();

                // Ensure we run pg_sleep *within the same unit-of-work transaction*.
                await uow.EnsureConnectionOpenAsync(ct);

                if (uow.Transaction is null)
                    throw new NotSupportedException(
                        "Test setup error: expected an active transaction in turnover writer.");

                var conn = (NpgsqlConnection)uow.Connection;
                await conn.ExecuteAsync(new CommandDefinition(
                    "select pg_sleep(5);",
                    transaction: uow.Transaction,
                    cancellationToken: ct));
            }

            await inner.DeleteForPeriodAsync(period, ct);
        }

        public Task WriteAsync(IEnumerable<AccountingTurnover> turnovers, CancellationToken ct = default)
            => inner.WriteAsync(turnovers, ct);
    }
}

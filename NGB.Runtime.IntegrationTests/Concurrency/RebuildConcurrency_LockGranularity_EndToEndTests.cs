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
/// P2 coverage: period advisory locks must be granular by month.
/// Rebuild (period A) must not block posting/closing of another period (period B).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RebuildConcurrency_LockGranularity_EndToEndTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact(Timeout = 30_000)]
    public async Task RebuildMonth_PeriodA_DoesNotBlock_Post_PeriodB()
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

        var periodA = new DateOnly(2026, 1, 1);
        var periodB = new DateOnly(2026, 2, 1);
        var periodBUtc = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        var rebuildTask = RebuildMonthAsync(host, periodA, CancellationToken.None);
        await probe.SleepIssued.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var documentId = Guid.CreateVersion7();
        var postTask = PostOnceAsync(host, documentId, periodBUtc, amount: 123m, CancellationToken.None);

        var completedQuickly = await Task.WhenAny(postTask, Task.Delay(TimeSpan.FromSeconds(2))) == postTask;
        completedQuickly.Should().BeTrue(
            "posting into a different period must not be blocked by the advisory lock for another month");

        await rebuildTask.WaitAsync(TimeSpan.FromSeconds(10));
        await postTask;

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var count = await conn.ExecuteScalarAsync<long>(
            "select count(*) from accounting_register_main where period_month = @p;",
            new { p = periodB });

        count.Should().BeGreaterThan(0);
    }


    [Fact(Timeout = 30_000)]
    public async Task RebuildMonth_PeriodA_DoesNotBlock_CloseMonth_PeriodB()
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

        var periodA = new DateOnly(2026, 1, 1);
        var periodB = new DateOnly(2026, 2, 1);

        var rebuildTask = RebuildMonthAsync(host, periodA, CancellationToken.None);
        await probe.SleepIssued.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var closeTask = CloseMonthAsync(host, periodB, closedBy: "tests", CancellationToken.None);

        var completedQuickly = await Task.WhenAny(closeTask, Task.Delay(TimeSpan.FromSeconds(2))) == closeTask;
        completedQuickly.Should().BeTrue(
            "closing a different period must not be blocked by the advisory lock for another month");

        await rebuildTask.WaitAsync(TimeSpan.FromSeconds(10));
        await closeTask;

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var count = await conn.ExecuteScalarAsync<long>(
            "select count(*) from accounting_closed_periods where period = @p;",
            new { p = periodB });

        count.Should().Be(1);
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

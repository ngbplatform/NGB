using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Persistence.UnitOfWork;
using NGB.Persistence.Writers;
using NGB.PostgreSql.Writers;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Concurrency;

/// <summary>
/// P1 coverage: period-level advisory locks must serialize postings within the same period,
/// but allow postings for different periods to proceed concurrently.
/// 
/// We simulate a long DB I/O inside the posting transaction via pg_sleep.
/// PostingEngine acquires its period lock before entry writer runs, so sleep holds the lock.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AdvisoryLocks_PeriodGranularity_EndToEndTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact(Timeout = 30_000)]
    public async Task TwoPosts_DifferentPeriods_CanCompleteWhileFirstIsSleeping()
    {
        await Fixture.ResetDatabaseAsync();

        var probe = new SleepProbe();

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            configureTestServices: services =>
            {
                services.RemoveAll<IAccountingEntryWriter>();

                services.AddSingleton(probe);
                services.AddScoped<PostgresAccountingEntryWriter>();

                services.AddScoped<IAccountingEntryWriter>(sp =>
                    new SleepOnceEntryWriter(
                        sp.GetRequiredService<SleepProbe>(),
                        sp.GetRequiredService<IUnitOfWork>(),
                        sp.GetRequiredService<PostgresAccountingEntryWriter>()));
            });

        await SeedMinimalCoaAsync(host);

        var periodA = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodB = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        var docA = Guid.CreateVersion7();
        var docB = Guid.CreateVersion7();

        // Start first post (will sleep inside DB transaction).
        var t1 = PostOnceAsync(host, docA, periodA, amount: 100m, CancellationToken.None);
        await probe.SleepIssued.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Start second post for different period; must complete while t1 is sleeping.
        var t2 = PostOnceAsync(host, docB, periodB, amount: 100m, CancellationToken.None);

        await t2.WaitAsync(TimeSpan.FromSeconds(3));

        // Then t1 must also complete.
        await t1.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact(Timeout = 30_000)]
    public async Task TwoPosts_SamePeriod_AreSerialized_SecondDoesNotCompleteUntilFirstReleasesLock()
    {
        await Fixture.ResetDatabaseAsync();

        var probe = new SleepProbe();

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            configureTestServices: services =>
            {
                services.RemoveAll<IAccountingEntryWriter>();

                services.AddSingleton(probe);
                services.AddScoped<PostgresAccountingEntryWriter>();

                services.AddScoped<IAccountingEntryWriter>(sp =>
                    new SleepOnceEntryWriter(
                        sp.GetRequiredService<SleepProbe>(),
                        sp.GetRequiredService<IUnitOfWork>(),
                        sp.GetRequiredService<PostgresAccountingEntryWriter>()));
            });

        await SeedMinimalCoaAsync(host);

        var period = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var doc1 = Guid.CreateVersion7();
        var doc2 = Guid.CreateVersion7();

        var t1 = PostOnceAsync(host, doc1, period, amount: 100m, CancellationToken.None);
        await probe.SleepIssued.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var t2 = PostOnceAsync(host, doc2, period, amount: 100m, CancellationToken.None);

        // While t1 holds the advisory lock, t2 must not finish quickly.
        var completed = await Task.WhenAny(t2, Task.Delay(TimeSpan.FromSeconds(1))) == t2;
        completed.Should().BeFalse("posting in the same period must be serialized by the period advisory lock");

        // After t1 completes, t2 must complete too (no deadlock).
        await t1.WaitAsync(TimeSpan.FromSeconds(10));
        await t2.WaitAsync(TimeSpan.FromSeconds(10));
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

    private sealed class SleepOnceEntryWriter(
        SleepProbe probe,
        IUnitOfWork uow,
        PostgresAccountingEntryWriter inner) : IAccountingEntryWriter
    {
        public async Task WriteAsync(IReadOnlyList<NGB.Accounting.Registers.AccountingEntry> entries,
            CancellationToken ct = default)
        {
            // Only the first call sleeps.
            if (Interlocked.Increment(ref probe.Calls) == 1)
            {
                probe.SleepIssued.TrySetResult();

                // Ensure we run pg_sleep *within the same unit-of-work transaction*.
                // Dapper MUST receive the transaction explicitly; otherwise Npgsql may treat it as an out-of-txn command
                // and the UoW rollback path can observe a completed transaction.
                await uow.EnsureConnectionOpenAsync(ct);

                if (uow.Transaction is null)
                    throw new NotSupportedException(
                        "Test setup error: expected an active transaction in entry writer.");

                var conn = (NpgsqlConnection)uow.Connection;
                await conn.ExecuteAsync(new CommandDefinition(
                    "select pg_sleep(5);",
                    transaction: uow.Transaction,
                    cancellationToken: ct));
            }

            await inner.WriteAsync(entries, ct);
        }
    }
}

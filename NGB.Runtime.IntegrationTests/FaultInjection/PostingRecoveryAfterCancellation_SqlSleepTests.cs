using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Accounting.Registers;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.PostingState;
using NGB.Persistence.UnitOfWork;
using NGB.Persistence.Writers;
using NGB.PostgreSql.Writers;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.FaultInjection;

/// <summary>
/// Infrastructure-level cancellation test: first attempt is cancelled while a real SQL command is executing
/// inside the posting transaction (pg_sleep). We then prove the same host/process can post again.
///
/// Goal: catch regressions where cancellation leaves an "aborted" connection/transaction state that poisons
/// subsequent postings or database resets.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostingRecoveryAfterCancellation_SqlSleepTests(PostgresTestFixture fixture)
{
    [Fact(Timeout = 30_000)]
    public async Task PostAsync_WhenFirstAttemptCancelledDuringDbIo_DoesNotPoisonSubsequentPosting()
    {
        await fixture.ResetDatabaseAsync();

        var probe = new CancellationProbe();

        using var host = IntegrationHostFactory.Create(
            fixture.ConnectionString,
            configureTestServices: services =>
            {
                // Replace the entry writer with: "cancel-first-write during real DB I/O, then delegate to the real postgres writer".
                services.RemoveAll<IAccountingEntryWriter>();

                // Probe is singleton so we can coordinate across multiple scopes (multiple post attempts).
                services.AddSingleton(probe);

                // Real postgres writer (concrete) used by the wrapper.
                services.AddScoped<PostgresAccountingEntryWriter>();

                // Wrapper uses the real writer on all calls except the very first one.
                services.AddScoped<IAccountingEntryWriter>(sp =>
                    new CancelFirstSqlSleepEntryWriter(
                        sp.GetRequiredService<CancellationProbe>(),
                        sp.GetRequiredService<IUnitOfWork>(),
                        sp.GetRequiredService<PostgresAccountingEntryWriter>()));
            });

        var period = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        await SeedMinimalCoaAsync(host);

        var failedDocId = Guid.CreateVersion7();
        var okDocId = Guid.CreateVersion7();

        Task? postTask = null;
        using var cts = new CancellationTokenSource();

        try
        {
            // 1) First attempt: cancel while a real SQL command is executing inside the entry-writer.
            postTask = PostOnceAsync(host, failedDocId, period, cts.Token);

            // Wait until the writer has issued the pg_sleep command (we are inside the DB transaction).
            await probe.SleepCommandIssued.Task.WaitAsync(TimeSpan.FromSeconds(10));

            // Cancel while the command is executing.
            await cts.CancelAsync();

            var ex = await Record.ExceptionAsync(
                () => postTask.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None));

            ex.Should().NotBeNull("the first attempt must be interrupted");
            IsExpectedCancellation(ex!).Should().BeTrue(
                $"expected cancellation/command-cancel exception (not statement_timeout); got: {ex}");

            // After the canceled attempt, the period MUST be clean (no turnovers at all).
            await AssertPeriodIsCleanAsync(host, period);

            // And document-scoped artifacts must not exist.
            await AssertNoDocumentScopedEffectsAsync(host, failedDocId);
        }
        finally
        {
            // Safety net: make sure no background task keeps DB locks/transactions alive.
            try
            {
                await cts.CancelAsync();
            }
            catch
            {
                // ignore
            }

            if (postTask is not null)
            {
                try
                {
                    await postTask.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
                }
                catch
                {
                    // ignore (we only care it doesn't hang)
                }
            }
        }

        // 2) Second attempt in the SAME host/process: must succeed (proves no poisoned connection/txn state).
        await PostOnceAsync(host, okDocId, period, CancellationToken.None);

        await AssertPostedAsync(host, okDocId, period);
    }

    private static bool IsExpectedCancellation(Exception ex)
    {
        var e = Unwrap(ex);

        if (e is OperationCanceledException)
            return true;

        // Depending on Npgsql version and the exact timing, cancellation can surface as a PostgresException (57014).
        if (e is PostgresException pe && pe.SqlState == "57014")
        {
            // Do NOT accept statement_timeout as a "pass" (it is only a failsafe against hangs).
            if (pe.MessageText.Contains("statement timeout", StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        return false;

        static Exception Unwrap(Exception ex)
        {
            Exception cur = ex;

            while (true)
            {
                if (cur is AggregateException agg && agg.InnerExceptions.Count == 1)
                {
                    cur = agg.InnerExceptions[0];
                    continue;
                }

                if (cur is NpgsqlException npg && npg.InnerException is not null)
                {
                    cur = npg.InnerException;
                    continue;
                }

                return cur;
            }
        }
    }

    private static async Task SeedMinimalCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var accounts = sp.GetRequiredService<IChartOfAccountsManagementService>();

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

    private static async Task PostOnceAsync(IHost host, Guid documentId, DateTime period, CancellationToken ct)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var posting = sp.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            operation: PostingOperation.Post,
            postingAction: async (ctx, ct2) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct2);
                var debit = chart.Get("50");
                var credit = chart.Get("90.1");

                ctx.Post(documentId, period, debit, credit, amount: 100m);
            },
            ct: ct);
    }

    private static async Task AssertPeriodIsCleanAsync(IHost host, DateTime period)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var turnoverReader = sp.GetRequiredService<IAccountingTurnoverReader>();
        (await turnoverReader.GetForPeriodAsync(DateOnly.FromDateTime(period), CancellationToken.None))
            .Should()
            .BeEmpty("a cancelled posting must rollback all writes in the transaction");
    }

    private static async Task AssertNoDocumentScopedEffectsAsync(IHost host, Guid documentId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        (await entryReader.GetByDocumentAsync(documentId, CancellationToken.None)).Should().BeEmpty();

        var logReader = sp.GetRequiredService<IPostingStateReader>();
        var page = await logReader.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = DateTime.UtcNow.AddDays(-7),
            ToUtc = DateTime.UtcNow.AddDays(7),
            DocumentId = documentId,
            Operation = PostingOperation.Post,
            PageSize = 50,
        }, CancellationToken.None);

        page.Records.Should().BeEmpty("posting_log must rollback with the transaction");
    }

    private static async Task AssertPostedAsync(IHost host, Guid documentId, DateTime period)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        (await entryReader.GetByDocumentAsync(documentId, CancellationToken.None)).Should().HaveCount(1);

        var turnoverReader = sp.GetRequiredService<IAccountingTurnoverReader>();
        var turnovers = await turnoverReader.GetForPeriodAsync(DateOnly.FromDateTime(period), CancellationToken.None);
        turnovers.Should().NotBeEmpty();
    }

    private sealed class CancellationProbe
    {
        public int WriteCallCount;

        public TaskCompletionSource SleepCommandIssued { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>
    /// First call runs a real SQL command that blocks (pg_sleep) under the posting transaction.
    /// Cancellation is expected to interrupt that command.
    /// </summary>
    private sealed class CancelFirstSqlSleepEntryWriter(
        CancellationProbe probe,
        IUnitOfWork uow,
        IAccountingEntryWriter inner) : IAccountingEntryWriter
    {
        public async Task WriteAsync(IReadOnlyList<AccountingEntry> entries, CancellationToken ct = default)
        {
            var call = Interlocked.Increment(ref probe.WriteCallCount);

            if (call == 1)
            {
                if (!ct.CanBeCanceled)
                {
                    throw new NotSupportedException(
                        $"CancellationToken did not flow into {nameof(IAccountingEntryWriter)} (non-cancelable token)." +
                        " This would make the test non-deterministic and could poison the test run.");
                }

                if (uow.Transaction is null)
                {
                    throw new NotSupportedException(
                        "Expected an active transaction inside PostingEngine when calling IAccountingEntryWriter.");
                }

                await uow.EnsureConnectionOpenAsync(ct);

                // Failsafe against hangs if cancellation plumbing regresses.
                // (Do NOT treat statement_timeout as a successful cancellation in assertions.)
                await uow.Connection.ExecuteAsync(new CommandDefinition(
                    "SET LOCAL statement_timeout = 5000;",
                    transaction: uow.Transaction,
                    cancellationToken: ct));

                // Signal that we're about to start the real blocking DB I/O.
                probe.SleepCommandIssued.TrySetResult();

                // Real DB I/O that should be cancelled via CancellationToken.
                await uow.Connection.ExecuteAsync(new CommandDefinition(
                    "SELECT pg_sleep(60);",
                    transaction: uow.Transaction,
                    cancellationToken: ct));

                // If we ever get here, neither cancellation nor statement_timeout happened.
                throw new NotSupportedException("pg_sleep completed unexpectedly; cancellation did not occur.");
            }

            await inner.WriteAsync(entries, ct);
        }
    }
}

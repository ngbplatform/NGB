using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using NGB.Accounting.Accounts;
using NGB.Accounting.Periods;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.PostingState;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Posting;

[Collection(PostgresCollection.Name)]
public sealed class PostingLog_TakeoverConcurrency_ExternalTransactionModeTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string Cash = "50";
    private const string Revenue = "90.1";

    [Fact]
    public async Task PostAsync_StaleInProgress_ExternalTx_TwoConcurrentAttempts_OnlyOneTakesOver_AndWritesOnce()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await SeedMinimalCoaAsync(host);

        var documentId = Guid.CreateVersion7();
        var periodUtc = new DateTime(2026, 1, 12, 12, 0, 0, DateTimeKind.Utc);
        var amount = 100m;

        // Simulate a crashed process: started long ago, not completed.
        var staleStartedAtUtc = DateTime.UtcNow.AddHours(-3);
        await InsertInProgressPostingLogRowAsync(
            Fixture.ConnectionString,
            documentId,
            PostingOperation.Post,
            startedAtUtc: staleStartedAtUtc);

        using var barrier = new Barrier(participantCount: 2);

        // Act: two concurrent attempts, each with its own explicit transaction.
        var t1 = Task.Run(() => TryPostOnceExternalTxAsync(host, documentId, periodUtc, amount, barrier));
        var t2 = Task.Run(() => TryPostOnceExternalTxAsync(host, documentId, periodUtc, amount, barrier));

        var outcomes = await Task.WhenAll(t1, t2);

        // Assert: exactly one executed. The other is either AlreadyCompleted or throws InProgress.
        outcomes.Count(o => o.Result == PostingResult.Executed).Should().Be(1);

        outcomes.Count(o =>
                o.Result == PostingResult.AlreadyCompleted ||
                (o.Exception is PostingAlreadyInProgressException))
            .Should().Be(1);

        // Assert: register wrote exactly once.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
            var entries = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);
            entries.Should().HaveCount(1);

            var turnoverReader = sp.GetRequiredService<IAccountingTurnoverReader>();
            var month = AccountingPeriod.FromDateTime(periodUtc);

            var turnovers = await turnoverReader.GetForPeriodAsync(month, CancellationToken.None);
            turnovers.Should().ContainSingle(x => x.AccountCode == Cash && x.DebitAmount == amount && x.CreditAmount == 0m);
            turnovers.Should().ContainSingle(x => x.AccountCode == Revenue && x.CreditAmount == amount && x.DebitAmount == 0m);

            var logReader = sp.GetRequiredService<IPostingStateReader>();
            var page = await logReader.GetPageAsync(new PostingStatePageRequest
            {
                FromUtc = DateTime.UtcNow.AddHours(-12),
                ToUtc = DateTime.UtcNow.AddHours(12),
                DocumentId = documentId,
                Operation = PostingOperation.Post,
                PageSize = 10
            }, CancellationToken.None);

            page.Records.Should().HaveCount(1);
            page.Records[0].Status.Should().Be(PostingStateStatus.Completed);
            page.Records[0].CompletedAtUtc.Should().NotBeNull();
            page.Records[0].StartedAtUtc.Should().BeAfter(staleStartedAtUtc);
        }
    }

    [Fact]
    public async Task PostAsync_StaleInProgress_ExternalTx_WinnerRollsBack_AllowsNextAttemptToExecute()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await SeedMinimalCoaAsync(host);

        var documentId = Guid.CreateVersion7();
        var periodUtc = new DateTime(2026, 1, 13, 12, 0, 0, DateTimeKind.Utc);

        var staleStartedAtUtc = DateTime.UtcNow.AddHours(-3);
        await InsertInProgressPostingLogRowAsync(
            Fixture.ConnectionString,
            documentId,
            PostingOperation.Post,
            startedAtUtc: staleStartedAtUtc);

        // Attempt #1: takes over stale InProgress but caller rolls back.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            var uow = sp.GetRequiredService<IUnitOfWork>();
            var posting = sp.GetRequiredService<PostingEngine>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            var result = await posting.PostAsync(
                operation: PostingOperation.Post,
                postingAction: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(documentId, periodUtc, chart.Get(Cash), chart.Get(Revenue), 100m);
                },
                manageTransaction: false,
                ct: CancellationToken.None);

            result.Should().Be(PostingResult.Executed, "first attempt should be able to take over stale InProgress");

            await uow.RollbackAsync(CancellationToken.None);
        }

        // After rollback: nothing written, posting_log row should still be InProgress (the stale row).
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
            (await entryReader.GetByDocumentAsync(documentId, CancellationToken.None)).Should().BeEmpty();

            var turnoverReader = sp.GetRequiredService<IAccountingTurnoverReader>();
            (await turnoverReader.GetForPeriodAsync(AccountingPeriod.FromDateTime(periodUtc), CancellationToken.None)).Should().BeEmpty();

            var logReader = sp.GetRequiredService<IPostingStateReader>();
            var page = await logReader.GetPageAsync(new PostingStatePageRequest
            {
                FromUtc = DateTime.UtcNow.AddHours(-12),
                ToUtc = DateTime.UtcNow.AddHours(12),
                DocumentId = documentId,
                Operation = PostingOperation.Post,
                PageSize = 10
            }, CancellationToken.None);

            page.Records.Should().HaveCount(1);
            // This record is intentionally stale (StartedAtUtc is set in the past). Status is a
            // computed classification by the reader, so after rollback it remains stale.
            page.Records[0].Status.Should().Be(PostingStateStatus.StaleInProgress);
            page.Records[0].CompletedAtUtc.Should().BeNull();
            page.Records[0].StartedAtUtc.Should().Be(staleStartedAtUtc);
        }

        // Attempt #2: should still be able to take over and complete.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            var uow = sp.GetRequiredService<IUnitOfWork>();
            var posting = sp.GetRequiredService<PostingEngine>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            var result = await posting.PostAsync(
                operation: PostingOperation.Post,
                postingAction: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(documentId, periodUtc, chart.Get(Cash), chart.Get(Revenue), 100m);
                },
                manageTransaction: false,
                ct: CancellationToken.None);

            result.Should().Be(PostingResult.Executed);
            await uow.CommitAsync(CancellationToken.None);
        }

        // Final: exactly one write exists, log is completed.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
            (await entryReader.GetByDocumentAsync(documentId, CancellationToken.None)).Should().HaveCount(1);

            var logReader = sp.GetRequiredService<IPostingStateReader>();
            var page = await logReader.GetPageAsync(new PostingStatePageRequest
            {
                FromUtc = DateTime.UtcNow.AddHours(-12),
                ToUtc = DateTime.UtcNow.AddHours(12),
                DocumentId = documentId,
                Operation = PostingOperation.Post,
                PageSize = 10
            }, CancellationToken.None);

            page.Records.Should().HaveCount(1);
            page.Records[0].Status.Should().Be(PostingStateStatus.Completed);
            page.Records[0].StartedAtUtc.Should().BeAfter(staleStartedAtUtc);
            page.Records[0].CompletedAtUtc.Should().NotBeNull();
        }
    }

    private sealed record AttemptOutcome(PostingResult? Result, Exception? Exception);

    private static async Task<AttemptOutcome> TryPostOnceExternalTxAsync(
        IHost host,
        Guid documentId,
        DateTime periodUtc,
        decimal amount,
        Barrier barrier)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var uow = sp.GetRequiredService<IUnitOfWork>();
        var posting = sp.GetRequiredService<PostingEngine>();

        await uow.BeginTransactionAsync(CancellationToken.None);

        try
        {
            barrier.SignalAndWaitOrThrow(TimeSpan.FromSeconds(10));

            var result = await posting.PostAsync(
                operation: PostingOperation.Post,
                postingAction: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(documentId, periodUtc, chart.Get(Cash), chart.Get(Revenue), amount);
                },
                manageTransaction: false,
                ct: CancellationToken.None);

            // External transaction mode: caller decides commit/rollback.
            await uow.CommitAsync(CancellationToken.None);
            return new AttemptOutcome(result, Exception: null);
        }
        catch (Exception ex)
        {
            await uow.RollbackAsync(CancellationToken.None);
            return new AttemptOutcome(Result: null, Exception: ex);
        }
    }

    private static async Task InsertInProgressPostingLogRowAsync(
        string connectionString,
        Guid documentId,
        PostingOperation operation,
        DateTime startedAtUtc)
    {
        // Bypass repositories: fault-injection setup.
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        const string sql = """
                           INSERT INTO accounting_posting_state(
                               document_id, operation, started_at_utc, completed_at_utc
                           )
                           VALUES (@document_id, @operation, @started_at_utc, NULL);
                           """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("document_id", documentId);
        cmd.Parameters.AddWithValue("operation", (short)operation);
        cmd.Parameters.AddWithValue("started_at_utc", startedAtUtc);

        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task SeedMinimalCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var accounts = sp.GetRequiredService<IChartOfAccountsManagementService>();

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: Cash,
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: Revenue,
            Name: "Revenue",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);
    }
}

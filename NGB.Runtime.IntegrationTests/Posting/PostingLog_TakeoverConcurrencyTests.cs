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
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Posting;

[Collection(PostgresCollection.Name)]
public sealed class PostingLog_TakeoverConcurrencyTests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    private const string Cash = "50";
    private const string Revenue = "90.1";

    [Fact]
    public async Task PostAsync_StaleInProgress_TwoConcurrentAttempts_OnlyOneTakesOver_AndWritesOnce()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await SeedMinimalCoaAsync(host);

        var documentId = Guid.CreateVersion7();
        var period = new DateTime(2026, 1, 6, 12, 0, 0, DateTimeKind.Utc);
        var amount = 100m;

        // Simulate a crashed process that started posting long ago (completed_at_utc is NULL).
        var staleStartedAtUtc = DateTime.UtcNow.AddHours(-2);
        await InsertInProgressPostingLogRowAsync(
            Fixture.ConnectionString,
            documentId,
            PostingOperation.Post,
            startedAtUtc: staleStartedAtUtc);

        using var barrier = new Barrier(participantCount: 2);

        // Act: two concurrent attempts.
        var t1 = Task.Run(() => TryPostOnceAsync(host, documentId, period, amount, barrier));
        var t2 = Task.Run(() => TryPostOnceAsync(host, documentId, period, amount, barrier));

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

            // Assert: monthly turnovers were written exactly once (by the winner).
            var turnoverReader = sp.GetRequiredService<IAccountingTurnoverReader>();
            var month = AccountingPeriod.FromDateTime(period);

            var turnovers = await turnoverReader.GetForPeriodAsync(month, CancellationToken.None);
            turnovers.Should().ContainSingle(x => x.AccountCode == Cash && x.DebitAmount == amount && x.CreditAmount == 0m);
            turnovers.Should().ContainSingle(x => x.AccountCode == Revenue && x.CreditAmount == amount && x.DebitAmount == 0m);

            // Assert: posting_log is completed.
            var logReader = sp.GetRequiredService<IPostingStateReader>();
            var page = await logReader.GetPageAsync(new PostingStatePageRequest
            {
                FromUtc = DateTime.UtcNow.AddHours(-6),
                ToUtc = DateTime.UtcNow.AddHours(6),
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

    private sealed record AttemptOutcome(PostingResult? Result, Exception? Exception);

    private static async Task<AttemptOutcome> TryPostOnceAsync(
        IHost host,
        Guid documentId,
        DateTime period,
        decimal amount,
        Barrier barrier)
    {
        try
        {
            await using var scope = host.Services.CreateAsyncScope();
            var sp = scope.ServiceProvider;

            var posting = sp.GetRequiredService<PostingEngine>();

            // Synchronize start to maximize race probability.
            barrier.SignalAndWaitOrThrow(TimeSpan.FromSeconds(10));

            var result = await posting.PostAsync(
                operation: PostingOperation.Post,
                postingAction: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    var debit = chart.Get(Cash);
                    var credit = chart.Get(Revenue);

                    ctx.Post(documentId, period, debit, credit, amount);
                },
                ct: CancellationToken.None);

            return new AttemptOutcome(result, Exception: null);
        }
        catch (Exception ex)
        {
            return new AttemptOutcome(Result: null, Exception: ex);
        }
    }

    private static async Task InsertInProgressPostingLogRowAsync(
        string connectionString,
        Guid documentId,
        PostingOperation operation,
        DateTime startedAtUtc)
    {
        // We intentionally bypass repositories: this is a fault-injection setup for integration tests.
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
            AccountType.Asset,
            StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: Revenue,
            Name: "Revenue",
            AccountType.Income,
            StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);
    }
}

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.PostingState;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

/// <summary>
/// Concurrency coverage for *forbidden* operations in a closed period.
///
/// The critical invariant: even under concurrency, the closed-period guard must reject operations
/// *before* any side effects (entries/turnovers/balances/posting_log) are persisted.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ClosedPeriodPolicy_ConcurrentForbiddenOpsTests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task Unpost_And_Repost_Concurrent_AfterCloseMonth_BothRejected_NoSideEffects_AndNoPostingLog()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var logWindow = PostingLogTestWindow.Capture();

        // Use an edge date inside the period to make sure period normalization is not the reason for a failure.
        var periodUtc = new DateTime(2026, 1, 31, 23, 59, 59, DateTimeKind.Utc);
        var period = new DateOnly(2026, 1, 1);
        var documentId = Guid.CreateVersion7();

        await SeedMinimalCoaAsync(host);

        // Post BEFORE closing.
        await PostOnceAsync(host, documentId, periodUtc, amount: 100m);

        // Close month.
        await CloseMonthAsync(host, period);

        var before = await SnapshotAsync(host, documentId, period, logWindow);

        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Act
        var unpostTask = RunOutcomeAsync(host, gate, async sp =>
        {
            var unposting = sp.GetRequiredService<UnpostingService>();
            await unposting.UnpostAsync(documentId, CancellationToken.None);
        });

        var repostTask = RunOutcomeAsync(host, gate, async sp =>
        {
            var reposting = sp.GetRequiredService<RepostingService>();
            await reposting.RepostAsync(
                documentId,
                postNew: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(documentId, periodUtc, chart.Get("50"), chart.Get("90.1"), 200m);
                },
                CancellationToken.None);
        });

        gate.SetResult(true);

        var outcomes = await Task.WhenAll(unpostTask, repostTask)
            .WaitAsync(TimeSpan.FromSeconds(45));

        // Assert: both rejected by closed-period guard (not by unrelated invariants).
        outcomes[0].Error.Should().BeOfType<PostingPeriodClosedException>()
            .Which.Message.Should().Contain($"Posting is forbidden. Period is closed: {period:yyyy-MM-dd}");

        outcomes[1].Error.Should().BeOfType<PostingPeriodClosedException>()
            .Which.Message.Should().Contain($"Posting is forbidden. Period is closed: {period:yyyy-MM-dd}");

        var after = await SnapshotAsync(host, documentId, period, logWindow);

        // No state changes.
        after.Entries.Should().BeEquivalentTo(before.Entries);
        after.Turnovers.Should().BeEquivalentTo(before.Turnovers);
        after.Balances.Should().BeEquivalentTo(before.Balances);

        // No posting_log entries for forbidden operations.
        after.PostingLogUnpostAll.Should().BeEmpty();
        after.PostingLogRepostAll.Should().BeEmpty();

        // Baseline: Post log exists and remains stable.
        after.PostingLogPostAll.Should().HaveCount(1);
        after.PostingLogPostAll.Should().BeEquivalentTo(before.PostingLogPostAll);

        // No unexpected posting_log changes (no hidden side effects).
        after.PostingLogAll.Should().BeEquivalentTo(before.PostingLogAll);
    }

    private sealed record Outcome(Exception? Error);

    private static async Task<Outcome> RunOutcomeAsync(IHost host, TaskCompletionSource<bool> gate, Func<IServiceProvider, Task> action)
    {
        try
        {
            await gate.Task;

            await using var scope = host.Services.CreateAsyncScope();
            await action(scope.ServiceProvider);
            return new Outcome(null);
        }
        catch (Exception ex)
        {
            return new Outcome(ex);
        }
    }

    private sealed record Snapshot(
        IReadOnlyList<object> Entries,
        IReadOnlyList<object> Turnovers,
        IReadOnlyList<object> Balances,
        IReadOnlyList<object> PostingLogAll,
        IReadOnlyList<object> PostingLogPostAll,
        IReadOnlyList<object> PostingLogUnpostAll,
        IReadOnlyList<object> PostingLogRepostAll);

    private static async Task<Snapshot> SnapshotAsync(IHost host, Guid documentId, DateOnly period, PostingLogTestWindow logWindow)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var entries = await sp.GetRequiredService<IAccountingEntryReader>()
            .GetByDocumentAsync(documentId, CancellationToken.None);

        var turnovers = await sp.GetRequiredService<IAccountingTurnoverReader>()
            .GetForPeriodAsync(period, CancellationToken.None);

        var balances = await sp.GetRequiredService<IAccountingBalanceReader>()
            .GetForPeriodAsync(period, CancellationToken.None);

        var postingLog = sp.GetRequiredService<IPostingStateReader>();

        async Task<IReadOnlyList<object>> GetLogAsync(PostingOperation? op)
        {
            var page = await postingLog.GetPageAsync(new PostingStatePageRequest
            {
                FromUtc = logWindow.FromUtc,
                ToUtc = logWindow.ToUtc,
                DocumentId = documentId,
                Operation = op,
                Status = null,
                PageSize = 10_000,
            }, CancellationToken.None);

            return page.Records.Cast<object>().ToArray();
        }

        async Task<IReadOnlyList<object>> GetAllLogAsync()
        {
            var page = await postingLog.GetPageAsync(new PostingStatePageRequest
            {
                FromUtc = logWindow.FromUtc,
                ToUtc = logWindow.ToUtc,
                DocumentId = null,
                Operation = null,
                Status = null,
                PageSize = 10_000,
            }, CancellationToken.None);

            return page.Records.Cast<object>().ToArray();
        }

        return new Snapshot(
            Entries: entries.Cast<object>().ToArray(),
            Turnovers: turnovers.Cast<object>().ToArray(),
            Balances: balances.Cast<object>().ToArray(),
            PostingLogAll: await GetAllLogAsync(),
            PostingLogPostAll: await GetLogAsync(PostingOperation.Post),
            PostingLogUnpostAll: await GetLogAsync(PostingOperation.Unpost),
            PostingLogRepostAll: await GetLogAsync(PostingOperation.Repost));
    }

    private static async Task SeedMinimalCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var accounts = sp.GetRequiredService<IChartOfAccountsManagementService>();

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Revenue",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
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
            PostingOperation.Post,
            async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(documentId, periodUtc, chart.Get("50"), chart.Get("90.1"), amount);
            },
            CancellationToken.None);
    }
}

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
/// Policy-level coverage for operations against documents that belong to a closed accounting period.
///
/// Guard behavior itself is covered by <see cref="ClosedPeriodGuardTests"/>.
/// Here we lock down the *operational* invariants for Unpost/Repost:
/// - operation must throw
/// - MUST NOT create posting_log records for the forbidden operation
/// - MUST NOT change any persisted state (entries / turnovers / balances)
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ClosedPeriodPolicyUnpostRepostTests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task UnpostAsync_DocumentInClosedPeriod_Throws_AndDoesNotCreatePostingLogOrChangeState()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var logWindow = PostingLogTestWindow.Capture();

        var periodUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var period = DateOnly.FromDateTime(periodUtc);
        var documentId = Guid.CreateVersion7();

        await SeedMinimalCoaAsync(host);

        // Post BEFORE closing.
        await PostOnceAsync(host, documentId, periodUtc, amount: 100m);

        // Close month.
        await CloseMonthAsync(host, period);

        var before = await SnapshotAsync(host, documentId, period, logWindow);

        // Act
        var act = async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var unposting = scope.ServiceProvider.GetRequiredService<UnpostingService>();
            await unposting.UnpostAsync(documentId, CancellationToken.None);
        };

        // Assert
        await act
            .Should()
            .ThrowAsync<PostingPeriodClosedException>()
            .WithMessage($"*Posting is forbidden. Period is closed: {period:yyyy-MM-dd}*");

        var after = await SnapshotAsync(host, documentId, period, logWindow);

        // No state changes.
        after.Entries.Should().BeEquivalentTo(before.Entries);
        after.Turnovers.Should().BeEquivalentTo(before.Turnovers);
        after.Balances.Should().BeEquivalentTo(before.Balances);

        // No posting log created for Unpost (policy invariant).
        after.PostingLogUnpostAll.Should().BeEmpty();
        after.PostingLogUnpostInProgress.Should().BeEmpty();
        after.PostingLogUnpostCompleted.Should().BeEmpty();

        // The posting_log table must not be touched at all (no unrelated side effects).
        after.PostingLogAll.Should().BeEquivalentTo(before.PostingLogAll);

        // Baseline: Post log exists and remains stable.
        after.PostingLogPostAll.Should().HaveCount(1);
        after.PostingLogPostAll.Should().BeEquivalentTo(before.PostingLogPostAll);
    }

    [Fact]
    public async Task RepostAsync_DocumentInClosedPeriod_Throws_AndDoesNotCreatePostingLogOrChangeState()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var logWindow = PostingLogTestWindow.Capture();

        var periodUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var period = DateOnly.FromDateTime(periodUtc);
        var documentId = Guid.CreateVersion7();

        await SeedMinimalCoaAsync(host);

        // Post BEFORE closing.
        await PostOnceAsync(host, documentId, periodUtc, amount: 100m);

        // Close month.
        await CloseMonthAsync(host, period);

        var before = await SnapshotAsync(host, documentId, period, logWindow);

        // Act
        var act = async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var reposting = scope.ServiceProvider.GetRequiredService<RepostingService>();

            await reposting.RepostAsync(
                documentId,
                postNew: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    var debit = chart.Get("50");
                    var credit = chart.Get("90.1");

                    // Would change state if allowed.
                    ctx.Post(
                        documentId: documentId,
                        period: periodUtc,
                        debit: debit,
                        credit: credit,
                        amount: 200m);
                },
                CancellationToken.None);
        };

        // Assert
        await act
            .Should()
            .ThrowAsync<PostingPeriodClosedException>()
            .WithMessage($"*Posting is forbidden. Period is closed: {period:yyyy-MM-dd}*");

        var after = await SnapshotAsync(host, documentId, period, logWindow);

        // No state changes.
        after.Entries.Should().BeEquivalentTo(before.Entries);
        after.Turnovers.Should().BeEquivalentTo(before.Turnovers);
        after.Balances.Should().BeEquivalentTo(before.Balances);

        // No posting log created for Repost (policy invariant).
        after.PostingLogRepostAll.Should().BeEmpty();
        after.PostingLogRepostInProgress.Should().BeEmpty();
        after.PostingLogRepostCompleted.Should().BeEmpty();

        // The posting_log table must not be touched at all (no unrelated side effects).
        after.PostingLogAll.Should().BeEquivalentTo(before.PostingLogAll);

        // Baseline: Post log exists and remains stable.
        after.PostingLogPostAll.Should().HaveCount(1);
        after.PostingLogPostAll.Should().BeEquivalentTo(before.PostingLogPostAll);
    }

    private sealed record Snapshot(
        IReadOnlyList<object> Entries,
        IReadOnlyList<object> Turnovers,
        IReadOnlyList<object> Balances,
        IReadOnlyList<object> PostingLogAll,
        IReadOnlyList<object> PostingLogPostAll,
        IReadOnlyList<object> PostingLogUnpostAll,
        IReadOnlyList<object> PostingLogUnpostInProgress,
        IReadOnlyList<object> PostingLogUnpostCompleted,
        IReadOnlyList<object> PostingLogRepostAll,
        IReadOnlyList<object> PostingLogRepostInProgress,
        IReadOnlyList<object> PostingLogRepostCompleted);

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

        async Task<IReadOnlyList<object>> GetLogAsync(PostingOperation? op, PostingStateStatus? status = null)
        {
            var page = await postingLog.GetPageAsync(new PostingStatePageRequest
            {
                FromUtc = logWindow.FromUtc,
                ToUtc = logWindow.ToUtc,
                DocumentId = documentId,
                Operation = op,
                Status = status,
                PageSize = 10_000,
            }, CancellationToken.None);

            // We only compare shapes/values; concrete types are not important for these assertions.
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

            // We only compare shapes/values; concrete types are not important for these assertions.
            return page.Records.Cast<object>().ToArray();
        }

        return new Snapshot(
            Entries: entries.Cast<object>().ToArray(),
            Turnovers: turnovers.Cast<object>().ToArray(),
            Balances: balances.Cast<object>().ToArray(),
            PostingLogAll: await GetAllLogAsync(),
            PostingLogPostAll: await GetLogAsync(PostingOperation.Post),
            PostingLogUnpostAll: await GetLogAsync(PostingOperation.Unpost),
            PostingLogUnpostInProgress: await GetLogAsync(PostingOperation.Unpost, PostingStateStatus.InProgress),
            PostingLogUnpostCompleted: await GetLogAsync(PostingOperation.Unpost, PostingStateStatus.Completed),
            PostingLogRepostAll: await GetLogAsync(PostingOperation.Repost),
            PostingLogRepostInProgress: await GetLogAsync(PostingOperation.Repost, PostingStateStatus.InProgress),
            PostingLogRepostCompleted: await GetLogAsync(PostingOperation.Repost, PostingStateStatus.Completed));
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

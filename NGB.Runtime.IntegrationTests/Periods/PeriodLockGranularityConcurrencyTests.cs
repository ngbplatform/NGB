using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.Periods;
using NGB.Persistence.Readers.PostingState;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(PostgresCollection.Name)]
public sealed class PeriodLockGranularityConcurrencyTests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task CloseMonth_Jan_WhilePosting_Feb_ShouldNotBlockAndBothSucceed()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var janStart = new DateOnly(2026, 1, 1);
        var febStart = new DateOnly(2026, 2, 1);

        var janUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var febUtc = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        await SeedMinimalCoaAsync(host);

        var postDocId = Guid.CreateVersion7();

        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Act
        var closeTask = RunCloseMonthAsync(host, janStart, gate);
        var postTask = RunPostOutcomeAsync(host, postDocId, febUtc, gate);

        gate.SetResult(true);

        await Task.WhenAll(closeTask, postTask)
            .WaitAsync(TimeSpan.FromSeconds(30));

        var postOutcome = await postTask;

        // Assert
        postOutcome.Succeeded.Should().BeTrue($"February posting must not be blocked by January close. Error: {postOutcome.Error}");

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var closedReader = sp.GetRequiredService<IClosedPeriodReader>();
        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        var postingLog = sp.GetRequiredService<IPostingStateReader>();

        // 1) January is closed.
        var closed = await closedReader.GetClosedAsync(janStart, janStart, CancellationToken.None);
        closed.Should().ContainSingle(p => p.Period == janStart);

        // 2) February posting exists.
        var entries = await entryReader.GetByDocumentAsync(postDocId, CancellationToken.None);
        entries.Should().HaveCount(1);
        entries.Single().Period.Date.Should().Be(febUtc.Date);

        // 3) Posting log exists for the February post.
        var page = await postingLog.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = DateTime.UtcNow.AddHours(-2),
            ToUtc = DateTime.UtcNow.AddHours(2),
            DocumentId = postDocId,
            Operation = PostingOperation.Post,
            PageSize = 20
        }, CancellationToken.None);

        page.Records.Should().HaveCount(1);
        page.Records.Single().CompletedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task CloseMonth_Jan_WhilePosting_Jan_Serializes_AndEndsClosed()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var janStart = new DateOnly(2026, 1, 1);
        var janUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        await SeedMinimalCoaAsync(host);

        var postDocId = Guid.CreateVersion7();

        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Act: same period. One will "win" depending on ordering, but system must not deadlock and must end closed.
        var closeTask = RunCloseMonthAsync(host, janStart, gate);
        var postTask = RunPostOutcomeAsync(host, postDocId, janUtc, gate);

        gate.SetResult(true);

        await Task.WhenAll(closeTask, postTask)
            .WaitAsync(TimeSpan.FromSeconds(30));

        var postOutcome = await postTask;

        // Assert: period is closed at the end, and posting either applied (before close) or rejected without side effects.
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var closedReader = sp.GetRequiredService<IClosedPeriodReader>();
        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        var postingLog = sp.GetRequiredService<IPostingStateReader>();

        var closed = await closedReader.GetClosedAsync(janStart, janStart, CancellationToken.None);
        closed.Should().ContainSingle(p => p.Period == janStart);

        var entries = await entryReader.GetByDocumentAsync(postDocId, CancellationToken.None);

        var page = await postingLog.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = DateTime.UtcNow.AddHours(-2),
            ToUtc = DateTime.UtcNow.AddHours(2),
            DocumentId = postDocId,
            Operation = PostingOperation.Post,
            PageSize = 20
        }, CancellationToken.None);

        if (postOutcome.Succeeded)
        {
            // Post happened before close.
            entries.Should().HaveCount(1);
            page.Records.Should().HaveCount(1);
            page.Records.Single().CompletedAtUtc.Should().NotBeNull();
        }
        else
        {
            // Post was rejected due to closed period: no posting_log and no entries.
            postOutcome.Error.Should().BeOfType<PostingPeriodClosedException>();
            postOutcome.Error!.Message.Should().Contain("Period is closed");

            entries.Should().BeEmpty();
            page.Records.Should().BeEmpty();
        }
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
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Income",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);
    }

    private static async Task RunCloseMonthAsync(IHost host, DateOnly period, TaskCompletionSource<bool> gate)
    {
        await gate.Task;

        await using var scope = host.Services.CreateAsyncScope();
        var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();

        await closing.CloseMonthAsync(period, closedBy: "test", ct: CancellationToken.None);
    }

    private static async Task<Outcome> RunPostOutcomeAsync(IHost host, Guid documentId, DateTime periodUtc, TaskCompletionSource<bool> gate)
    {
        await gate.Task;

        try
        {
            await using var scope = host.Services.CreateAsyncScope();
            var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

            await posting.PostAsync(
                postingAction: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    var debit = chart.Get("50");
                    var credit = chart.Get("90.1");

                    ctx.Post(documentId, periodUtc, debit, credit, 10m);
                },
                ct: CancellationToken.None);

            return Outcome.Success();
        }
        catch (Exception ex)
        {
            return Outcome.Fail(ex);
        }
    }

    private sealed record Outcome(bool Succeeded, Exception? Error)
    {
        public static Outcome Success() => new(true, null);
        public static Outcome Fail(Exception ex) => new(false, ex);
    }
}

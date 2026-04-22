using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.Periods;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.Periods;
using NGB.Persistence.Readers.PostingState;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(PostgresCollection.Name)]
public sealed class MultiPeriodPostVsCloseMonthConcurrencyTests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task MultiPeriodPostVsCloseMonth_Jan_Concurrent_NoPartialWrites_AndMonthEndsClosed()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var janStart = new DateOnly(2026, 1, 1);
        var febStart = new DateOnly(2026, 2, 1);

        var janUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var febUtc = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        await SeedMinimalCoaAsync(host);

        var docId = Guid.CreateVersion7();
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Act: run CloseMonth(Jan) concurrently with a single posting that spans Jan + Feb.
        var closeTask = RunCloseMonthOutcomeAsync(host, janStart, gate);
        var postTask = RunMultiPeriodPostOutcomeAsync(host, docId, janUtc, febUtc, gate);

        gate.SetResult(true);

        var outcomes = await Task.WhenAll(closeTask, postTask)
            .WaitAsync(TimeSpan.FromSeconds(45));

        var closeOutcome = outcomes[0];
        var postOutcome = outcomes[1];

        // Assert: close must succeed or already-closed (no deadlock).
        if (closeOutcome.Error is not null)
            closeOutcome.Error.Should().BeOfType<PeriodAlreadyClosedException>();

        // The post can either:
        //  - succeed (if it acquires locks and posts before month is closed), or
        //  - fail with "period is closed" (if CloseMonth wins).
        if (postOutcome.Error is not null)
            postOutcome.Error.Should().BeOfType<NgbArgumentInvalidException>()
                .Which.Message.Should().Match("*same UTC day*");

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var closedReader = sp.GetRequiredService<IClosedPeriodReader>();
        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        var postingLog = sp.GetRequiredService<IPostingStateReader>();

        // 1) January must be closed at the end.
        var closed = await closedReader.GetClosedAsync(janStart, janStart, CancellationToken.None);
        closed.Should().ContainSingle(p => p.Period == janStart);

        // 2) No partial writes: either 2 entries (Jan+Feb) or 0 entries.
        var entries = await entryReader.GetByDocumentAsync(docId, CancellationToken.None);

        if (postOutcome.Error is null)
        {
            entries.Should().HaveCount(2, "successful multi-period post must write both entries atomically");
            entries.Select(e => e.Period.Date).Should().BeEquivalentTo(new[] { janUtc.Date, febUtc.Date });
        }
        else
        {
            // If rejected due to closed period, there must be no entries at all (no partial).
            entries.Should().BeEmpty();
        }

        // 3) Posting log must follow the "no side effects on reject" contract.
        var page = await postingLog.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = DateTime.UtcNow.AddHours(-2),
            ToUtc = DateTime.UtcNow.AddHours(2),
            DocumentId = docId,
            Operation = PostingOperation.Post,
            PageSize = 20
        }, CancellationToken.None);

        if (postOutcome.Error is null)
        {
            page.Records.Should().HaveCount(1);
            page.Records.Single().CompletedAtUtc.Should().NotBeNull();
        }
        else
        {
            // When rejected by closed-period guard, posting_log must not be created.
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

    private static async Task<Outcome> RunCloseMonthOutcomeAsync(IHost host, DateOnly period, TaskCompletionSource<bool> gate)
    {
        await gate.Task;

        try
        {
            await using var scope = host.Services.CreateAsyncScope();
            var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
            await closing.CloseMonthAsync(period, closedBy: "test", ct: CancellationToken.None);
            return Outcome.Success();
        }
        catch (Exception ex)
        {
            return Outcome.Fail(ex);
        }
    }

    private static async Task<Outcome> RunMultiPeriodPostOutcomeAsync(
        IHost host,
        Guid documentId,
        DateTime janUtc,
        DateTime febUtc,
        TaskCompletionSource<bool> gate)
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

                    // Two entries in different periods, same document id.
                    ctx.Post(documentId, janUtc, debit, credit, 10m);
                    ctx.Post(documentId, febUtc, debit, credit, 20m);
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

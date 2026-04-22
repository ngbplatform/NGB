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
public sealed class ThreePeriodRace_CloseMonthVsMultiPeriodPost_P3Tests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task CloseMonth_Jan_And_CloseMonth_Feb_Concurrent_With_Post_Jan_NoDeadlock_NoPartialWrites()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var jan = new DateOnly(2026, 1, 1);
        var feb = new DateOnly(2026, 2, 1);

        var janUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        // All entries must belong to the same UTC day (validator invariant).
        // We still run CloseMonth for another month concurrently to stress period locking under mixed workloads.

        await SeedMinimalCoaAsync(host);

        var docId = Guid.CreateVersion7();
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var closeJanTask = RunCloseMonthOutcomeAsync(host, jan, gate);
        var closeFebTask = RunCloseMonthOutcomeAsync(host, feb, gate);
        var postTask = RunPostOutcomeAsync(host, docId, janUtc, gate);

        gate.SetResult(true);

        var outcomes = await Task.WhenAll(closeJanTask, closeFebTask, postTask)
            .WaitAsync(TimeSpan.FromSeconds(45));

        var closeJan = outcomes[0];
        var closeFeb = outcomes[1];
        var post = outcomes[2];

        closeJan.Error.Should().BeNull($"CloseMonth(Jan) must succeed. Error: {closeJan.Error}");
        closeFeb.Error.Should().BeNull($"CloseMonth(Feb) must succeed. Error: {closeFeb.Error}");

        // Post can legitimately be rejected if Jan closes first.
        if (post.Error is not null)
            post.Error.Should().BeOfType<PostingPeriodClosedException>();

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var closedReader = sp.GetRequiredService<IClosedPeriodReader>();
        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        var postingLog = sp.GetRequiredService<IPostingStateReader>();

        var closed = await closedReader.GetClosedAsync(jan, feb, CancellationToken.None);
        closed.Should().ContainSingle(x => x.Period == jan);
        closed.Should().ContainSingle(x => x.Period == feb);
        // We didn't close any other months.
        closed.Should().HaveCount(2);

        // No partial writes: either 3 entries (Jan) or 0 entries.
        var entries = await entryReader.GetByDocumentAsync(docId, CancellationToken.None);

        var page = await postingLog.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = DateTime.UtcNow.AddHours(-2),
            ToUtc = DateTime.UtcNow.AddHours(2),
            DocumentId = docId,
            Operation = PostingOperation.Post,
            PageSize = 20
        }, CancellationToken.None);

        if (post.Error is null)
        {
            entries.Should().HaveCount(3);
            entries.Select(e => e.Period.Date).Should().AllBeEquivalentTo(janUtc.Date);

            page.Records.Should().HaveCount(1);
            page.Records.Single().CompletedAtUtc.Should().NotBeNull();
        }
        else
        {
            entries.Should().BeEmpty("rejected posting must have no register side effects");
            page.Records.Should().BeEmpty("rejected posting must not create posting_log");
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
            return Outcome.Ok();
        }
        catch (Exception ex)
        {
            return Outcome.Fail(ex);
        }
    }

    private static async Task<Outcome> RunPostOutcomeAsync(
        IHost host,
        Guid documentId,
        DateTime janUtc,
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

                    // Three entries, same UTC day => valid by invariant.
                    ctx.Post(documentId, janUtc, debit, credit, 10m);
                    ctx.Post(documentId, janUtc, debit, credit, 20m);
                    ctx.Post(documentId, janUtc, debit, credit, 30m);
                },
                ct: CancellationToken.None);

            return Outcome.Ok();
        }
        catch (Exception ex)
        {
            return Outcome.Fail(ex);
        }
    }

    private sealed record Outcome(Exception? Error)
    {
        public static Outcome Ok() => new(Error: null);
        public static Outcome Fail(Exception ex) => new(ex);
    }
}

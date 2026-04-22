using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Definitions;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.Periods;
using NGB.Persistence.Readers.PostingState;
using NGB.Runtime.Accounts;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using Xunit;
using NGB.Runtime.Posting;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(PostgresCollection.Name)]
public sealed class DoubleCloseMonthTwoPeriodsConcurrencyTests(PostgresTestFixture fixture)
{
    private const string TypeCode = "it_doc_tx";

    [Fact]
    public async Task CloseMonth_Jan_And_CloseMonth_Feb_Concurrent_With_Posts_NoDeadlock_BothMonthsClosed_NoCrossPeriodLockCollision()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(
            fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>());

        var jan = new DateOnly(2026, 1, 1);
        var feb = new DateOnly(2026, 2, 1);

        var janUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var febUtc = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        await SeedMinimalCoaAsync(host);

        Guid janDocId;
        Guid febDocId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

            janDocId = await drafts.CreateDraftAsync(TypeCode, number: null, dateUtc: janUtc, manageTransaction: true, ct: CancellationToken.None);
            febDocId = await drafts.CreateDraftAsync(TypeCode, number: null, dateUtc: febUtc, manageTransaction: true, ct: CancellationToken.None);
        }

        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Act: two closes in different periods + two posts in those same periods.
        var tasks = new[]
        {
            RunCloseMonthOutcomeAsync(host, jan, gate),
            RunCloseMonthOutcomeAsync(host, feb, gate),
            RunPostOutcomeAsync(host, janDocId, janUtc, gate),
            RunPostOutcomeAsync(host, febDocId, febUtc, gate),
        };

        gate.SetResult(true);

        var outcomes = await Task.WhenAll(tasks)
            .WaitAsync(TimeSpan.FromSeconds(45));

        // Assert: no deadlock and both months end up closed.
        var closeJan = outcomes[0];
        var closeFeb = outcomes[1];
        var postJan = outcomes[2];
        var postFeb = outcomes[3];

        closeJan.Error.Should().BeNull();
        closeFeb.Error.Should().BeNull();

        await using var verifyScope = host.Services.CreateAsyncScope();
        var sp = verifyScope.ServiceProvider;

        var closedReader = sp.GetRequiredService<IClosedPeriodReader>();
        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        var postingLog = sp.GetRequiredService<IPostingStateReader>();

        var closed = await closedReader.GetClosedAsync(jan, feb, CancellationToken.None);
        closed.Should().ContainSingle(x => x.Period == jan);
        closed.Should().ContainSingle(x => x.Period == feb);

        // Posting results are allowed to race with the close *of their own period*:
        // - succeed => entries exist + posting_log exists
        // - rejected due to closed period => no entries + no posting_log
        await AssertPostOutcomeAsync(entryReader, postingLog, janDocId, expectedUtcDate: janUtc.Date, postJan);
        await AssertPostOutcomeAsync(entryReader, postingLog, febDocId, expectedUtcDate: febUtc.Date, postFeb);
    }

    private static async Task AssertPostOutcomeAsync(
        IAccountingEntryReader entryReader,
        IPostingStateReader postingLog,
        Guid documentId,
        DateTime expectedUtcDate,
        Outcome outcome)
    {
        var entries = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);

        var page = await postingLog.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = DateTime.UtcNow.AddHours(-2),
            ToUtc = DateTime.UtcNow.AddHours(2),
            DocumentId = documentId,
            Operation = PostingOperation.Post,
            PageSize = 20
        }, CancellationToken.None);

        if (outcome.Error is null)
        {
            entries.Should().HaveCount(1);
            entries.Single().Period.Date.Should().Be(expectedUtcDate);

            page.Records.Should().HaveCount(1);
            page.Records.Single().CompletedAtUtc.Should().NotBeNull();
        }
        else
        {
            outcome.Error.Should().BeOfType<PostingPeriodClosedException>();
            outcome.Error!.Message.Should().Contain("closed", "expected closed-period rejection");

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

    private static async Task<Outcome> RunPostOutcomeAsync(IHost host, Guid documentId, DateTime periodUtc, TaskCompletionSource<bool> gate)
    {
        await gate.Task;

        try
        {
            await using var scope = host.Services.CreateAsyncScope();
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

            await docs.PostAsync(
                documentId,
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

    private sealed record Outcome(Exception? Error)
    {
        public static Outcome Success() => new(Error: null);
        public static Outcome Fail(Exception ex) => new(Error: ex);
    }
}

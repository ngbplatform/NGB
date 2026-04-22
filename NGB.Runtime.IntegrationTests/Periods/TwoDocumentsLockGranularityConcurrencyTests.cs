using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Definitions;
using NGB.Persistence.Documents;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.Periods;
using NGB.Persistence.Readers.PostingState;
using NGB.Runtime.Accounts;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(PostgresCollection.Name)]
public sealed class TwoDocumentsLockGranularityConcurrencyTests(PostgresTestFixture fixture)
{
    private const string TypeCode = "it_doc_tx";

    [Fact]
    public async Task Repost_DocA_And_Unpost_DocB_Concurrent_SamePeriod_NoDeadlock_BothSucceed_NoCrossInterference()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(
            fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>());

        var janStart = new DateOnly(2026, 1, 1);
        var janUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        await SeedMinimalCoaAsync(host);

        Guid docA;
        Guid docB;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

            docA = await drafts.CreateDraftAsync(TypeCode, number: null, dateUtc: janUtc, manageTransaction: true, ct: CancellationToken.None);
            docB = await drafts.CreateDraftAsync(TypeCode, number: null, dateUtc: janUtc, manageTransaction: true, ct: CancellationToken.None);

            // Initial post for both documents (single-entry each).
            await docs.PostAsync(docA,
                postingAction: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(docA, janUtc, chart.Get("50"), chart.Get("90.1"), 100m);
                },
                ct: CancellationToken.None);

            await docs.PostAsync(docB,
                postingAction: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(docB, janUtc, chart.Get("50"), chart.Get("90.1"), 50m);
                },
                ct: CancellationToken.None);
        }

        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Act: concurrently run Repost on A and Unpost on B (same period).
        var tasks = new[]
        {
            RunRepostAsync(host, docA, janUtc, gate, newAmount: 200m),
            RunUnpostAsync(host, docB, gate)
        };

        gate.SetResult(true);

        var outcomes = await Task.WhenAll(tasks)
            .WaitAsync(TimeSpan.FromSeconds(45));

        // Assert: both succeed (we're validating "no deadlock" + correct isolation).
        outcomes.Should().HaveCount(2);
        outcomes.All(o => o.Error is null).Should().BeTrue(
            $"unexpected errors: {string.Join(", ", outcomes.Where(o => o.Error is not null).Select(o => o.Error!.Message))}");

        await using var verifyScope = host.Services.CreateAsyncScope();
        var sp = verifyScope.ServiceProvider;

        var closedReader = sp.GetRequiredService<IClosedPeriodReader>();
        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        var postingLog = sp.GetRequiredService<IPostingStateReader>();
        var docsReader = sp.GetRequiredService<IDocumentRepository>();

        // Period must remain open (we didn't close it), and operations should not close it implicitly.
        var closed = await closedReader.GetClosedAsync(janStart, janStart, CancellationToken.None);
        closed.Should().BeEmpty();

        // DocA: Repost => original + storno + new = 3 entries; still Posted
        var entriesA = await entryReader.GetByDocumentAsync(docA, CancellationToken.None);
        entriesA.Should().HaveCount(3);
        entriesA.Count(e => e.IsStorno).Should().Be(1);

	    var docAState = await docsReader.GetAsync(docA, CancellationToken.None);
	    docAState.Should().NotBeNull("docA must exist after initial Post and concurrent Repost");
	    docAState!.Status.Should().Be(NGB.Core.Documents.DocumentStatus.Posted);

        // DocB: Unpost => original + storno = 2 entries; ends Draft
        var entriesB = await entryReader.GetByDocumentAsync(docB, CancellationToken.None);
        entriesB.Should().HaveCount(2);
        entriesB.Count(e => e.IsStorno).Should().Be(1);

	    var docBState = await docsReader.GetAsync(docB, CancellationToken.None);
	    docBState.Should().NotBeNull("docB must exist after initial Post and concurrent Unpost");
	    docBState!.Status.Should().Be(NGB.Core.Documents.DocumentStatus.Draft);

        // posting_log: exactly one completed row per operation/doc
        var fromUtc = DateTime.UtcNow.AddHours(-3);
        var toUtc = DateTime.UtcNow.AddHours(3);

        var repostLog = await postingLog.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            DocumentId = docA,
            Operation = PostingOperation.Repost,
            PageSize = 20
        }, CancellationToken.None);

        repostLog.Records.Should().HaveCount(1);
        repostLog.Records.Single().CompletedAtUtc.Should().NotBeNull();

        var unpostLog = await postingLog.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            DocumentId = docB,
            Operation = PostingOperation.Unpost,
            PageSize = 20
        }, CancellationToken.None);

        unpostLog.Records.Should().HaveCount(1);
        unpostLog.Records.Single().CompletedAtUtc.Should().NotBeNull();
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

    private static async Task<Outcome> RunUnpostAsync(IHost host, Guid documentId, TaskCompletionSource<bool> gate)
    {
        await gate.Task;
        try
        {
            await using var scope = host.Services.CreateAsyncScope();
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            await docs.UnpostAsync(documentId, CancellationToken.None);
            return Outcome.Ok();
        }
        catch (Exception ex)
        {
            return Outcome.Fail(ex);
        }
    }

    private static async Task<Outcome> RunRepostAsync(
        IHost host,
        Guid documentId,
        DateTime periodUtc,
        TaskCompletionSource<bool> gate,
        decimal newAmount)
    {
        await gate.Task;
        try
        {
            await using var scope = host.Services.CreateAsyncScope();
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

            await docs.RepostAsync(
                documentId,
                postNew: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(documentId, periodUtc, chart.Get("50"), chart.Get("90.1"), newAmount);
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
        public static Outcome Fail(Exception ex) => new(Error: ex);
    }
}

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.PostingState;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.Documents;
using NGB.Runtime.Documents.Workflow;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Posting;

[Collection(PostgresCollection.Name)]
public sealed class UnpostVsRepostRaceTests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task UnpostVsRepost_ConcurrentOperations_AreSerialized_NoDoubleStorno_NoDuplicatePostingLog()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var periodUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var period = new DateOnly(2026, 1, 1);

        await SeedMinimalCoaAsync(host);

        Guid documentId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

            documentId = await drafts.CreateDraftAsync(typeCode: "test", number: null, dateUtc: periodUtc, manageTransaction: true, ct: CancellationToken.None);

            // Original post: Cash D / Income C 100
            await docs.PostAsync(
                documentId,
                postingAction: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(documentId, periodUtc, chart.Get("50"), chart.Get("90.1"), 100m);
                },
                ct: CancellationToken.None);
        }

        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Act: run Unpost and Repost concurrently (same documentId).
        var tasks = new[]
        {
            RunUnpostAsync(host, documentId, gate),
            RunRepostAsync(host, documentId, periodUtc, gate, newAmount: 200m)
        };

        gate.SetResult(true);

        var outcomes = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));

        // Repost may legitimately fail if Unpost wins the race first (document becomes Draft).
        outcomes.Should().HaveCount(2);
        outcomes.All(o => o.Error is null || o.Error is DocumentWorkflowStateMismatchException).Should().BeTrue(
            $"unexpected exception types: {string.Join(", ", outcomes.Where(o => o.Error is not null).Select(o => o.Error!.GetType().Name))}");

        await using var verifyScope = host.Services.CreateAsyncScope();
        var sp = verifyScope.ServiceProvider;

        var docRepo = sp.GetRequiredService<IDocumentRepository>();
        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        var postingLog = sp.GetRequiredService<IPostingStateReader>();
        var trialBalance = sp.GetRequiredService<ITrialBalanceReader>();

        // 1) Document ends Draft (Unpost eventually applies).
        var doc = await docRepo.GetAsync(documentId, CancellationToken.None);
        doc.Should().NotBeNull();
        doc!.Status.Should().Be(DocumentStatus.Draft);

        // 2) No duplicates in posting_log per operation.
        var fromUtc = DateTime.UtcNow.AddHours(-2);
        var toUtc = DateTime.UtcNow.AddHours(2);

        var unpostLog = await postingLog.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            DocumentId = documentId,
            Operation = PostingOperation.Unpost,
            PageSize = 20
        }, CancellationToken.None);

        unpostLog.Records.Should().HaveCount(1);
        unpostLog.Records.Single().CompletedAtUtc.Should().NotBeNull();

        var repostLog = await postingLog.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            DocumentId = documentId,
            Operation = PostingOperation.Repost,
            PageSize = 20
        }, CancellationToken.None);

        repostLog.Records.Should().HaveCountLessThanOrEqualTo(1);
        if (repostLog.Records.Count == 1)
            repostLog.Records.Single().CompletedAtUtc.Should().NotBeNull();

        // 3) Entries must match one of two valid serializations:
        //    A) Unpost first => total=2, storno=1
        //    B) Repost first then Unpost => total=6, storno=4
        var entries = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);

        var stornoCount = entries.Count(e => e.IsStorno);
        var isCaseA = entries.Count == 2 && stornoCount == 1;
        var isCaseB = entries.Count == 6 && stornoCount == 4;

        (isCaseA || isCaseB).Should().BeTrue($"unexpected entry shape: total={entries.Count}, storno={stornoCount}");

        // 4) Trial balance ends net-zero for the period in both cases.
        var tb = await trialBalance.GetAsync(period, period, CancellationToken.None);

        tb.Should().ContainSingle(r => r.AccountCode == "50" && r.ClosingBalance == 0m);
        tb.Should().ContainSingle(r => r.AccountCode == "90.1" && r.ClosingBalance == 0m);
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
            return Outcome.Success();
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

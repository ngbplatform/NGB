using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Accounting.Reports.TrialBalance;
using NGB.Core.Documents;
using NGB.Definitions;
using NGB.Persistence.Documents;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.PostingState;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentPostingService_UnpostRepost_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task UnpostAsync_FromPosted_PostsStorno_SetsDraft_AndReturnsTrialBalanceToZero()
    {
        var window = PostingLogTestWindow.Capture();

        using var host = CreateHost();
        await SeedMinimalCoaAsync(host);

        var dateUtc = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var period = new DateOnly(2026, 1, 1);

        var docId = await CreateDraftAsync(host, dateUtc, number: "INV-U1");

        await PostCashRevenueAsync(host, docId, dateUtc, amount: 100m);

        // Sanity: posted effect exists.
        var tbAfterPost = await ReadTrialBalanceAsync(host, period);
        tbAfterPost.Single(r => r.AccountCode == "50").ClosingBalance.Should().Be(100m);
        tbAfterPost.Single(r => r.AccountCode == "90.1").ClosingBalance.Should().Be(-100m);

        // Act
        await UnpostAsync(host, docId);

        // Assert (document)
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var doc = await sp.GetRequiredService<IDocumentRepository>().GetAsync(docId, CancellationToken.None);
        doc.Should().NotBeNull();
        doc!.Status.Should().Be(DocumentStatus.Draft);
        doc.PostedAtUtc.Should().BeNull();

        // Assert (entries)
        var entries = await sp.GetRequiredService<IAccountingEntryReader>().GetByDocumentAsync(docId, CancellationToken.None);
        entries.Should().HaveCount(2);
        entries.Count(e => e.IsStorno).Should().Be(1);

        var original = entries.Single(e => !e.IsStorno);
        var storno = entries.Single(e => e.IsStorno);

        original.Debit.Code.Should().Be("50");
        original.Credit.Code.Should().Be("90.1");
        storno.Debit.Code.Should().Be("90.1");
        storno.Credit.Code.Should().Be("50");
        storno.Amount.Should().Be(original.Amount);
        storno.Period.Should().Be(original.Period);

        // Assert (trial balance returns to zero; turnovers stay non-zero, which is fine)
        var tbAfterUnpost = await sp.GetRequiredService<ITrialBalanceReader>()
            .GetAsync(fromInclusive: period, toInclusive: period, CancellationToken.None);

        tbAfterUnpost.Single(r => r.AccountCode == "50").ClosingBalance.Should().Be(0m);
        tbAfterUnpost.Single(r => r.AccountCode == "90.1").ClosingBalance.Should().Be(0m);

        // Assert (posting_log)
        var logs = await ReadPostingLogsAsync(sp, window, docId);
        logs.Should().ContainSingle(l => l.Operation == PostingOperation.Unpost && l.Status == PostingStateStatus.Completed);
    }

    [Fact]
    public async Task RepostAsync_ReplacesFinancialEffect_WithNewAmount_AndSecondRepostIsStrictNoOp()
    {
        var window = PostingLogTestWindow.Capture();

        using var host = CreateHost();
        await SeedMinimalCoaAsync(host);

        var dateUtc = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var period = new DateOnly(2026, 1, 1);

        var docId = await CreateDraftAsync(host, dateUtc, number: "INV-R1");

        await PostCashRevenueAsync(host, docId, dateUtc, amount: 100m);

        // Act: repost to a new amount.
        await RepostCashRevenueAsync(host, docId, dateUtc, newAmount: 250m);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            var doc = await sp.GetRequiredService<IDocumentRepository>().GetAsync(docId, CancellationToken.None);
            doc!.Status.Should().Be(DocumentStatus.Posted);
            doc.PostedAtUtc.Should().NotBeNull();

            var entries = await sp.GetRequiredService<IAccountingEntryReader>().GetByDocumentAsync(docId, CancellationToken.None);
            entries.Should().HaveCount(3, "repost must add exactly storno(old) + new entry");
            entries.Count(e => e.IsStorno).Should().Be(1);

            var tb = await sp.GetRequiredService<ITrialBalanceReader>()
                .GetAsync(fromInclusive: period, toInclusive: period, CancellationToken.None);

            tb.Single(r => r.AccountCode == "50").ClosingBalance.Should().Be(250m);
            tb.Single(r => r.AccountCode == "90.1").ClosingBalance.Should().Be(-250m);

            var logs = await ReadPostingLogsAsync(sp, window, docId);
            logs.Count(l => l.Operation == PostingOperation.Repost && l.Status == PostingStateStatus.Completed).Should().Be(1);
        }

        // Act: second repost with a different amount must be a strict no-op because posting_log is idempotent.
        await RepostCashRevenueAsync(host, docId, dateUtc, newAmount: 999m);

        // Assert: still exactly 3 entries and effect is still 250.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            var entries = await sp.GetRequiredService<IAccountingEntryReader>().GetByDocumentAsync(docId, CancellationToken.None);
            entries.Should().HaveCount(3);

            var tb = await sp.GetRequiredService<ITrialBalanceReader>()
                .GetAsync(fromInclusive: period, toInclusive: period, CancellationToken.None);

            tb.Single(r => r.AccountCode == "50").ClosingBalance.Should().Be(250m);
            tb.Single(r => r.AccountCode == "90.1").ClosingBalance.Should().Be(-250m);

            var logs = await ReadPostingLogsAsync(sp, window, docId);
            logs.Count(l => l.Operation == PostingOperation.Repost && l.Status == PostingStateStatus.Completed).Should().Be(1);
        }
    }

    [Fact]
    public async Task RepostAsync_WhenNewPostingIsInvalid_RollsBackEverything_StatusStaysPosted_NoEntriesNoPostingLog()
    {
        var window = PostingLogTestWindow.Capture();

        using var host = CreateHost();
        await SeedMinimalCoaAsync(host);

        var dateUtc = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var period = new DateOnly(2026, 1, 1);

        var docId = await CreateDraftAsync(host, dateUtc, number: "INV-RBAD");
        await PostCashRevenueAsync(host, docId, dateUtc, amount: 100m);

        DateTime postedAtBefore;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            postedAtBefore = (await scope.ServiceProvider.GetRequiredService<IDocumentRepository>()
                .GetAsync(docId, CancellationToken.None))!.PostedAtUtc!.Value;
        }

        // Act
        Func<Task> act = () => RepostInvalidTwoDaysAsync(host, docId, dateUtc, newAmount: 10m);

        // Assert
        await act.Should().ThrowAsync<NgbArgumentInvalidException>()
            .WithMessage("*same UTC day*");

        await using var scope2 = host.Services.CreateAsyncScope();
        var sp2 = scope2.ServiceProvider;

        var doc = await sp2.GetRequiredService<IDocumentRepository>().GetAsync(docId, CancellationToken.None);
        doc!.Status.Should().Be(DocumentStatus.Posted);
        doc.PostedAtUtc.Should().Be(postedAtBefore, "failed repost must not mutate document timestamps");

        var entries = await sp2.GetRequiredService<IAccountingEntryReader>().GetByDocumentAsync(docId, CancellationToken.None);
        entries.Should().HaveCount(1, "failed repost must not write storno or new entries");
        entries.Single().IsStorno.Should().BeFalse();

        var tb = await sp2.GetRequiredService<ITrialBalanceReader>()
            .GetAsync(fromInclusive: period, toInclusive: period, CancellationToken.None);

        tb.Single(r => r.AccountCode == "50").ClosingBalance.Should().Be(100m);
        tb.Single(r => r.AccountCode == "90.1").ClosingBalance.Should().Be(-100m);

        var logs = await ReadPostingLogsAsync(sp2, window, docId);
        logs.Should().NotBeEmpty("post is completed");
        logs.Any(l => l.Operation == PostingOperation.Repost).Should().BeFalse("failed repost must rollback posting_log row");
    }

    [Fact]
    public async Task Concurrent_Repost_TwoCalls_ResultInSingleEffect_NoDuplicateEntries_OnePostingLog()
    {
        var window = PostingLogTestWindow.Capture();

        using var host = CreateHost();
        await SeedMinimalCoaAsync(host);

        var dateUtc = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var period = new DateOnly(2026, 1, 1);
        var docId = await CreateDraftAsync(host, dateUtc, number: "INV-RCONC");

        await PostCashRevenueAsync(host, docId, dateUtc, amount: 100m);

        using var barrier = new Barrier(3);

        var t1 = Task.Run(async () =>
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            await RepostCashRevenueAsync(host, docId, dateUtc, newAmount: 250m);
        });

        var t2 = Task.Run(async () =>
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            await RepostCashRevenueAsync(host, docId, dateUtc, newAmount: 250m);
        });

        barrier.SignalAndWait(TimeSpan.FromSeconds(10));
        await Task.WhenAll(t1, t2).WaitAsync(TimeSpan.FromSeconds(30));

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var entries = await sp.GetRequiredService<IAccountingEntryReader>().GetByDocumentAsync(docId, CancellationToken.None);
        entries.Should().HaveCount(3);

        var tb = await sp.GetRequiredService<ITrialBalanceReader>()
            .GetAsync(fromInclusive: period, toInclusive: period, CancellationToken.None);

        tb.Single(r => r.AccountCode == "50").ClosingBalance.Should().Be(250m);

        var logs = await ReadPostingLogsAsync(sp, window, docId);
        logs.Count(l => l.Operation == PostingOperation.Repost && l.Status == PostingStateStatus.Completed).Should().Be(1);
    }

    [Fact]
    public async Task Concurrent_Unpost_TwoCalls_ResultInSingleEffect_NoDuplicateStorno_OnePostingLog()
    {
        var window = PostingLogTestWindow.Capture();

        using var host = CreateHost();
        await SeedMinimalCoaAsync(host);

        var dateUtc = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var period = new DateOnly(2026, 1, 1);
        var docId = await CreateDraftAsync(host, dateUtc, number: "INV-UCONC");

        await PostCashRevenueAsync(host, docId, dateUtc, amount: 100m);

        using var barrier = new Barrier(3);

        var t1 = Task.Run(async () =>
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            await UnpostAsync(host, docId);
        });

        var t2 = Task.Run(async () =>
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            await UnpostAsync(host, docId);
        });

        barrier.SignalAndWait(TimeSpan.FromSeconds(10));
        await Task.WhenAll(t1, t2).WaitAsync(TimeSpan.FromSeconds(30));

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var doc = await sp.GetRequiredService<IDocumentRepository>().GetAsync(docId, CancellationToken.None);
        doc!.Status.Should().Be(DocumentStatus.Draft);

        var entries = await sp.GetRequiredService<IAccountingEntryReader>().GetByDocumentAsync(docId, CancellationToken.None);
        entries.Should().HaveCount(2);
        entries.Count(e => e.IsStorno).Should().Be(1);

        var tb = await sp.GetRequiredService<ITrialBalanceReader>()
            .GetAsync(fromInclusive: period, toInclusive: period, CancellationToken.None);

        tb.Single(r => r.AccountCode == "50").ClosingBalance.Should().Be(0m);

        var logs = await ReadPostingLogsAsync(sp, window, docId);
        logs.Count(l => l.Operation == PostingOperation.Unpost && l.Status == PostingStateStatus.Completed).Should().Be(1);
    }

    private IHost CreateHost() => IntegrationHostFactory.Create(
        Fixture.ConnectionString,
        services => services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>());


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
            Name: "Revenue",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);
    }

    private static async Task<Guid> CreateDraftAsync(IHost host, DateTime dateUtc, string number)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

        return await drafts.CreateDraftAsync(
            typeCode: "demo.sales_invoice",
            number: number,
            dateUtc: dateUtc,
            manageTransaction: true,
            ct: CancellationToken.None);
    }

    private static async Task PostCashRevenueAsync(IHost host, Guid documentId, DateTime dateUtc, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

        await docs.PostAsync(documentId, async (ctx, ct) =>
        {
            var chart = await ctx.GetChartOfAccountsAsync(ct);
            ctx.Post(documentId, dateUtc, chart.Get("50"), chart.Get("90.1"), amount);
        }, CancellationToken.None);
    }

    private static async Task UnpostAsync(IHost host, Guid documentId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
        await docs.UnpostAsync(documentId, CancellationToken.None);
    }

    private static async Task RepostCashRevenueAsync(IHost host, Guid documentId, DateTime dateUtc, decimal newAmount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

        await docs.RepostAsync(documentId, async (ctx, ct) =>
        {
            var chart = await ctx.GetChartOfAccountsAsync(ct);
            ctx.Post(documentId, dateUtc, chart.Get("50"), chart.Get("90.1"), newAmount);
        }, CancellationToken.None);
    }

    private static async Task RepostInvalidTwoDaysAsync(IHost host, Guid documentId, DateTime dateUtc, decimal newAmount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

        // Storno uses the original entry day (document date), but the new posting uses another UTC day.
        // This MUST fail the validator: "All entries must belong to the same UTC day (document date)."
        await docs.RepostAsync(documentId, async (ctx, ct) =>
        {
            var chart = await ctx.GetChartOfAccountsAsync(ct);

            ctx.Post(documentId, dateUtc.AddDays(1), chart.Get("50"), chart.Get("90.1"), newAmount);
        }, CancellationToken.None);
    }

    private static async Task<IReadOnlyList<TrialBalanceRow>> ReadTrialBalanceAsync(IHost host, DateOnly period)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var tb = scope.ServiceProvider.GetRequiredService<ITrialBalanceReader>();
        return await tb.GetAsync(period, period, CancellationToken.None);
    }

    private static async Task<IReadOnlyList<PostingStateRecord>> ReadPostingLogsAsync(
        IServiceProvider sp,
        PostingLogTestWindow window,
        Guid documentId)
    {
        var reader = sp.GetRequiredService<IPostingStateReader>();

        var page = await reader.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = window.FromUtc,
            ToUtc = window.ToUtc,
            DocumentId = documentId,
            PageSize = 10_000,
        }, CancellationToken.None);

        return page.Records.ToList();
    }
}

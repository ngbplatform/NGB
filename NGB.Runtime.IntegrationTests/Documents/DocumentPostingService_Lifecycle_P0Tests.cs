using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Definitions;
using NGB.Persistence.Documents;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.PostingState;
using NGB.Runtime.Accounts;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentPostingService_Lifecycle_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task PostAsync_FromDraft_WritesAccounting_AndSetsDocumentPosted()
    {
        var window = PostingLogTestWindow.Capture();

        using var host = CreateHost();
        await SeedMinimalCoaAsync(host);

        var dateUtc = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var docId = await CreateDraftAsync(host, dateUtc, number: "INV-1");

        // Act
        await PostCashRevenueAsync(host, docId, dateUtc, amount: 100m);

        // Assert
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var docRepo = sp.GetRequiredService<IDocumentRepository>();
        var doc = await docRepo.GetAsync(docId, CancellationToken.None);

        doc.Should().NotBeNull();
        doc!.Status.Should().Be(DocumentStatus.Posted);
        doc.PostedAtUtc.Should().NotBeNull();
        doc.MarkedForDeletionAtUtc.Should().BeNull();
        doc.UpdatedAtUtc.Should().BeAfter(doc.CreatedAtUtc);

        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        var entries = await entryReader.GetByDocumentAsync(docId, CancellationToken.None);
        entries.Should().HaveCount(1);
        entries.Single().IsStorno.Should().BeFalse();
        entries.Single().Amount.Should().Be(100m);

        var logs = await ReadPostingLogsAsync(sp, window, docId);
        logs.Should().ContainSingle(l => l.Operation == PostingOperation.Post && l.Status == PostingStateStatus.Completed);
    }

    [Fact]
    public async Task PostAsync_WhenAlreadyPosted_IsIdempotent_NoExtraEntries_NoDocMutation()
    {
        var window = PostingLogTestWindow.Capture();

        using var host = CreateHost();
        await SeedMinimalCoaAsync(host);

        var dateUtc = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var docId = await CreateDraftAsync(host, dateUtc, number: "INV-2");

        await PostCashRevenueAsync(host, docId, dateUtc, amount: 100m);

        DateTime postedAtBefore;
        DateTime updatedAtBefore;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var doc = await scope.ServiceProvider.GetRequiredService<IDocumentRepository>()
                .GetAsync(docId, CancellationToken.None);

            doc!.Status.Should().Be(DocumentStatus.Posted);
            doc.PostedAtUtc.Should().NotBeNull();
            postedAtBefore = doc.PostedAtUtc!.Value;
            updatedAtBefore = doc.UpdatedAtUtc;
        }

        // Act: second post with a DIFFERENT amount must be a strict no-op.
        await PostCashRevenueAsync(host, docId, dateUtc, amount: 999m);

        // Assert
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
            var entries = await entryReader.GetByDocumentAsync(docId, CancellationToken.None);
            entries.Should().HaveCount(1);
            entries.Single().Amount.Should().Be(100m, "second PostAsync must not post anything");

            var doc = await sp.GetRequiredService<IDocumentRepository>()
                .GetAsync(docId, CancellationToken.None);

            doc!.Status.Should().Be(DocumentStatus.Posted);
            doc.PostedAtUtc.Should().Be(postedAtBefore, "second PostAsync must not mutate PostedAt");
            doc.UpdatedAtUtc.Should().Be(updatedAtBefore, "second PostAsync must not update UpdatedAt");

            var logs = await ReadPostingLogsAsync(sp, window, docId);
            logs.Count(l => l.Operation == PostingOperation.Post).Should().Be(1);
        }
    }

    [Fact]
    public async Task PostAsync_WhenPostingProducesInvalidEntries_RollsBackEverything_NoPostingLog_NoEntries_StatusStaysDraft()
    {
        var window = PostingLogTestWindow.Capture();

        using var host = CreateHost();
        await SeedMinimalCoaAsync(host);

        var dateUtc = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var docId = await CreateDraftAsync(host, dateUtc, number: "INV-3");

        // Act
        Func<Task> act = () => PostInvalidTwoDaysAsync(host, docId, dateUtc, amount: 10m);

        // Assert
        await act.Should().ThrowAsync<NgbArgumentInvalidException>()
            .WithMessage("*same UTC day*");

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var doc = await sp.GetRequiredService<IDocumentRepository>().GetAsync(docId, CancellationToken.None);
        doc!.Status.Should().Be(DocumentStatus.Draft);
        doc.PostedAtUtc.Should().BeNull();

        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        var entries = await entryReader.GetByDocumentAsync(docId, CancellationToken.None);
        entries.Should().BeEmpty();

        var logs = await ReadPostingLogsAsync(sp, window, docId);
        logs.Should().BeEmpty("posting_log begin row must be rolled back together with the failed PostAsync");
    }

    [Fact]
    public async Task MarkForDeletionAsync_ThenPostAsync_Throws_AndDoesNotTouchAccountingOrPostingLog()
    {
        var window = PostingLogTestWindow.Capture();

        using var host = CreateHost();
        await SeedMinimalCoaAsync(host);

        var dateUtc = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var docId = await CreateDraftAsync(host, dateUtc, number: "INV-4");

        await MarkForDeletionAsync(host, docId);

        Func<Task> act = () => PostCashRevenueAsync(host, docId, dateUtc, amount: 10m);

        var ex = await act.Should().ThrowAsync<DocumentMarkedForDeletionException>();
        ex.Which.ErrorCode.Should().Be(DocumentMarkedForDeletionException.ErrorCodeConst);
        ex.Which.Context.Should().ContainKey("operation");
        ex.Which.Context["operation"].Should().Be("Document.Post");
        ex.Which.Context["documentId"].Should().Be(docId);

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var doc = await sp.GetRequiredService<IDocumentRepository>().GetAsync(docId, CancellationToken.None);
        doc!.Status.Should().Be(DocumentStatus.MarkedForDeletion);
        doc.PostedAtUtc.Should().BeNull();

        var entries = await sp.GetRequiredService<IAccountingEntryReader>()
            .GetByDocumentAsync(docId, CancellationToken.None);
        entries.Should().BeEmpty();

        var logs = await ReadPostingLogsAsync(sp, window, docId);
        logs.Should().BeEmpty();
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
        var sp = scope.ServiceProvider;
        var docs = sp.GetRequiredService<IDocumentPostingService>();

        await docs.PostAsync(documentId, async (ctx, ct) =>
        {
            var chart = await ctx.GetChartOfAccountsAsync(ct);

            ctx.Post(
                documentId: documentId,
                period: dateUtc,
                debit: chart.Get("50"),
                credit: chart.Get("90.1"),
                amount: amount);
        }, CancellationToken.None);
    }

    private static async Task PostInvalidTwoDaysAsync(IHost host, Guid documentId, DateTime baseDateUtc, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var docs = sp.GetRequiredService<IDocumentPostingService>();

        // Produce two entries with different UTC days => validator must throw.
        await docs.PostAsync(documentId, async (ctx, ct) =>
        {
            var chart = await ctx.GetChartOfAccountsAsync(ct);

            ctx.Post(
                documentId: documentId,
                period: baseDateUtc,
                debit: chart.Get("50"),
                credit: chart.Get("90.1"),
                amount: amount);

            ctx.Post(
                documentId: documentId,
                period: baseDateUtc.AddDays(1),
                debit: chart.Get("50"),
                credit: chart.Get("90.1"),
                amount: amount);
        }, CancellationToken.None);
    }

    private static async Task MarkForDeletionAsync(IHost host, Guid documentId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
        await docs.MarkForDeletionAsync(documentId, CancellationToken.None);
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
            Status = null,
            PageSize = 10_000,
        }, CancellationToken.None);

        return page.Records.ToList();
    }
}

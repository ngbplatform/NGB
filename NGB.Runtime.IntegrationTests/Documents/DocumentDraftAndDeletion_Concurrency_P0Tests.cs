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
using NGB.Runtime.Documents.Workflow;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentDraftAndDeletion_Concurrency_P0Tests(PostgresTestFixture fixture)
{
    private const string Cash = "50";
    private const string Revenue = "90.1";
    private const string TypeCode = "it_doc_tx";

    [Fact]
    public async Task Draft_MarkForDeletion_ConcurrentWithPost_ExactlyOneWins_AndStateIsConsistent()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(
            fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>());

        await SeedMinimalCoaAsync(host);

        var docDateUtc = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var window = PostingLogTestWindow.Capture(lookBack: TimeSpan.FromHours(3), lookAhead: TimeSpan.FromHours(3));

        Guid documentId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            documentId = await drafts.CreateDraftAsync(TypeCode, number: null, dateUtc: docDateUtc, manageTransaction: true, ct: CancellationToken.None);
        }

        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Act
        var markTask = Task.Run(() => RunMarkForDeletionAsync(host, documentId, gate));
        var postTask = Task.Run(() => RunPostAsync(host, documentId, docDateUtc, gate));

        gate.SetResult(true);

        var outcomes = await Task.WhenAll(markTask, postTask)
            .WaitAsync(TimeSpan.FromSeconds(45));

        var mark = outcomes.Single(o => o.Operation == "MarkForDeletion");
        var post = outcomes.Single(o => o.Operation == "Post");

        // Assert: exactly one succeeds
        outcomes.Count(o => o.Error is null).Should().Be(1,
            $"Expected exactly one operation to succeed, but got: {string.Join(", ", outcomes.Select(o => o.Operation + ":" + (o.Error?.Message ?? "OK")))}");

        await using var verifyScope = host.Services.CreateAsyncScope();
        var sp = verifyScope.ServiceProvider;

        var docsRepo = sp.GetRequiredService<IDocumentRepository>();
        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        var postingLog = sp.GetRequiredService<IPostingStateReader>();

        var doc = await docsRepo.GetAsync(documentId, CancellationToken.None);
        doc.Should().NotBeNull();

        var entries = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);

        var logPage = await postingLog.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = window.FromUtc,
            ToUtc = window.ToUtc,
            DocumentId = documentId,
            PageSize = 50
        }, CancellationToken.None);

        if (post.Error is null)
        {
            // Post won.
            doc!.Status.Should().Be(DocumentStatus.Posted);
            doc.PostedAtUtc.Should().NotBeNull();
            doc.MarkedForDeletionAtUtc.Should().BeNull();

            entries.Should().HaveCount(1);
            logPage.Records.Should().ContainSingle(r => r.Operation == PostingOperation.Post && r.CompletedAtUtc != null);

            mark.Error.Should().NotBeNull();
            mark.Error.Should().BeOfType<DocumentWorkflowStateMismatchException>();
            var markEx = (DocumentWorkflowStateMismatchException)mark.Error!;
            markEx.Operation.Should().Be("Document.MarkForDeletion");
            markEx.ExpectedState.Should().Be(DocumentStatus.Draft.ToString());
            markEx.ActualState.Should().Be(DocumentStatus.Posted.ToString());
        }
        else
        {
            // MarkForDeletion won.
            doc!.Status.Should().Be(DocumentStatus.MarkedForDeletion);
            doc.MarkedForDeletionAtUtc.Should().NotBeNull();
            doc.PostedAtUtc.Should().BeNull();

            entries.Should().BeEmpty();
            logPage.Records.Should().BeEmpty("posting_log must not be written when Post is rejected before PostingEngine is invoked");

            post.Error.Should().NotBeNull();
            post.Error.Should().BeOfType<DocumentMarkedForDeletionException>();
            var postEx = (DocumentMarkedForDeletionException)post.Error!;
            postEx.Context.Should().ContainKey("operation").WhoseValue.Should().Be("Document.Post");
        }
    }

    [Fact]
    public async Task MarkForDeletion_IsIdempotent_EvenUnderConcurrentCalls_AndDoesNotMutateTimestampsOnNoOp()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(
            fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>());

        var docDateUtc = new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc);

        Guid documentId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            documentId = await drafts.CreateDraftAsync(TypeCode, number: null, dateUtc: docDateUtc, manageTransaction: true, ct: CancellationToken.None);
        }

        // Act 1: two concurrent calls
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var t1 = Task.Run(() => RunMarkForDeletionAsync(host, documentId, gate));
        var t2 = Task.Run(() => RunMarkForDeletionAsync(host, documentId, gate));

        gate.SetResult(true);

        var outcomes = await Task.WhenAll(t1, t2)
            .WaitAsync(TimeSpan.FromSeconds(45));

        outcomes.All(o => o.Error is null).Should().BeTrue(
            $"Expected both MarkForDeletion calls to succeed, but got: {string.Join(", ", outcomes.Select(o => o.Error?.Message ?? "OK"))}");

        // Assert: marked + no accounting side effects
        await using var scopeAfter = host.Services.CreateAsyncScope();
        var spAfter = scopeAfter.ServiceProvider;

        var docsRepo = spAfter.GetRequiredService<IDocumentRepository>();
        var entryReader = spAfter.GetRequiredService<IAccountingEntryReader>();

        var docAfter = await docsRepo.GetAsync(documentId, CancellationToken.None);
        docAfter.Should().NotBeNull();
        docAfter!.Status.Should().Be(DocumentStatus.MarkedForDeletion);
        docAfter.MarkedForDeletionAtUtc.Should().NotBeNull();
        docAfter.PostedAtUtc.Should().BeNull();

        var entries = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);
        entries.Should().BeEmpty();

        // Act 2: call again (strict no-op)
        var updatedAt = docAfter.UpdatedAtUtc;
        var markedAt = docAfter.MarkedForDeletionAtUtc;

        await using (var scopeNoOp = host.Services.CreateAsyncScope())
        {
            var docs = scopeNoOp.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            await docs.MarkForDeletionAsync(documentId, CancellationToken.None);
        }

        var docAfterNoOp = await docsRepo.GetAsync(documentId, CancellationToken.None);
        docAfterNoOp.Should().NotBeNull();
        docAfterNoOp!.UpdatedAtUtc.Should().Be(updatedAt);
        docAfterNoOp.MarkedForDeletionAtUtc.Should().Be(markedAt);
    }

    private static async Task SeedMinimalCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var accounts = sp.GetRequiredService<IChartOfAccountsManagementService>();

        await accounts.CreateAsync(new CreateAccountRequest(
            Cash,
            "Cash",
            AccountType.Asset,
            StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Revenue,
            "Revenue",
            AccountType.Income,
            StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);
    }

    private static async Task<Outcome> RunPostAsync(IHost host, Guid documentId, DateTime periodUtc, TaskCompletionSource<bool> gate)
    {
        await gate.Task;

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var docs = sp.GetRequiredService<IDocumentPostingService>();

        try
        {
            await docs.PostAsync(documentId,
                postingAction: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(documentId, periodUtc, chart.Get(Cash), chart.Get(Revenue), 10m);
                },
                ct: CancellationToken.None);

            return Outcome.Success("Post");
        }
        catch (Exception ex)
        {
            return Outcome.Failure("Post", ex);
        }
    }

    private static async Task<Outcome> RunMarkForDeletionAsync(IHost host, Guid documentId, TaskCompletionSource<bool> gate)
    {
        await gate.Task;

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var docs = sp.GetRequiredService<IDocumentPostingService>();

        try
        {
            await docs.MarkForDeletionAsync(documentId, CancellationToken.None);
            return Outcome.Success("MarkForDeletion");
        }
        catch (Exception ex)
        {
            return Outcome.Failure("MarkForDeletion", ex);
        }
    }

    private sealed record Outcome(string Operation, Exception? Error)
    {
        public static Outcome Success(string operation) => new(operation, null);
        public static Outcome Failure(string operation, Exception error) => new(operation, error);
    }
}

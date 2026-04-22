using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Definitions;
using NGB.Persistence.Documents;
using NGB.Runtime.Documents;
using NGB.Runtime.Documents.Workflow;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Concurrency;

[Collection(PostgresCollection.Name)]
public sealed class DocumentLifecycle_ConcurrentPostVsMarkForDeletion_NoDeadlocks_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string TypeCode = "it_doc_tx";

    [Fact]
    public async Task PostAsync_ConcurrentWith_MarkForDeletionAsync_NoDeadlocks_AndNoPartialWrites()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>());
        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        Guid docId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            docId = await drafts.CreateDraftAsync(TypeCode, number: null, dateUtc: ReportingTestHelpers.Day15Utc, manageTransaction: true, ct: CancellationToken.None);
        }

        // Coordinated start to maximize contention.
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var postTask = Task.Run(async () =>
        {
            await start.Task;
            await PostAsync(host, docId, cts.Token);
            return "post";
        }, cts.Token);

        var markTask = Task.Run(async () =>
        {
            await start.Task;
            await MarkForDeletionAsync(host, docId, cts.Token);
            return "mark";
        }, cts.Token);

        start.SetResult();

        // We expect one to win; the other should fail fast (no deadlock). Capture both outcomes.
        var postEx = await Record.ExceptionAsync(() => postTask);
        var markEx = await Record.ExceptionAsync(() => markTask);

        // At least one should succeed.
        (postEx is null || markEx is null).Should().BeTrue();

        // Final state must be consistent.
        await using var verifyScope = host.Services.CreateAsyncScope();
        var docs = verifyScope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var doc = await docs.GetAsync(docId, CancellationToken.None);
        doc.Should().NotBeNull();

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync(CancellationToken.None);

        var regCount = (int)(await new NpgsqlCommand(
            "SELECT COUNT(*)::int FROM accounting_register_main WHERE document_id = @d",
            conn)
        {
            Parameters = { new("d", docId) }
        }.ExecuteScalarAsync(CancellationToken.None))!;

        if (doc!.Status == DocumentStatus.Posted)
        {
            regCount.Should().Be(1, "a successful Post must write exactly one movement for this test");
            doc.MarkedForDeletionAtUtc.Should().BeNull();

            // MarkForDeletion must fail for Posted docs.
            markEx.Should().NotBeNull();
            markEx.Should().BeOfType<DocumentWorkflowStateMismatchException>();
            var markMismatch = (DocumentWorkflowStateMismatchException)markEx!;
            markMismatch.Operation.Should().Be("Document.MarkForDeletion");
            markMismatch.ExpectedState.Should().Be(DocumentStatus.Draft.ToString());
            markMismatch.ActualState.Should().Be(DocumentStatus.Posted.ToString());
        }
        else if (doc.Status == DocumentStatus.MarkedForDeletion)
        {
            regCount.Should().Be(0, "posting must not partially write movements when the doc is marked for deletion first");

            // Post must fail for MarkedForDeletion.
            postEx.Should().NotBeNull();
            postEx.Should().BeOfType<DocumentMarkedForDeletionException>();
            var postMarked = (DocumentMarkedForDeletionException)postEx!;
            postMarked.Context.Should().ContainKey("operation").WhoseValue.Should().Be("Document.Post");
        }
        else
        {
            throw new Xunit.Sdk.XunitException($"Unexpected final status: {doc.Status}");
        }
    }

    private static async Task PostAsync(IHost host, Guid documentId, CancellationToken ct)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

        await svc.PostAsync(
            documentId,
            async (ctx, innerCt) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(innerCt);
                ctx.Post(
                    documentId,
                    ReportingTestHelpers.Day15Utc,
                    chart.Get("50"),
                    chart.Get("90.1"),
                    1m);
            },
            ct);
    }

    private static async Task MarkForDeletionAsync(IHost host, Guid documentId, CancellationToken ct)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
        await svc.MarkForDeletionAsync(documentId, ct);
    }
}

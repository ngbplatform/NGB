using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Documents;
using NGB.Accounting.Posting;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Core.AuditLog;
using NGB.Core.Documents;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Documents;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.PostingState;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(PostgresCollection.Name)]
public sealed class AuditLog_DocumentPost_Concurrency_And_ValidatorFailure_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Post_ConcurrentCalls_WriteSingleAuditEvent_AndSinglePostingLogRow()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: "kc|doc-post-concurrent",
                        Email: "doc.post.concurrent@example.com",
                        DisplayName: "Doc Post Concurrent")));
            });

        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var dateUtc = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var window = PostingLogTestWindow.Capture(lookBack: TimeSpan.FromHours(3), lookAhead: TimeSpan.FromHours(3));

        Guid documentId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            documentId = await drafts.CreateDraftAsync(
                typeCode: AccountingDocumentTypeCodes.GeneralJournalEntry,
                number: "GJE-IT-POST-CONCURRENT-0001",
                dateUtc: dateUtc,
                manageTransaction: true,
                ct: CancellationToken.None);
        }

        async Task PostCashRevenueAsync(IAccountingPostingContext ctx, CancellationToken ct)
        {
            var chart = await ctx.GetChartOfAccountsAsync(ct);
            ctx.Post(
                documentId: documentId,
                period: dateUtc,
                debit: chart.Get("50"),
                credit: chart.Get("90.1"),
                amount: 10m);
        }

        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var t1 = Task.Run(() => RunPostAsync(host, documentId, PostCashRevenueAsync, gate));
        var t2 = Task.Run(() => RunPostAsync(host, documentId, PostCashRevenueAsync, gate));

        gate.SetResult(true);

        var errors = await Task.WhenAll(t1, t2)
            .WaitAsync(TimeSpan.FromSeconds(45));

        errors.All(e => e is null).Should().BeTrue("PostAsync is idempotent under concurrency; one call posts, the other becomes a no-op");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            var docsRepo = sp.GetRequiredService<IDocumentRepository>();
            var doc = await docsRepo.GetAsync(documentId, CancellationToken.None);
            doc.Should().NotBeNull();
            doc!.Status.Should().Be(DocumentStatus.Posted);
            doc.PostedAtUtc.Should().NotBeNull();

            var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
            var entries = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);
            entries.Should().HaveCount(1);

            var logReader = sp.GetRequiredService<IPostingStateReader>();
            var page = await logReader.GetPageAsync(new PostingStatePageRequest
            {
                FromUtc = window.FromUtc,
                ToUtc = window.ToUtc,
                DocumentId = documentId,
                Operation = PostingOperation.Post,
                PageSize = 50
            }, CancellationToken.None);

            page.Records.Should().ContainSingle(r => r.Operation == PostingOperation.Post && r.CompletedAtUtc != null);

            var auditReader = sp.GetRequiredService<IAuditEventReader>();
            var postEvents = await auditReader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: documentId,
                    ActionCode: AuditActionCodes.DocumentPost,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            postEvents.Should().HaveCount(1, "idempotent Post (including concurrent duplicates) must emit exactly one document.post audit event");
        }
    }

    [Fact]
    public async Task Post_WhenPostingValidatorFails_Throws_AndDoesNotWritePostAuditEvent()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: "kc|doc-post-invalid",
                        Email: null,
                        DisplayName: null)));
            });

        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var dateUtc = new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc);
        var window = PostingLogTestWindow.Capture(lookBack: TimeSpan.FromHours(3), lookAhead: TimeSpan.FromHours(3));

        Guid documentId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            documentId = await drafts.CreateDraftAsync(
                typeCode: AccountingDocumentTypeCodes.GeneralJournalEntry,
                number: "GJE-IT-POST-INVALID-0001",
                dateUtc: dateUtc,
                manageTransaction: true,
                ct: CancellationToken.None);
        }

        async Task PostInvalidAsync(IAccountingPostingContext ctx, CancellationToken ct)
        {
            var chart = await ctx.GetChartOfAccountsAsync(ct);

            // Invalid: validator requires Amount > 0.
            ctx.Post(
                documentId: documentId,
                period: dateUtc,
                debit: chart.Get("50"),
                credit: chart.Get("90.1"),
                amount: 0m);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

            var act = async () => await posting.PostAsync(documentId, PostInvalidAsync, CancellationToken.None);
            await act.Should().ThrowAsync<NgbArgumentInvalidException>()
                .WithMessage("*must be > 0*");
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            var docsRepo = sp.GetRequiredService<IDocumentRepository>();
            var doc = await docsRepo.GetAsync(documentId, CancellationToken.None);
            doc.Should().NotBeNull();
            doc!.Status.Should().Be(DocumentStatus.Draft, "failed posting must rollback document header state");
            doc.PostedAtUtc.Should().BeNull();

            var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
            var entries = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);
            entries.Should().BeEmpty();

            var logReader = sp.GetRequiredService<IPostingStateReader>();
            var page = await logReader.GetPageAsync(new PostingStatePageRequest
            {
                FromUtc = window.FromUtc,
                ToUtc = window.ToUtc,
                DocumentId = documentId,
                Operation = PostingOperation.Post,
                PageSize = 50
            }, CancellationToken.None);

            page.Records.Should().BeEmpty("failed Post must rollback posting_log and must not emit audit events");

            var auditReader = sp.GetRequiredService<IAuditEventReader>();
            var postEvents = await auditReader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: documentId,
                    ActionCode: AuditActionCodes.DocumentPost,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            postEvents.Should().BeEmpty("failed posting must not be audited");
        }
    }

    private static async Task<Exception?> RunPostAsync(
        IHost host,
        Guid documentId,
        Func<IAccountingPostingContext, CancellationToken, Task> post,
        TaskCompletionSource<bool> gate)
    {
        await gate.Task;

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var posting = sp.GetRequiredService<IDocumentPostingService>();

        try
        {
            await posting.PostAsync(documentId, post, CancellationToken.None);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private sealed class FixedCurrentActorContext(ActorIdentity actor) : ICurrentActorContext
    {
        public ActorIdentity? Current => actor;
    }
}

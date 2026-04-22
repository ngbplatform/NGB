using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Documents;
using NGB.Accounting.Posting;
using NGB.Core.AuditLog;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Persistence.AuditLog;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Documents;
using NGB.Runtime.Documents.Workflow;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(PostgresCollection.Name)]
public sealed class AuditLog_DocumentOperations_FailureModes_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Post_WhenMarkedForDeletion_Throws_AndDoesNotWritePostAuditEvent()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: "kc|doc-fail-1",
                        Email: "doc.fail@example.com",
                        DisplayName: "Doc Fail")));
            });

        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var dateUtc = new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc);
        var number = "GJE-FAIL-POST-" + Guid.CreateVersion7().ToString("N")[..8];

        Guid documentId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

            documentId = await drafts.CreateDraftAsync(
                typeCode: AccountingDocumentTypeCodes.GeneralJournalEntry,
                dateUtc: dateUtc,
                number: number,
                ct: CancellationToken.None);

            await posting.MarkForDeletionAsync(documentId, CancellationToken.None);

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

            var act = async () => await posting.PostAsync(documentId, PostCashRevenueAsync, CancellationToken.None);
            var ex = await act.Should().ThrowAsync<DocumentMarkedForDeletionException>();
            ex.Which.ErrorCode.Should().Be(DocumentMarkedForDeletionException.ErrorCodeConst);
            ex.Which.Context.Should().ContainKey("operation").WhoseValue.Should().Be("Document.Post");
            ex.Which.Context.Should().ContainKey("documentId").WhoseValue.Should().Be(documentId);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var postEvents = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: documentId,
                    ActionCode: AuditActionCodes.DocumentPost,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            postEvents.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task MarkForDeletion_WhenPosted_Throws_AndDoesNotWriteMarkForDeletionAuditEvent()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: "kc|doc-fail-2",
                        Email: null,
                        DisplayName: null)));
            });

        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var dateUtc = new DateTime(2026, 1, 21, 0, 0, 0, DateTimeKind.Utc);
        var number = "GJE-FAIL-MFD-" + Guid.CreateVersion7().ToString("N")[..8];

        Guid documentId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

            documentId = await drafts.CreateDraftAsync(
                typeCode: AccountingDocumentTypeCodes.GeneralJournalEntry,
                dateUtc: dateUtc,
                number: number,
                ct: CancellationToken.None);

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

            await posting.PostAsync(documentId, PostCashRevenueAsync, CancellationToken.None);

            var act = async () => await posting.MarkForDeletionAsync(documentId, CancellationToken.None);
            var ex = await act.Should().ThrowAsync<DocumentWorkflowStateMismatchException>();
            ex.Which.ErrorCode.Should().Be(DocumentWorkflowStateMismatchException.ErrorCodeConst);
            ex.Which.Operation.Should().Be("Document.MarkForDeletion");
            ex.Which.ExpectedState.Should().Be(DocumentStatus.Draft.ToString());
            ex.Which.ActualState.Should().Be(DocumentStatus.Posted.ToString());
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var markEvents = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: documentId,
                    ActionCode: AuditActionCodes.DocumentMarkForDeletion,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            markEvents.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Unpost_WhenMarkedForDeletion_Throws_AndDoesNotWriteUnpostAuditEvent()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: "kc|doc-fail-3",
                        Email: null,
                        DisplayName: null)));
            });

        var dateUtc = new DateTime(2026, 1, 22, 0, 0, 0, DateTimeKind.Utc);
        var number = "GJE-FAIL-UNPOST-" + Guid.CreateVersion7().ToString("N")[..8];

        Guid documentId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

            documentId = await drafts.CreateDraftAsync(
                typeCode: AccountingDocumentTypeCodes.GeneralJournalEntry,
                dateUtc: dateUtc,
                number: number,
                ct: CancellationToken.None);

            await posting.MarkForDeletionAsync(documentId, CancellationToken.None);

            var act = async () => await posting.UnpostAsync(documentId, CancellationToken.None);
            var ex = await act.Should().ThrowAsync<DocumentWorkflowStateMismatchException>();
            ex.Which.ErrorCode.Should().Be(DocumentWorkflowStateMismatchException.ErrorCodeConst);
            ex.Which.Operation.Should().Be("Document.Unpost");
            ex.Which.ExpectedState.Should().Be(DocumentStatus.Posted.ToString());
            ex.Which.ActualState.Should().Be(DocumentStatus.MarkedForDeletion.ToString());
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var unpostEvents = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: documentId,
                    ActionCode: AuditActionCodes.DocumentUnpost,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            unpostEvents.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Repost_WhenDraft_Throws_AndDoesNotWriteRepostAuditEvent()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: "kc|doc-fail-4",
                        Email: null,
                        DisplayName: null)));
            });

        var dateUtc = new DateTime(2026, 1, 23, 0, 0, 0, DateTimeKind.Utc);
        var number = "GJE-FAIL-REPOST-" + Guid.CreateVersion7().ToString("N")[..8];

        Guid documentId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

            documentId = await drafts.CreateDraftAsync(
                typeCode: AccountingDocumentTypeCodes.GeneralJournalEntry,
                dateUtc: dateUtc,
                number: number,
                ct: CancellationToken.None);

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

            var act = async () => await posting.RepostAsync(documentId, PostCashRevenueAsync, CancellationToken.None);
            var ex = await act.Should().ThrowAsync<DocumentWorkflowStateMismatchException>();
            ex.Which.ErrorCode.Should().Be(DocumentWorkflowStateMismatchException.ErrorCodeConst);
            ex.Which.Operation.Should().Be("Document.Repost");
            ex.Which.ExpectedState.Should().Be(DocumentStatus.Posted.ToString());
            ex.Which.ActualState.Should().Be(DocumentStatus.Draft.ToString());
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var repostEvents = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: documentId,
                    ActionCode: AuditActionCodes.DocumentRepost,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            repostEvents.Should().BeEmpty();
        }
    }

    private sealed class FixedCurrentActorContext(ActorIdentity actor) : ICurrentActorContext
    {
        public ActorIdentity? Current => actor;
    }
}

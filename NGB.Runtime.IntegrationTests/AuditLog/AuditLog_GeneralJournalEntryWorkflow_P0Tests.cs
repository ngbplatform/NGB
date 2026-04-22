using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.AuditLog;
using NGB.Core.Documents.GeneralJournalEntry;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Documents.GeneralJournalEntry;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Documents.GeneralJournalEntry;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(PostgresCollection.Name)]
public sealed class AuditLog_GeneralJournalEntryWorkflow_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ManualWorkflow_WritesAuditEvents_ForDraftEdits_AndWorkflowTransitions()
    {
        using var host = CreateHost("kc|gje-audit-1", "alex.carter@example.com", "Alex Carter");
        var (cashId, revenueId, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        Guid documentId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();

            documentId = await gje.CreateDraftAsync(
                new DateTime(2026, 4, 6, 12, 0, 0, DateTimeKind.Utc),
                initiatedBy: "Alex Carter",
                ct: CancellationToken.None);

            await gje.UpdateDraftHeaderAsync(
                documentId,
                new GeneralJournalEntryDraftHeaderUpdate(
                    JournalType: GeneralJournalEntryModels.JournalType.Standard,
                    ReasonCode: "TEST GJE",
                    Memo: "Just testing",
                    ExternalReference: null,
                    AutoReverse: false,
                    AutoReverseOnUtc: null),
                updatedBy: "Alex Carter",
                ct: CancellationToken.None);

            await gje.ReplaceDraftLinesAsync(
                documentId,
                [
                    new GeneralJournalEntryDraftLineInput(
                        Side: GeneralJournalEntryModels.LineSide.Debit,
                        AccountId: cashId,
                        Amount: 10m,
                        Memo: "Debit"),
                    new GeneralJournalEntryDraftLineInput(
                        Side: GeneralJournalEntryModels.LineSide.Credit,
                        AccountId: revenueId,
                        Amount: 10m,
                        Memo: "Credit"),
                ],
                updatedBy: "Alex Carter",
                ct: CancellationToken.None);

            await gje.SubmitAsync(documentId, submittedBy: "Alex Carter", ct: CancellationToken.None);
            await gje.ApproveAsync(documentId, approvedBy: "Alex Carter", ct: CancellationToken.None);
            await gje.PostApprovedAsync(documentId, postedBy: "Alex Carter", ct: CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();
            var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: documentId,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            var actor = await users.GetByAuthSubjectAsync("kc|gje-audit-1", CancellationToken.None);
            actor.Should().NotBeNull();

            events.Select(x => x.ActionCode).Should().Contain([
                AuditActionCodes.DocumentCreateDraft,
                AuditActionCodes.DocumentUpdateDraft,
                AuditActionCodes.DocumentSubmit,
                AuditActionCodes.DocumentApprove,
                AuditActionCodes.DocumentPost,
            ]);

            events.Should().OnlyContain(x => x.ActorUserId == actor!.UserId);
            events.Count(x => x.ActionCode == AuditActionCodes.DocumentUpdateDraft).Should().Be(2);

            var headerUpdate = events.Single(x =>
                x.ActionCode == AuditActionCodes.DocumentUpdateDraft &&
                x.Changes.Any(c => c.FieldPath == "reason_code"));
            headerUpdate.Changes.Should().Contain(x => x.FieldPath == "reason_code" && x.NewValueJson == "\"TEST GJE\"");
            headerUpdate.Changes.Should().Contain(x => x.FieldPath == "memo" && x.NewValueJson == "\"Just testing\"");

            var linesUpdate = events.Single(x =>
                x.ActionCode == AuditActionCodes.DocumentUpdateDraft &&
                x.Changes.Any(c => c.FieldPath == "line_1_account_id"));
            linesUpdate.Changes.Should().Contain(x => x.FieldPath == "line_1_amount" && x.NewValueJson == "10");
            linesUpdate.Changes.Should().Contain(x => x.FieldPath == "line_2_amount" && x.NewValueJson == "10");

            var submit = events.Single(x => x.ActionCode == AuditActionCodes.DocumentSubmit);
            submit.Changes.Should().Contain(x => x.FieldPath == "approval_state" && x.NewValueJson == "\"submitted\"");
            submit.Changes.Should().Contain(x => x.FieldPath == "submitted_by" && x.NewValueJson == "\"Alex Carter\"");

            var approve = events.Single(x => x.ActionCode == AuditActionCodes.DocumentApprove);
            approve.Changes.Should().Contain(x => x.FieldPath == "approval_state" && x.NewValueJson == "\"approved\"");
            approve.Changes.Should().Contain(x => x.FieldPath == "approved_by" && x.NewValueJson == "\"Alex Carter\"");

            var post = events.Single(x => x.ActionCode == AuditActionCodes.DocumentPost);
            post.Changes.Should().Contain(x => x.FieldPath == "status" && x.NewValueJson == "\"posted\"");
            post.Changes.Should().Contain(x => x.FieldPath == "posted_by" && x.NewValueJson == "\"Alex Carter\"");
        }
    }

    [Fact]
    public async Task ReversePosted_UsesInitiatingUser_ForWorkflowFields_AndWritesAuditHistory()
    {
        using var host = CreateHost("kc|gje-audit-2", "alex.carter@example.com", "Alex Carter");
        var (cashId, revenueId, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        Guid reversalId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();

            var originalId = await gje.CreateDraftAsync(
                new DateTime(2026, 4, 9, 12, 0, 0, DateTimeKind.Utc),
                initiatedBy: "Alex Carter",
                ct: CancellationToken.None);

            await gje.UpdateDraftHeaderAsync(
                originalId,
                new GeneralJournalEntryDraftHeaderUpdate(
                    JournalType: GeneralJournalEntryModels.JournalType.Standard,
                    ReasonCode: "REVERSAL SOURCE",
                    Memo: "Original entry",
                    ExternalReference: null,
                    AutoReverse: false,
                    AutoReverseOnUtc: null),
                updatedBy: "Alex Carter",
                ct: CancellationToken.None);

            await gje.ReplaceDraftLinesAsync(
                originalId,
                [
                    new GeneralJournalEntryDraftLineInput(
                        Side: GeneralJournalEntryModels.LineSide.Debit,
                        AccountId: cashId,
                        Amount: 10m,
                        Memo: null),
                    new GeneralJournalEntryDraftLineInput(
                        Side: GeneralJournalEntryModels.LineSide.Credit,
                        AccountId: revenueId,
                        Amount: 10m,
                        Memo: null),
                ],
                updatedBy: "Alex Carter",
                ct: CancellationToken.None);

            await gje.SubmitAsync(originalId, submittedBy: "Alex Carter", ct: CancellationToken.None);
            await gje.ApproveAsync(originalId, approvedBy: "Alex Carter", ct: CancellationToken.None);
            await gje.PostApprovedAsync(originalId, postedBy: "Alex Carter", ct: CancellationToken.None);

            reversalId = await gje.ReversePostedAsync(
                originalId,
                new DateTime(2026, 4, 9, 12, 0, 0, DateTimeKind.Utc),
                initiatedBy: "Alex Carter",
                postImmediately: true,
                ct: CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();
            var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();
            var repo = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryRepository>();

            var header = await repo.GetHeaderAsync(reversalId, CancellationToken.None);
            header.Should().NotBeNull();
            header!.InitiatedBy.Should().Be("Alex Carter");
            header.SubmittedBy.Should().Be("Alex Carter");
            header.ApprovedBy.Should().Be("Alex Carter");
            header.PostedBy.Should().Be("Alex Carter");

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: reversalId,
                    Limit: 20,
                    Offset: 0),
                CancellationToken.None);

            var actor = await users.GetByAuthSubjectAsync("kc|gje-audit-2", CancellationToken.None);
            actor.Should().NotBeNull();

            events.Select(x => x.ActionCode).Should().Contain([
                AuditActionCodes.DocumentCreateDraft,
                AuditActionCodes.DocumentPost,
            ]);
            events.Should().OnlyContain(x => x.ActorUserId == actor!.UserId);

            var create = events.Single(x => x.ActionCode == AuditActionCodes.DocumentCreateDraft);
            create.Changes.Should().Contain(x => x.FieldPath == "source" && x.NewValueJson == "\"system\"");
            create.Changes.Should().Contain(x => x.FieldPath == "approval_state" && x.NewValueJson == "\"approved\"");
            create.Changes.Should().Contain(x => x.FieldPath == "submitted_by" && x.NewValueJson == "\"Alex Carter\"");
            create.Changes.Should().Contain(x => x.FieldPath == "approved_by" && x.NewValueJson == "\"Alex Carter\"");

            var post = events.Single(x => x.ActionCode == AuditActionCodes.DocumentPost);
            post.Changes.Should().Contain(x => x.FieldPath == "status" && x.NewValueJson == "\"posted\"");
            post.Changes.Should().Contain(x => x.FieldPath == "posted_by" && x.NewValueJson == "\"Alex Carter\"");
        }
    }

    private IHost CreateHost(string authSubject, string email, string displayName)
    {
        return IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: authSubject,
                        Email: email,
                        DisplayName: displayName)));
            });
    }

    private sealed class FixedCurrentActorContext(ActorIdentity actor) : ICurrentActorContext
    {
        public ActorIdentity? Current => actor;
    }
}

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Accounting.Documents;
using NGB.Accounting.Periods;
using NGB.Accounting.Posting;
using NGB.Core.AuditLog;
using NGB.Persistence.AuditLog;
using NGB.Runtime.Accounts;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Documents;
using NGB.Runtime.Documents.Workflow;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(PostgresCollection.Name)]
public sealed class AuditLog_NoOpOperations_NotLogged_P0Tests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Period_CloseMonth_WhenAlreadyClosed_DoesNotWriteSecondAuditEvent()
    {
        var period = new DateOnly(2026, 3, 1);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: "kc|period-closer-noop-1",
                        Email: "period.noop@example.com",
                        DisplayName: "Period NoOp")));
            });

        await ReportingTestHelpers.CloseMonthAsync(host, period, closedBy: "closer1");

        try
        {
            await ReportingTestHelpers.CloseMonthAsync(host, period, closedBy: "closer2");
        }
        catch (PeriodAlreadyClosedException)
        {
            // Expected in most implementations; audit must still remain single-event.
        }

        var expectedEntityId = DeterministicGuid.Create($"CloseMonth|{period:yyyy-MM-dd}");

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

        var events = await reader.QueryAsync(
            new AuditLogQuery(
                EntityKind: AuditEntityKind.Period,
                EntityId: expectedEntityId,
                ActionCode: AuditActionCodes.PeriodCloseMonth,
                Limit: 50,
                Offset: 0),
            CancellationToken.None);

        events.Should().HaveCount(1);
    }

    [Fact]
    public async Task Document_Post_WhenAlreadyPosted_DoesNotWriteSecondAuditEvent()
    {
        var dateUtc = new DateTime(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: "kc|doc-post-noop-1",
                        Email: "doc.post.noop@example.com",
                        DisplayName: "Doc Post NoOp")));
            });

        // Ensure accounts exist for posting callback.
        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        Guid documentId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            documentId = await drafts.CreateDraftAsync(
                typeCode: AccountingDocumentTypeCodes.GeneralJournalEntry,
                dateUtc: dateUtc,
                number: "GJE-NOOP-POST-0001",
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
                amount: 123m);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            await posting.PostAsync(documentId, PostCashRevenueAsync, CancellationToken.None);
            await posting.PostAsync(documentId, PostCashRevenueAsync, CancellationToken.None); // idempotent no-op
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();
            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: documentId,
                    ActionCode: AuditActionCodes.DocumentPost,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().HaveCount(1);
        }
    }

    [Fact]
    public async Task Document_Unpost_WhenAlreadyDraft_DoesNotWriteSecondAuditEvent()
    {
        var dateUtc = new DateTime(2026, 3, 3, 12, 0, 0, DateTimeKind.Utc);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: "kc|doc-unpost-noop-1",
                        Email: "doc.unpost.noop@example.com",
                        DisplayName: "Doc Unpost NoOp")));
            });

        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        Guid documentId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            documentId = await drafts.CreateDraftAsync(
                typeCode: AccountingDocumentTypeCodes.GeneralJournalEntry,
                dateUtc: dateUtc,
                number: "GJE-NOOP-UNPOST-0001",
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
                amount: 100m);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            await posting.PostAsync(documentId, PostCashRevenueAsync, CancellationToken.None);
            await posting.UnpostAsync(documentId, CancellationToken.None);
            await posting.UnpostAsync(documentId, CancellationToken.None); // idempotent no-op (already Draft)
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();
            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: documentId,
                    ActionCode: AuditActionCodes.DocumentUnpost,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().HaveCount(1);
        }
    }

    [Fact]
    public async Task Document_MarkForDeletion_WhenAlreadyMarked_DoesNotWriteSecondAuditEvent()
    {
        var dateUtc = new DateTime(2026, 3, 4, 12, 0, 0, DateTimeKind.Utc);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: "kc|doc-delete-noop-1",
                        Email: "doc.delete.noop@example.com",
                        DisplayName: "Doc Delete NoOp")));
            });

        Guid documentId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            documentId = await drafts.CreateDraftAsync(
                typeCode: AccountingDocumentTypeCodes.GeneralJournalEntry,
                dateUtc: dateUtc,
                number: "GJE-NOOP-DEL-0001",
                ct: CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            await posting.MarkForDeletionAsync(documentId, CancellationToken.None);
            await posting.MarkForDeletionAsync(documentId, CancellationToken.None); // idempotent no-op
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();
            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: documentId,
                    ActionCode: AuditActionCodes.DocumentMarkForDeletion,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().HaveCount(1);
        }
    }

    [Fact]
    public async Task Document_Repost_WhenDraft_DoesNotWriteAuditEvent_AndDoesNotExecutePostingCallback()
    {
        var dateUtc = new DateTime(2026, 3, 5, 12, 0, 0, DateTimeKind.Utc);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: "kc|doc-repost-draft-1",
                        Email: "doc.repost.draft@example.com",
                        DisplayName: "Doc Repost Draft")));
            });

        Guid documentId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            documentId = await drafts.CreateDraftAsync(
                typeCode: AccountingDocumentTypeCodes.GeneralJournalEntry,
                dateUtc: dateUtc,
                number: "GJE-NOOP-REPOST-0001",
                ct: CancellationToken.None);
        }

        var callbackExecuted = false;

        Task ShouldNotExecuteAsync(IAccountingPostingContext _, CancellationToken __)
        {
            callbackExecuted = true;
            throw new XunitException("Posting callback must not execute for Draft repost attempt.");
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

            var ex = await FluentActions
                .Awaiting(() => posting.RepostAsync(documentId, ShouldNotExecuteAsync, CancellationToken.None))
                .Should()
                .ThrowAsync<DocumentWorkflowStateMismatchException>();

            ex.Which.ErrorCode.Should().Be(DocumentWorkflowStateMismatchException.ErrorCodeConst);
            ex.Which.Context["operation"].Should().Be("Document.Repost");
            ex.Which.Context["documentId"].Should().Be(documentId);
            ex.Which.Context["expectedState"].Should().Be("Posted");
            ex.Which.Context["actualState"].Should().Be("Draft");
        }

        callbackExecuted.Should().BeFalse();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();
            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: documentId,
                    ActionCode: AuditActionCodes.DocumentRepost,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task CoA_SetActive_WhenAlreadyInTargetState_DoesNotWriteSecondAuditEvent()
    {
        var authSubject = "kc|coa-setactive-noop-1";

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: authSubject,
                        Email: "coa.setactive.noop@example.com",
                        DisplayName: "CoA SetActive NoOp")));
            });

        Guid accountId;
        DateTime updatedAtAfterFirst;
        DateTime updatedAtAfterNoOp;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
            var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();

            accountId = await coa.CreateAsync(
                new CreateAccountRequest(Code: "A-NOOP-1", Name: "Temp", Type: AccountType.Asset),
                CancellationToken.None);

            await coa.SetActiveAsync(accountId, isActive: false, CancellationToken.None);

            updatedAtAfterFirst = (await users.GetByAuthSubjectAsync(authSubject, CancellationToken.None))!.UpdatedAtUtc;

            await Task.Delay(25);

            await coa.SetActiveAsync(accountId, isActive: false, CancellationToken.None); // no-op

            updatedAtAfterNoOp = (await users.GetByAuthSubjectAsync(authSubject, CancellationToken.None))!.UpdatedAtUtc;
        }

        updatedAtAfterNoOp.Should().Be(updatedAtAfterFirst, "no-op operations must not touch platform_users (actor upsert happens only through audit)");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.ChartOfAccountsAccount,
                    EntityId: accountId,
                    ActionCode: AuditActionCodes.CoaAccountSetActive,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().HaveCount(1);
        }
    }

    [Fact]
    public async Task CoA_SoftDelete_WhenAlreadyDeleted_DoesNotWriteSecondAuditEvent()
    {
        var authSubject = "kc|coa-softdelete-noop-1";

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: authSubject,
                        Email: "coa.softdelete.noop@example.com",
                        DisplayName: "CoA SoftDelete NoOp")));
            });

        Guid accountId;
        DateTime updatedAtAfterFirst;
        DateTime updatedAtAfterNoOp;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
            var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();

            accountId = await coa.CreateAsync(
                new CreateAccountRequest(Code: "A-NOOP-DEL-1", Name: "Temp", Type: AccountType.Asset),
                CancellationToken.None);

            await coa.MarkForDeletionAsync(accountId, CancellationToken.None);

            updatedAtAfterFirst = (await users.GetByAuthSubjectAsync(authSubject, CancellationToken.None))!.UpdatedAtUtc;

            await Task.Delay(25);

            await coa.MarkForDeletionAsync(accountId, CancellationToken.None); // no-op

            updatedAtAfterNoOp = (await users.GetByAuthSubjectAsync(authSubject, CancellationToken.None))!.UpdatedAtUtc;
        }

        updatedAtAfterNoOp.Should().Be(updatedAtAfterFirst, "no-op operations must not touch platform_users (actor upsert happens only through audit)");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.ChartOfAccountsAccount,
                    EntityId: accountId,
                    ActionCode: AuditActionCodes.CoaAccountMarkForDeletion,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().HaveCount(1);
        }
    }

    private sealed class FixedCurrentActorContext(ActorIdentity actor) : ICurrentActorContext
    {
        public ActorIdentity? Current => actor;
    }
}

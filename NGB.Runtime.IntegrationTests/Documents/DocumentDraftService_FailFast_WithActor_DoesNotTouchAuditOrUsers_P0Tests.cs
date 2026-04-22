using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Documents;
using NGB.Accounting.Posting;
using NGB.Core.AuditLog;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Documents;
using NGB.Runtime.Documents.Workflow;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentDraftService_FailFast_WithActor_DoesNotTouchAuditOrUsers_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string AuthSubject = "kc|draft-failfast-actor-1";
    private const string Email = "draft.failfast.actor@example.com";
    private const string DisplayName = "Draft FailFast Actor";

    [Fact]
    public async Task UpdateDraft_WhenPosted_Throws_AndDoesNotWriteAuditOrTouchPlatformUser()
    {
        var dateUtc = new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: AuthSubject,
                        Email: Email,
                        DisplayName: DisplayName)));
            });

        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var documentId = await CreatePostedGjeAsync(
            host,
            dateUtc,
            number: "GJE-FF-ACTOR-UPD-0001",
            CancellationToken.None);

        var baselineUser = await GetUserSnapshotAsync(Fixture.ConnectionString, AuthSubject);
        var baselineAuditCount = await CountAuditEventsAsync(
            Fixture.ConnectionString,
            (short)AuditEntityKind.Document,
            entityId: documentId,
            actionCode: AuditActionCodes.DocumentUpdateDraft);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

            Func<Task> act = () => drafts.UpdateDraftAsync(
                documentId,
                number: "NEW-NUMBER",
                dateUtc: dateUtc.AddDays(1),
                ct: CancellationToken.None);

            var ex = await act.Should().ThrowAsync<DocumentWorkflowStateMismatchException>();
            ex.Which.ErrorCode.Should().Be(DocumentWorkflowStateMismatchException.ErrorCodeConst);
            ex.Which.Context.Should().ContainKey("operation").WhoseValue.Should().Be("DocumentDraft.UpdateDraft");
        }

        var afterUser = await GetUserSnapshotAsync(Fixture.ConnectionString, AuthSubject);
        afterUser.UserId.Should().Be(baselineUser.UserId);
        afterUser.UpdatedAtUtc.Should().Be(baselineUser.UpdatedAtUtc);

        var afterAuditCount = await CountAuditEventsAsync(
            Fixture.ConnectionString,
            (short)AuditEntityKind.Document,
            entityId: documentId,
            actionCode: AuditActionCodes.DocumentUpdateDraft);

        afterAuditCount.Should().Be(baselineAuditCount);
        afterAuditCount.Should().Be(0);
    }


    [Fact]
    public async Task DeleteDraft_WhenPosted_Throws_AndDoesNotWriteAuditOrTouchPlatformUser()
    {
        var dateUtc = new DateTime(2026, 3, 11, 12, 0, 0, DateTimeKind.Utc);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: AuthSubject,
                        Email: Email,
                        DisplayName: DisplayName)));
            });

        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var documentId = await CreatePostedGjeAsync(
            host,
            dateUtc,
            number: "GJE-FF-ACTOR-DEL-0001",
            CancellationToken.None);

        var baselineUser = await GetUserSnapshotAsync(Fixture.ConnectionString, AuthSubject);
        var baselineAuditCount = await CountAuditEventsAsync(
            Fixture.ConnectionString,
            (short)AuditEntityKind.Document,
            entityId: documentId,
            actionCode: AuditActionCodes.DocumentDeleteDraft);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

            Func<Task> act = () => drafts.DeleteDraftAsync(documentId, ct: CancellationToken.None);

            var ex = await act.Should().ThrowAsync<DocumentWorkflowStateMismatchException>();
            ex.Which.ErrorCode.Should().Be(DocumentWorkflowStateMismatchException.ErrorCodeConst);
            ex.Which.Context.Should().ContainKey("operation").WhoseValue.Should().Be("DocumentDraft.DeleteDraft");
        }

        var afterUser = await GetUserSnapshotAsync(Fixture.ConnectionString, AuthSubject);
        afterUser.UserId.Should().Be(baselineUser.UserId);
        afterUser.UpdatedAtUtc.Should().Be(baselineUser.UpdatedAtUtc);

        var afterAuditCount = await CountAuditEventsAsync(
            Fixture.ConnectionString,
            (short)AuditEntityKind.Document,
            entityId: documentId,
            actionCode: AuditActionCodes.DocumentDeleteDraft);

        afterAuditCount.Should().Be(baselineAuditCount);
        afterAuditCount.Should().Be(0);
    }

    private static async Task<Guid> CreatePostedGjeAsync(
        IHost host,
        DateTime dateUtc,
        string number,
        CancellationToken ct)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
        var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

        var documentId = await drafts.CreateDraftAsync(
            AccountingDocumentTypeCodes.GeneralJournalEntry,
            number,
            dateUtc,
            ct: ct);

        async Task PostCallback(IAccountingPostingContext ctx, CancellationToken innerCt)
        {
            var chart = await ctx.GetChartOfAccountsAsync(innerCt);
            ctx.Post(documentId, dateUtc, chart.Get("50"), chart.Get("90.1"), 10m);
        }

        await posting.PostAsync(documentId, PostCallback, ct);
        return documentId;
    }

    private static async Task<UserSnapshot> GetUserSnapshotAsync(string connectionString, string authSubject)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var row = await conn.QuerySingleAsync<UserRow>(
            """
            SELECT user_id AS UserId, updated_at_utc AS UpdatedAtUtc
            FROM platform_users
            WHERE auth_subject = @authSubject;
            """,
            new { authSubject });

        return new UserSnapshot(row.UserId, row.UpdatedAtUtc);
    }

    private static async Task<int> CountAuditEventsAsync(
        string connectionString,
        short entityKind,
        Guid entityId,
        string actionCode)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        return await conn.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM platform_audit_events
            WHERE entity_kind = @entityKind
              AND entity_id = @entityId
              AND action_code = @actionCode;
            """,
            new { entityKind, entityId, actionCode });
    }

    private readonly record struct UserSnapshot(Guid UserId, DateTime UpdatedAtUtc);

    private sealed class UserRow
    {
        public Guid UserId { get; init; }
        public DateTime UpdatedAtUtc { get; init; }
    }

    private sealed class FixedCurrentActorContext(ActorIdentity actor) : ICurrentActorContext
    {
        public ActorIdentity? Current => actor;
    }
}

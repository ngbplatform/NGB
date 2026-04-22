using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.UnitOfWork;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentPostingService_NoOp_WithActor_DoesNotWriteAudit_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string AuthSubject = "kc|doc-posting-noop-actor-p0";
    private static readonly DateTime NowUtc = new(2026, 2, 3, 12, 0, 0, DateTimeKind.Utc);

    private static IHost CreateHostWithActor(string connectionString)
    {
        return IntegrationHostFactory.Create(connectionString, services =>
        {
            services.AddScoped<ICurrentActorContext>(_ =>
                new FixedCurrentActorContext(new ActorIdentity(
                    AuthSubject: AuthSubject,
                    Email: "noop.actor@example.com",
                    DisplayName: "NoOp Actor")));
        });
    }

    [Fact]
    public async Task PostAsync_WhenAlreadyPosted_WithActor_IsStrictNoOp_NoAudit_NoActor_NoPostingSideEffects()
    {
        using var host = CreateHostWithActor(Fixture.ConnectionString);
        var docId = Guid.CreateVersion7();

        await CreateDocumentAsync(host, new DocumentRecord
        {
            Id = docId,
            TypeCode = "it.noop.post.actor",
            Number = "NOOP-ACTOR-POST-1",
            DateUtc = NowUtc,
            Status = DocumentStatus.Posted,
            CreatedAtUtc = NowUtc,
            UpdatedAtUtc = NowUtc,
            PostedAtUtc = NowUtc,
            MarkedForDeletionAtUtc = null
        });

        var baseline = await CaptureBaselineAsync();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

            var act = () => posting.PostAsync(
                docId,
                (_, __) => throw new XunitException("postingAction must NOT be invoked for no-op Post"),
                CancellationToken.None);

            await act.Should().NotThrowAsync();
        }

        await AssertNoOpAsync(
            docId,
            baseline,
            expectedStatus: DocumentStatus.Posted,
            expectedPostedAtUtc: NowUtc,
            expectedMarkedForDeletionAtUtc: null,
            expectedUpdatedAtUtc: NowUtc);
    }

    [Fact]
    public async Task UnpostAsync_WhenDraft_WithActor_IsStrictNoOp_NoAudit_NoActor_NoPostingSideEffects()
    {
        using var host = CreateHostWithActor(Fixture.ConnectionString);
        var docId = Guid.CreateVersion7();

        await CreateDocumentAsync(host, new DocumentRecord
        {
            Id = docId,
            TypeCode = "it.noop.unpost.actor",
            Number = "NOOP-ACTOR-UNPOST-1",
            DateUtc = NowUtc,
            Status = DocumentStatus.Draft,
            CreatedAtUtc = NowUtc,
            UpdatedAtUtc = NowUtc,
            PostedAtUtc = null,
            MarkedForDeletionAtUtc = null
        });

        var baseline = await CaptureBaselineAsync();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            await posting.Invoking(p => p.UnpostAsync(docId, CancellationToken.None)).Should().NotThrowAsync();
        }

        await AssertNoOpAsync(
            docId,
            baseline,
            expectedStatus: DocumentStatus.Draft,
            expectedPostedAtUtc: null,
            expectedMarkedForDeletionAtUtc: null,
            expectedUpdatedAtUtc: NowUtc);
    }

    [Fact]
    public async Task MarkForDeletionAsync_WhenAlreadyMarked_WithActor_IsStrictNoOp_NoAudit_NoActor()
    {
        using var host = CreateHostWithActor(Fixture.ConnectionString);
        var docId = Guid.CreateVersion7();
        var markedAt = new DateTime(2026, 2, 3, 10, 0, 0, DateTimeKind.Utc);

        await CreateDocumentAsync(host, new DocumentRecord
        {
            Id = docId,
            TypeCode = "it.noop.mark.actor",
            Number = "NOOP-ACTOR-MARK-1",
            DateUtc = NowUtc,
            Status = DocumentStatus.MarkedForDeletion,
            CreatedAtUtc = NowUtc,
            UpdatedAtUtc = NowUtc,
            PostedAtUtc = null,
            MarkedForDeletionAtUtc = markedAt
        });

        var baseline = await CaptureBaselineAsync();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            await posting.Invoking(p => p.MarkForDeletionAsync(docId, CancellationToken.None)).Should().NotThrowAsync();
        }

        await AssertNoOpAsync(
            docId,
            baseline,
            expectedStatus: DocumentStatus.MarkedForDeletion,
            expectedPostedAtUtc: null,
            expectedMarkedForDeletionAtUtc: markedAt,
            expectedUpdatedAtUtc: NowUtc);
    }

    [Fact]
    public async Task UnmarkForDeletionAsync_WhenAlreadyDraft_WithActor_IsStrictNoOp_NoAudit_NoActor()
    {
        using var host = CreateHostWithActor(Fixture.ConnectionString);
        var docId = Guid.CreateVersion7();

        await CreateDocumentAsync(host, new DocumentRecord
        {
            Id = docId,
            TypeCode = "it.noop.unmark.actor",
            Number = "NOOP-ACTOR-UNMARK-1",
            DateUtc = NowUtc,
            Status = DocumentStatus.Draft,
            CreatedAtUtc = NowUtc,
            UpdatedAtUtc = NowUtc,
            PostedAtUtc = null,
            MarkedForDeletionAtUtc = null
        });

        var baseline = await CaptureBaselineAsync();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            await posting.Invoking(p => p.UnmarkForDeletionAsync(docId, CancellationToken.None)).Should().NotThrowAsync();
        }

        await AssertNoOpAsync(
            docId,
            baseline,
            expectedStatus: DocumentStatus.Draft,
            expectedPostedAtUtc: null,
            expectedMarkedForDeletionAtUtc: null,
            expectedUpdatedAtUtc: NowUtc);
    }

    private sealed record Baseline(int AuditEvents, int AuditChanges, int UsersForSubject);

    private async Task<Baseline> CaptureBaselineAsync()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var eventsCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_events;");
        var changesCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_event_changes;");
        var usersCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_users WHERE auth_subject = @s;",
            new { s = AuthSubject });

        return new Baseline(eventsCount, changesCount, usersCount);
    }

    private async Task AssertNoOpAsync(
        Guid documentId,
        Baseline baseline,
        DocumentStatus expectedStatus,
        DateTime? expectedPostedAtUtc,
        DateTime? expectedMarkedForDeletionAtUtc,
        DateTime expectedUpdatedAtUtc)
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var doc = await conn.QuerySingleAsync<(short status, DateTime updated_at_utc, DateTime? posted_at_utc, DateTime? marked_for_deletion_at_utc)>(
            "SELECT status, updated_at_utc, posted_at_utc, marked_for_deletion_at_utc FROM documents WHERE id = @id;",
            new { id = documentId });

        ((DocumentStatus)doc.status).Should().Be(expectedStatus);
        doc.updated_at_utc.Should().Be(expectedUpdatedAtUtc);
        doc.posted_at_utc.Should().Be(expectedPostedAtUtc);
        doc.marked_for_deletion_at_utc.Should().Be(expectedMarkedForDeletionAtUtc);

        // Posting side effects must not exist.
        (await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM accounting_posting_state WHERE document_id = @id;",
            new { id = documentId })).Should().Be(0);

        (await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM accounting_register_main WHERE document_id = @id;",
            new { id = documentId })).Should().Be(0);

        (await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM operational_register_write_state WHERE document_id = @id;",
            new { id = documentId })).Should().Be(0);

        // No-op must not produce audit events for this document.
        (await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_audit_events WHERE entity_id = @id;",
            new { id = documentId })).Should().Be(0);

        (await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_audit_event_changes c " +
            "JOIN platform_audit_events e ON e.audit_event_id = c.audit_event_id " +
            "WHERE e.entity_id = @id;",
            new { id = documentId })).Should().Be(0);

        // And no global audit/actor side effects should be present either.
        (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_events;"))
            .Should().Be(baseline.AuditEvents);

        (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_event_changes;"))
            .Should().Be(baseline.AuditChanges);

        (await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_users WHERE auth_subject = @s;",
            new { s = AuthSubject }))
            .Should().Be(baseline.UsersForSubject);
    }

    private static async Task CreateDocumentAsync(IHost host, DocumentRecord record)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await repo.CreateAsync(record, ct);
        }, CancellationToken.None);
    }

    private sealed class FixedCurrentActorContext(ActorIdentity actor) : ICurrentActorContext
    {
        public ActorIdentity? Current => actor;
    }
}

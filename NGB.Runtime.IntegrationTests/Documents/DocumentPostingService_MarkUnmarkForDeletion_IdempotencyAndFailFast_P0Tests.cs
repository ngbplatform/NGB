using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.AuditLog;
using NGB.Core.Documents;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Documents;
using NGB.Runtime.Documents.Workflow;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentPostingService_MarkUnmarkForDeletion_IdempotencyAndFailFast_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task MarkForDeletionAsync_WhenCalledTwice_IsIdempotent_NoSecondAudit_AndDoesNotChangeTimestamps()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var dateUtc = new DateTime(2026, 02, 04, 12, 0, 0, DateTimeKind.Utc);

        Guid documentId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            documentId = await drafts.CreateDraftAsync(
                typeCode: "demo.sales_invoice",
                number: "MARK-IDEM-1",
                dateUtc: dateUtc,
                manageTransaction: true,
                ct: CancellationToken.None);
        }

        DateTime firstUpdatedAt;
        DateTime? firstMarkedAt;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            await posting.MarkForDeletionAsync(documentId, CancellationToken.None);
        }

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            var row = await conn.QuerySingleAsync<(short Status, DateTime UpdatedAtUtc, DateTime? MarkedAtUtc)>(
                "SELECT status, updated_at_utc AS UpdatedAtUtc, marked_for_deletion_at_utc AS MarkedAtUtc FROM documents WHERE id = @id;",
                new { id = documentId });

            row.Status.Should().Be((short)DocumentStatus.MarkedForDeletion);
            row.MarkedAtUtc.Should().NotBeNull();

            firstUpdatedAt = row.UpdatedAtUtc;
            firstMarkedAt = row.MarkedAtUtc;
        }

        // Second call must be idempotent no-op.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            await posting.MarkForDeletionAsync(documentId, CancellationToken.None);
        }

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            var row2 = await conn.QuerySingleAsync<(short Status, DateTime UpdatedAtUtc, DateTime? MarkedAtUtc)>(
                "SELECT status, updated_at_utc AS UpdatedAtUtc, marked_for_deletion_at_utc AS MarkedAtUtc FROM documents WHERE id = @id;",
                new { id = documentId });

            row2.Status.Should().Be((short)DocumentStatus.MarkedForDeletion);
            row2.UpdatedAtUtc.Should().Be(firstUpdatedAt);
            row2.MarkedAtUtc.Should().Be(firstMarkedAt);
        }

        // Only ONE audit event must exist for MarkForDeletion.
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

            events.Should().ContainSingle();
        }
    }

    [Fact]
    public async Task MarkForDeletionAsync_WhenPosted_Throws_AndNoAudit()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var nowUtc = new DateTime(2026, 02, 04, 12, 0, 0, DateTimeKind.Utc);
        var docId = Guid.CreateVersion7();

        await CreateDocumentAsync(host, new DocumentRecord
        {
            Id = docId,
            TypeCode = "it.posted.markdel",
            Number = "POSTED-MARKDEL-1",
            DateUtc = nowUtc,
            Status = DocumentStatus.Posted,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            PostedAtUtc = nowUtc,
            MarkedForDeletionAtUtc = null
        });

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

            var act = () => posting.MarkForDeletionAsync(docId, CancellationToken.None);

            var ex = await act.Should().ThrowAsync<DocumentWorkflowStateMismatchException>();
            ex.Which.ErrorCode.Should().Be(DocumentWorkflowStateMismatchException.ErrorCodeConst);
            ex.Which.Context["operation"].Should().Be("Document.MarkForDeletion");
            ex.Which.Context["expectedState"].Should().Be("Draft");
            ex.Which.Context["actualState"].Should().Be("Posted");
        }

        await AssertNoAuditForAsync(docId, AuditActionCodes.DocumentMarkForDeletion);
    }

    [Fact]
    public async Task UnmarkForDeletionAsync_WhenPosted_Throws_AndNoAudit()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var nowUtc = new DateTime(2026, 02, 04, 12, 0, 0, DateTimeKind.Utc);
        var docId = Guid.CreateVersion7();

        await CreateDocumentAsync(host, new DocumentRecord
        {
            Id = docId,
            TypeCode = "it.posted.unmarkdel",
            Number = "POSTED-UNMARKDEL-1",
            DateUtc = nowUtc,
            Status = DocumentStatus.Posted,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            PostedAtUtc = nowUtc,
            MarkedForDeletionAtUtc = null
        });

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

            var act = () => posting.UnmarkForDeletionAsync(docId, CancellationToken.None);

            var ex = await act.Should().ThrowAsync<DocumentWorkflowStateMismatchException>();
            ex.Which.ErrorCode.Should().Be(DocumentWorkflowStateMismatchException.ErrorCodeConst);
            ex.Which.Context["operation"].Should().Be("Document.UnmarkForDeletion");
            ex.Which.Context["expectedState"].Should().Be("MarkedForDeletion");
            ex.Which.Context["actualState"].Should().Be("Posted");
        }

        await AssertNoAuditForAsync(docId, AuditActionCodes.DocumentUnmarkForDeletion);
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

    private async Task AssertNoAuditForAsync(Guid entityId, string actionCode)
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        (await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM platform_audit_events WHERE entity_kind = @k AND entity_id = @id AND action_code = @a;",
                new { k = (short)AuditEntityKind.Document, id = entityId, a = actionCode }))
            .Should().Be(0);

        (await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM platform_audit_event_changes c " +
                "JOIN platform_audit_events e ON e.audit_event_id = c.audit_event_id " +
                "WHERE e.entity_kind = @k AND e.entity_id = @id AND e.action_code = @a;",
                new { k = (short)AuditEntityKind.Document, id = entityId, a = actionCode }))
            .Should().Be(0);
    }
}

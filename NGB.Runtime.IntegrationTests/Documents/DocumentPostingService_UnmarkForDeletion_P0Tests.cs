using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.AuditLog;
using NGB.Core.Documents;
using NGB.Persistence.AuditLog;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentPostingService_UnmarkForDeletion_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task UnmarkForDeletionAsync_WhenMarkedForDeletion_RestoresDraftStatus_AndWritesAudit()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var dateUtc = new DateTime(2026, 01, 15, 12, 00, 00, DateTimeKind.Utc);

        Guid documentId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            documentId = await drafts.CreateDraftAsync(
                typeCode: "demo.sales_invoice",
                number: "UNMARK-1",
                dateUtc: dateUtc,
                manageTransaction: true,
                ct: CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            await posting.MarkForDeletionAsync(documentId, CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            await posting.UnmarkForDeletionAsync(documentId, CancellationToken.None);

            // second call -> idempotent no-op
            await posting.UnmarkForDeletionAsync(documentId, CancellationToken.None);
        }

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            var row = await conn.QuerySingleAsync<(short Status, DateTime? MarkedAtUtc)>(
                "SELECT status AS Status, marked_for_deletion_at_utc AS MarkedAtUtc FROM documents WHERE id = @id;",
                new { id = documentId });

            row.Status.Should().Be((short)DocumentStatus.Draft);
            row.MarkedAtUtc.Should().BeNull();
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: documentId,
                    ActionCode: AuditActionCodes.DocumentUnmarkForDeletion,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().ContainSingle(); // second call must not log a second event
        }
    }
}

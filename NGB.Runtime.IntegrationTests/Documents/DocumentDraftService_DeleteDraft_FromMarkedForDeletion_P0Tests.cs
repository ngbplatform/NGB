using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.AuditLog;
using NGB.Persistence.AuditLog;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentDraftService_DeleteDraft_FromMarkedForDeletion_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task DeleteDraftAsync_WhenMarkedForDeletion_DeletesDraftAndWritesAudit()
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
                number: "DEL-MARK-1",
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
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            (await drafts.DeleteDraftAsync(documentId, manageTransaction: true, ct: CancellationToken.None))
                .Should()
                .BeTrue();
        }

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            var docCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM documents WHERE id = @id;",
                new { id = documentId });

            docCount.Should().Be(0);
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

            markEvents.Should().ContainSingle();

            var deleteEvents = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: documentId,
                    ActionCode: AuditActionCodes.DocumentDeleteDraft,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            deleteEvents.Should().ContainSingle();
        }
    }
}

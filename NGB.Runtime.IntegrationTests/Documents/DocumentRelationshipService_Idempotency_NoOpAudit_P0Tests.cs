using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Extensions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentRelationshipService_Idempotency_NoOpAudit_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateTime NowUtc = new(2026, 2, 4, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task CreateAsync_WhenCalledTwice_IsIdempotent_NoSecondAudit_AndNoDuplicateRow()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var fromId = Guid.CreateVersion7();
        var toId = Guid.CreateVersion7();
        await SeedDraftDocumentsAsync(host, fromId, toId);

        var relationshipId = DeterministicGuid.Create($"DocumentRelationship|{fromId:D}|based_on|{toId:D}");

        // First call must create row and write exactly one audit event (and changes).
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();
            await svc.CreateAsync(fromId, toId, "based_on", manageTransaction: true, ct: CancellationToken.None);
        }

        (await CountRelationshipRowsAsync(relationshipId)).Should().Be(1);

        var (events1, changes1) = await CountAuditAsync();

        // Second call must be a no-op: still 1 row, audit counts unchanged.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();
            await svc.CreateAsync(fromId, toId, "based_on", manageTransaction: true, ct: CancellationToken.None);
        }

        (await CountRelationshipRowsAsync(relationshipId)).Should().Be(1);
        var (events2, changes2) = await CountAuditAsync();

        events2.Should().Be(events1, "no-op relationship create must not be audited");
        changes2.Should().Be(changes1, "no-op relationship create must not write field changes");
    }

    [Fact]
    public async Task DeleteAsync_WhenCalledTwice_IsIdempotent_NoSecondAudit_AndStaysDeleted()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var fromId = Guid.CreateVersion7();
        var toId = Guid.CreateVersion7();
        await SeedDraftDocumentsAsync(host, fromId, toId);

        var relationshipId = DeterministicGuid.Create($"DocumentRelationship|{fromId:D}|based_on|{toId:D}");

        // Create once.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();
            await svc.CreateAsync(fromId, toId, "based_on", manageTransaction: true, ct: CancellationToken.None);
        }

        (await CountRelationshipRowsAsync(relationshipId)).Should().Be(1);

        // Delete once.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();
            await svc.DeleteAsync(fromId, toId, "based_on", manageTransaction: true, ct: CancellationToken.None);
        }

        (await CountRelationshipRowsAsync(relationshipId)).Should().Be(0);

        var (events1, changes1) = await CountAuditAsync();

        // Second delete must be a no-op: still 0 rows, audit counts unchanged.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();
            await svc.DeleteAsync(fromId, toId, "based_on", manageTransaction: true, ct: CancellationToken.None);
        }

        (await CountRelationshipRowsAsync(relationshipId)).Should().Be(0);

        var (events2, changes2) = await CountAuditAsync();

        events2.Should().Be(events1, "no-op relationship delete must not be audited");
        changes2.Should().Be(changes1, "no-op relationship delete must not write field changes");
    }

    private static async Task SeedDraftDocumentsAsync(IHost host, Guid fromId, Guid toId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await repo.CreateAsync(new DocumentRecord
            {
                Id = fromId,
                TypeCode = "it.doc.from.idem",
                Number = "FROM-IDEM",
                DateUtc = NowUtc,
                Status = DocumentStatus.Draft,
                CreatedAtUtc = NowUtc,
                UpdatedAtUtc = NowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, ct);

            await repo.CreateAsync(new DocumentRecord
            {
                Id = toId,
                TypeCode = "it.doc.to.idem",
                Number = "TO-IDEM",
                DateUtc = NowUtc,
                Status = DocumentStatus.Draft,
                CreatedAtUtc = NowUtc,
                UpdatedAtUtc = NowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, ct);
        }, CancellationToken.None);
    }

    private async Task<int> CountRelationshipRowsAsync(Guid relationshipId)
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM document_relationships WHERE relationship_id = @id;",
            new { id = relationshipId });
    }

    private async Task<(int Events, int Changes)> CountAuditAsync()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var events = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_events;");
        var changes = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_event_changes;");
        return (events, changes);
    }
}

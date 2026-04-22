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
public sealed class DocumentRelationshipService_Bidirectional_Idempotency_NoOpAudit_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateTime NowUtc = new(2026, 2, 4, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task CreateRelatedTo_WhenCalledTwice_IsIdempotent_CreatesExactlyTwoEdges_AndNoSecondAudit()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();
        await SeedDraftDocumentsAsync(host, a, b);

        var ab = DeterministicGuid.Create($"DocumentRelationship|{a:D}|related_to|{b:D}");
        var ba = DeterministicGuid.Create($"DocumentRelationship|{b:D}|related_to|{a:D}");

        // First create: must create both directions.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();
            await svc.CreateAsync(a, b, "related_to", manageTransaction: true, ct: CancellationToken.None);
        }

        (await CountRelationshipRowAsync(ab)).Should().Be(1);
        (await CountRelationshipRowAsync(ba)).Should().Be(1);

        var (events1, changes1) = await CountAuditAsync();

        // Second create: must be a no-op.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();
            await svc.CreateAsync(a, b, "related_to", manageTransaction: true, ct: CancellationToken.None);
        }

        (await CountRelationshipRowAsync(ab)).Should().Be(1);
        (await CountRelationshipRowAsync(ba)).Should().Be(1);

        var (events2, changes2) = await CountAuditAsync();
        events2.Should().Be(events1, "no-op bidirectional relationship create must not be audited");
        changes2.Should().Be(changes1, "no-op bidirectional relationship create must not write field changes");
    }

    [Fact]
    public async Task DeleteRelatedTo_WhenCalledTwice_IsIdempotent_DeletesBothEdges_AndNoSecondAudit()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();
        await SeedDraftDocumentsAsync(host, a, b);

        var ab = DeterministicGuid.Create($"DocumentRelationship|{a:D}|related_to|{b:D}");
        var ba = DeterministicGuid.Create($"DocumentRelationship|{b:D}|related_to|{a:D}");

        // Create once.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();
            await svc.CreateAsync(a, b, "related_to", manageTransaction: true, ct: CancellationToken.None);
        }

        (await CountRelationshipRowAsync(ab)).Should().Be(1);
        (await CountRelationshipRowAsync(ba)).Should().Be(1);

        // Delete once (should delete both directions).
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();
            await svc.DeleteAsync(a, b, "related_to", manageTransaction: true, ct: CancellationToken.None);
        }

        (await CountRelationshipRowAsync(ab)).Should().Be(0);
        (await CountRelationshipRowAsync(ba)).Should().Be(0);

        var (events1, changes1) = await CountAuditAsync();

        // Delete again: no-op.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();
            await svc.DeleteAsync(a, b, "related_to", manageTransaction: true, ct: CancellationToken.None);
        }

        (await CountRelationshipRowAsync(ab)).Should().Be(0);
        (await CountRelationshipRowAsync(ba)).Should().Be(0);

        var (events2, changes2) = await CountAuditAsync();
        events2.Should().Be(events1, "no-op bidirectional relationship delete must not be audited");
        changes2.Should().Be(changes1, "no-op bidirectional relationship delete must not write field changes");
    }

    private static async Task SeedDraftDocumentsAsync(IHost host, Guid a, Guid b)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await repo.CreateAsync(new DocumentRecord
            {
                Id = a,
                TypeCode = "it.doc.a",
                Number = "A-REL",
                DateUtc = NowUtc,
                Status = DocumentStatus.Draft,
                CreatedAtUtc = NowUtc,
                UpdatedAtUtc = NowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, ct);

            await repo.CreateAsync(new DocumentRecord
            {
                Id = b,
                TypeCode = "it.doc.b",
                Number = "B-REL",
                DateUtc = NowUtc,
                Status = DocumentStatus.Draft,
                CreatedAtUtc = NowUtc,
                UpdatedAtUtc = NowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, ct);
        }, CancellationToken.None);
    }

    private async Task<int> CountRelationshipRowAsync(Guid relationshipId)
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

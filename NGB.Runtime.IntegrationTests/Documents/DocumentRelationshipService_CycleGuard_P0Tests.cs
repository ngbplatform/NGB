using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentRelationshipService_CycleGuard_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateTime NowUtc = new(2026, 2, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task CreateAsync_DirectedRelationship_WhenWouldCreateCycle_Throws()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();
        var c = Guid.CreateVersion7();
        await SeedDraftDocumentsAsync(host, a, b, c);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();

            await svc.CreateAsync(a, b, "based_on", manageTransaction: true, ct: CancellationToken.None);
            await svc.CreateAsync(b, c, "based_on", manageTransaction: true, ct: CancellationToken.None);

            // C -> A would close the loop: A -> B -> C -> A
            var act = () => svc.CreateAsync(c, a, "based_on", manageTransaction: true, ct: CancellationToken.None);
            var ex = await act.Should().ThrowAsync<DocumentRelationshipValidationException>();
            ex.Which.AssertNgbError(
                DocumentRelationshipValidationException.Code,
                "reason",
                "relationshipCode",
                "fromDocumentId",
                "toDocumentId");
            ex.Which.Reason.Should().Be("cycle_detected");
        }

        // The failing call must not create a row.
        (await CountRelationshipsAsync(codeNorm: "based_on")).Should().Be(2);
    }

    [Fact]
    public async Task CreateAsync_BidirectionalRelationship_AllowsCycles()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();
        var c = Guid.CreateVersion7();
        await SeedDraftDocumentsAsync(host, a, b, c);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();

            await svc.CreateAsync(a, b, "related_to", manageTransaction: true, ct: CancellationToken.None);
            await svc.CreateAsync(b, c, "related_to", manageTransaction: true, ct: CancellationToken.None);
            await svc.CreateAsync(c, a, "related_to", manageTransaction: true, ct: CancellationToken.None);
        }

        // Each call creates two edges (A->B and B->A). Total 3 * 2 = 6.
        (await CountRelationshipsAsync(codeNorm: "related_to")).Should().Be(6);
    }

    private static async Task SeedDraftDocumentsAsync(IHost host, params Guid[] ids)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            for (var i = 0; i < ids.Length; i++)
            {
                await repo.CreateAsync(new DocumentRecord
                {
                    Id = ids[i],
                    TypeCode = $"it.doc.cycle.{i}",
                    Number = $"CYCLE-{i}",
                    DateUtc = NowUtc,
                    Status = DocumentStatus.Draft,
                    CreatedAtUtc = NowUtc,
                    UpdatedAtUtc = NowUtc,
                    PostedAtUtc = null,
                    MarkedForDeletionAtUtc = null
                }, ct);
            }
        }, CancellationToken.None);
    }

    private async Task<int> CountRelationshipsAsync(string codeNorm)
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();
        
        return await conn.ExecuteScalarAsync<int>(
            "SELECT count(1) FROM document_relationships WHERE relationship_code_norm = @codeNorm;",
            new { codeNorm });
    }
}

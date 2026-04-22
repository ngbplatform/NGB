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
public sealed class DocumentRelationshipService_NormalizesRelationshipCode_CaseAndTrim_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateTime NowUtc = new(2026, 2, 4, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task CreateDelete_NormalizesRelationshipCode_ByTrimAndLowercase_AndIsIdempotentAcrossVariants()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var fromId = Guid.CreateVersion7();
        var toId = Guid.CreateVersion7();

        await SeedDraftDocumentsAsync(host, fromId, toId);

        var codeNorm = "based_on";
        var relationshipId = DeterministicGuid.Create($"DocumentRelationship|{fromId:D}|{codeNorm}|{toId:D}");

        // Create with messy casing + surrounding whitespace.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();
            await svc.CreateAsync(fromId, toId, "   BaSeD_On   ", manageTransaction: true, ct: CancellationToken.None);
        }

        // Row must exist, and norm must be lowercased.
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            var row = await conn.QuerySingleAsync<(string CodeNorm, string CodeRaw)>(
                "SELECT relationship_code_norm AS CodeNorm, relationship_code AS CodeRaw " +
                "FROM document_relationships WHERE relationship_id = @id;",
                new { id = relationshipId });

            row.CodeNorm.Should().Be(codeNorm);
            row.CodeRaw.Should().NotBeNullOrWhiteSpace("raw relationship_code may preserve user input but must be present");
        }

        // Create again with different variant -> must be idempotent (still 1 row, same relationship_id).
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();
            await svc.CreateAsync(fromId, toId, "BASED_ON", manageTransaction: true, ct: CancellationToken.None);
        }

        (await CountRelationshipRowsAsync(relationshipId)).Should().Be(1);

        // Delete with another messy variant must remove it.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();
            await svc.DeleteAsync(fromId, toId, " \t bAsEd_On \n ", manageTransaction: true, ct: CancellationToken.None);
        }

        (await CountRelationshipRowsAsync(relationshipId)).Should().Be(0);
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
                TypeCode = "it.doc.from.norm",
                Number = "FROM-NORM",
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
                TypeCode = "it.doc.to.norm",
                Number = "TO-NORM",
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
}

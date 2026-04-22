using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

/// <summary>
/// P1: DocumentRelationshipsMigration declares ON DELETE CASCADE for both from_document_id and to_document_id.
/// We must prove that deleting a Draft document does not leave orphan relationship rows.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DocumentRelationships_CascadeDelete_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task DeleteDraft_Cascades_DocumentRelationships_WhenDeletingFromDocument()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
        var rels = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();

        var fromId = await drafts.CreateDraftAsync(
            typeCode: "it_alpha",
            number: "A-0001",
            dateUtc: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            manageTransaction: true,
            ct: CancellationToken.None);

        var toId = await drafts.CreateDraftAsync(
            typeCode: "it_beta",
            number: "B-0001",
            dateUtc: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            manageTransaction: true,
            ct: CancellationToken.None);

        (await rels.CreateAsync(fromId, toId, "based_on", manageTransaction: true, ct: CancellationToken.None))
            .Should().BeTrue();

        (await CountByDocumentAsync(fromId)).Should().Be(1);
        (await CountByDocumentAsync(toId)).Should().Be(1);

        // Deleting the "from" document must cascade-delete relationship rows.
        (await drafts.DeleteDraftAsync(fromId, manageTransaction: true, ct: CancellationToken.None))
            .Should().BeTrue();

        (await CountByDocumentAsync(fromId)).Should().Be(0);
        (await CountByDocumentAsync(toId)).Should().Be(0);
    }

    [Fact]
    public async Task DeleteDraft_Cascades_DocumentRelationships_WhenDeletingToDocument()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
        var rels = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();

        var fromId = await drafts.CreateDraftAsync(
            typeCode: "it_alpha",
            number: "A-0002",
            dateUtc: new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc),
            manageTransaction: true,
            ct: CancellationToken.None);

        var toId = await drafts.CreateDraftAsync(
            typeCode: "it_beta",
            number: "B-0002",
            dateUtc: new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc),
            manageTransaction: true,
            ct: CancellationToken.None);

        (await rels.CreateAsync(fromId, toId, "based_on", manageTransaction: true, ct: CancellationToken.None))
            .Should().BeTrue();

        (await CountByDocumentAsync(fromId)).Should().Be(1);
        (await CountByDocumentAsync(toId)).Should().Be(1);

        // Deleting the "to" document must cascade-delete relationship rows.
        (await drafts.DeleteDraftAsync(toId, manageTransaction: true, ct: CancellationToken.None))
            .Should().BeTrue();

        (await CountByDocumentAsync(fromId)).Should().Be(0);
        (await CountByDocumentAsync(toId)).Should().Be(0);
    }

    private async Task<int> CountByDocumentAsync(Guid documentId)
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM document_relationships WHERE from_document_id=@Id OR to_document_id=@Id;",
            new { Id = documentId });
    }
}

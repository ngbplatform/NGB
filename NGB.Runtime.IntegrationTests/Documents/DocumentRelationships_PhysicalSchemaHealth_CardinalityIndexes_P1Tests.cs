using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NGB.Persistence.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

/// <summary>
/// P1: Schema health must detect drift for built-in cardinality guard indexes.
/// These partial unique indexes enforce relationship type cardinalities at the DB level.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DocumentRelationships_PhysicalSchemaHealth_CardinalityIndexes_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Theory]
    [InlineData("ux_docrel_from_rev_of")]
    [InlineData("ux_docrel_from_created_from")]
    [InlineData("ux_docrel_from_supersedes")]
    [InlineData("ux_docrel_to_supersedes")]
    public async Task GetAsync_WhenCardinalityIndexMissing_ReportsMissingIndex(string indexName)
    {
        await DropIndexAsync(Fixture.ConnectionString, indexName);

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var reader = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipsPhysicalSchemaHealthReader>();
        var health = await reader.GetAsync(CancellationToken.None);

        health.Exists.Should().BeTrue();
        health.MissingIndexes.Should().Contain(indexName);
    }

    private static async Task DropIndexAsync(string cs, string indexName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand($"DROP INDEX IF EXISTS {indexName};", conn);
        await cmd.ExecuteNonQueryAsync();
    }
}

using Dapper;
using FluentAssertions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.DatabaseGuards;

/// <summary>
/// P0: platform_dimension_sets is append-only at the database level.
/// A DimensionSetId represents an immutable snapshot; once materialized it must never change.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DatabaseGuards_PlatformDimensionSets_AppendOnly_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task PlatformDimensionSets_IsAppendOnly_UpdateAndDeleteThrow()
    {
        await Fixture.ResetDatabaseAsync();
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var setId = Guid.CreateVersion7();

        await conn.ExecuteAsync(
            "INSERT INTO platform_dimension_sets(dimension_set_id) VALUES (@Id);",
            new { Id = setId });

        var updateAct = async () =>
        {
            await conn.ExecuteAsync(
                "UPDATE platform_dimension_sets SET created_at_utc = created_at_utc WHERE dimension_set_id = @Id;",
                new { Id = setId });
        };

        var updateEx = await updateAct.Should().ThrowAsync<PostgresException>();
        updateEx.Which.SqlState.Should().Be("55000");

        var deleteAct = async () =>
        {
            await conn.ExecuteAsync(
                "DELETE FROM platform_dimension_sets WHERE dimension_set_id = @Id;",
                new { Id = setId });
        };

        var deleteEx = await deleteAct.Should().ThrowAsync<PostgresException>();
        deleteEx.Which.SqlState.Should().Be("55000");
    }
}

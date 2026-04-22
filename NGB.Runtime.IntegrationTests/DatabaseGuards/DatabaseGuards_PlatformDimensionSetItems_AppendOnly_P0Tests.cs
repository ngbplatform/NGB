using Dapper;
using FluentAssertions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.DatabaseGuards;

/// <summary>
/// P0: platform_dimension_set_items is append-only at the database level.
/// Once a DimensionSetId is materialized, its items must never change.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DatabaseGuards_PlatformDimensionSetItems_AppendOnly_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task PlatformDimensionSetItems_IsAppendOnly_UpdateAndDeleteThrow()
    {
        await Fixture.ResetDatabaseAsync();
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var dimId = Guid.CreateVersion7();
        var setId = Guid.CreateVersion7();
        var valueId = Guid.CreateVersion7();

        await conn.ExecuteAsync(
            "INSERT INTO platform_dimensions(dimension_id, code, name) VALUES (@Id, 'DEPT', 'Department');",
            new { Id = dimId });

        await conn.ExecuteAsync(
            "INSERT INTO platform_dimension_sets(dimension_set_id) VALUES (@Id);",
            new { Id = setId });

        await conn.ExecuteAsync(
            "INSERT INTO platform_dimension_set_items(dimension_set_id, dimension_id, value_id) VALUES (@SetId, @DimId, @ValueId);",
            new { SetId = setId, DimId = dimId, ValueId = valueId });

        var updateAct = async () =>
        {
            await conn.ExecuteAsync(
                "UPDATE platform_dimension_set_items SET value_id = @NewValueId WHERE dimension_set_id=@SetId AND dimension_id=@DimId;",
                new { SetId = setId, DimId = dimId, NewValueId = Guid.CreateVersion7() });
        };

        var updateEx = await updateAct.Should().ThrowAsync<PostgresException>();
        updateEx.Which.SqlState.Should().Be("55000");

        var deleteAct = async () =>
        {
            await conn.ExecuteAsync(
                "DELETE FROM platform_dimension_set_items WHERE dimension_set_id=@SetId AND dimension_id=@DimId;",
                new { SetId = setId, DimId = dimId });
        };

        var deleteEx = await deleteAct.Should().ThrowAsync<PostgresException>();
        deleteEx.Which.SqlState.Should().Be("55000");
    }
}

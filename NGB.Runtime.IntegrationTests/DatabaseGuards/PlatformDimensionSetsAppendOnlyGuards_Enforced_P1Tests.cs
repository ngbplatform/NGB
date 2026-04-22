using Dapper;
using FluentAssertions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.DatabaseGuards;

/// <summary>
/// P1: platform dimension set tables are append-only.
///
/// Dimension sets are immutable snapshots: once materialized, their rows must never be UPDATED or DELETED.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PlatformDimensionSetsAppendOnlyGuards_Enforced_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task UpdateAndDeleteAreForbidden_OnDimensionSets_And_DimensionSetItems()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync(CancellationToken.None);

        var dimensionId = Guid.CreateVersion7();
        await conn.ExecuteAsync(
            """
            INSERT INTO platform_dimensions (dimension_id, code, name)
            VALUES (@id, @code, @name);
            """,
            new
            {
                id = dimensionId,
                code = "it_dim_" + dimensionId.ToString("N")[..8],
                name = "IT Dimension"
            });

        var dimensionSetId = Guid.CreateVersion7();
        await conn.ExecuteAsync(
            """
            INSERT INTO platform_dimension_sets (dimension_set_id)
            VALUES (@id);
            """,
            new { id = dimensionSetId });

        await conn.ExecuteAsync(
            """
            INSERT INTO platform_dimension_set_items (dimension_set_id, dimension_id, value_id)
            VALUES (@set_id, @dim_id, @value_id);
            """,
            new
            {
                set_id = dimensionSetId,
                dim_id = dimensionId,
                value_id = Guid.CreateVersion7()
            });

        // platform_dimension_sets: UPDATE forbidden.
        await AssertAppendOnlyGuardAsync(
            () => conn.ExecuteAsync(
                "UPDATE platform_dimension_sets SET created_at_utc = created_at_utc + interval '1 second' WHERE dimension_set_id = @id;",
                new { id = dimensionSetId }),
            expectedTableName: "platform_dimension_sets");

        // platform_dimension_sets: DELETE forbidden.
        await AssertAppendOnlyGuardAsync(
            () => conn.ExecuteAsync(
                "DELETE FROM platform_dimension_sets WHERE dimension_set_id = @id;",
                new { id = dimensionSetId }),
            expectedTableName: "platform_dimension_sets");

        // platform_dimension_set_items: UPDATE forbidden.
        await AssertAppendOnlyGuardAsync(
            () => conn.ExecuteAsync(
                "UPDATE platform_dimension_set_items SET value_id = @new_value WHERE dimension_set_id = @set_id AND dimension_id = @dim_id;",
                new
                {
                    set_id = dimensionSetId,
                    dim_id = dimensionId,
                    new_value = Guid.CreateVersion7()
                }),
            expectedTableName: "platform_dimension_set_items");

        // platform_dimension_set_items: DELETE forbidden.
        await AssertAppendOnlyGuardAsync(
            () => conn.ExecuteAsync(
                "DELETE FROM platform_dimension_set_items WHERE dimension_set_id = @set_id AND dimension_id = @dim_id;",
                new
                {
                    set_id = dimensionSetId,
                    dim_id = dimensionId
                }),
            expectedTableName: "platform_dimension_set_items");
    }

    private static async Task AssertAppendOnlyGuardAsync(Func<Task> act, string expectedTableName)
    {
        var ex = await FluentActions.Invoking(act).Should().ThrowAsync<PostgresException>();

        ex.Which.SqlState.Should().Be("55000");
        ex.Which.Message.Should().Contain("Append-only table cannot be mutated");
        ex.Which.Message.Should().Contain(expectedTableName);
    }
}

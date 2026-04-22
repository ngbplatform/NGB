using Dapper;
using FluentAssertions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.DatabaseGuards;

/// <summary>
/// P0: platform_dimension_set_items must be protected by hard DB constraints.
/// This ensures integrity even if application-layer validation is bypassed.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DatabaseConstraints_PlatformDimensionSetItems_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CheckConstraints_Forbid_EmptyIds()
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

        // Empty dimension_set_id is forbidden (Guid.Empty is reserved for the empty set header only).
        var actSetEmpty = async () =>
        {
            await conn.ExecuteAsync(
                "INSERT INTO platform_dimension_set_items(dimension_set_id, dimension_id, value_id) VALUES (@SetId, @DimId, @ValueId);",
                new { SetId = Guid.Empty, DimId = dimId, ValueId = valueId });
        };

        var exSetEmpty = await actSetEmpty.Should().ThrowAsync<PostgresException>();
        exSetEmpty.Which.SqlState.Should().Be("23514");
        exSetEmpty.Which.ConstraintName.Should().Be("ck_platform_dimset_items_set_nonempty");

        // Empty dimension_id is forbidden.
        var actDimEmpty = async () =>
        {
            await conn.ExecuteAsync(
                "INSERT INTO platform_dimension_set_items(dimension_set_id, dimension_id, value_id) VALUES (@SetId, @DimId, @ValueId);",
                new { SetId = setId, DimId = Guid.Empty, ValueId = valueId });
        };

        var exDimEmpty = await actDimEmpty.Should().ThrowAsync<PostgresException>();
        exDimEmpty.Which.SqlState.Should().Be("23514");
        exDimEmpty.Which.ConstraintName.Should().Be("ck_platform_dimset_items_dimension_nonempty");

        // Empty value_id is forbidden.
        var actValueEmpty = async () =>
        {
            await conn.ExecuteAsync(
                "INSERT INTO platform_dimension_set_items(dimension_set_id, dimension_id, value_id) VALUES (@SetId, @DimId, @ValueId);",
                new { SetId = setId, DimId = dimId, ValueId = Guid.Empty });
        };

        var exValueEmpty = await actValueEmpty.Should().ThrowAsync<PostgresException>();
        exValueEmpty.Which.SqlState.Should().Be("23514");
        exValueEmpty.Which.ConstraintName.Should().Be("ck_platform_dimset_items_value_nonempty");
    }

    [Fact]
    public async Task ForeignKeys_AreEnforced_ByDb()
    {
        await Fixture.ResetDatabaseAsync();
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var existingDimId = Guid.CreateVersion7();
        var existingSetId = Guid.CreateVersion7();

        await conn.ExecuteAsync(
            "INSERT INTO platform_dimensions(dimension_id, code, name) VALUES (@Id, 'DEPT', 'Department');",
            new { Id = existingDimId });

        await conn.ExecuteAsync(
            "INSERT INTO platform_dimension_sets(dimension_set_id) VALUES (@Id);",
            new { Id = existingSetId });

        // Missing dimension_set_id must be rejected.
        var actMissingSet = async () =>
        {
            await conn.ExecuteAsync(
                "INSERT INTO platform_dimension_set_items(dimension_set_id, dimension_id, value_id) VALUES (@SetId, @DimId, @ValueId);",
                new { SetId = Guid.CreateVersion7(), DimId = existingDimId, ValueId = Guid.CreateVersion7() });
        };

        var exMissingSet = await actMissingSet.Should().ThrowAsync<PostgresException>();
        exMissingSet.Which.SqlState.Should().Be("23503");
        exMissingSet.Which.ConstraintName.Should().Be("fk_platform_dimset_items_set");

        // Missing dimension_id must be rejected.
        var actMissingDim = async () =>
        {
            await conn.ExecuteAsync(
                "INSERT INTO platform_dimension_set_items(dimension_set_id, dimension_id, value_id) VALUES (@SetId, @DimId, @ValueId);",
                new { SetId = existingSetId, DimId = Guid.CreateVersion7(), ValueId = Guid.CreateVersion7() });
        };

        var exMissingDim = await actMissingDim.Should().ThrowAsync<PostgresException>();
        exMissingDim.Which.SqlState.Should().Be("23503");
        exMissingDim.Which.ConstraintName.Should().Be("fk_platform_dimset_items_dimension");
    }

    [Fact]
    public async Task PrimaryKey_Forbids_DuplicateDimensionPerSet()
    {
        await Fixture.ResetDatabaseAsync();
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var dimId = Guid.CreateVersion7();
        var setId = Guid.CreateVersion7();

        await conn.ExecuteAsync(
            "INSERT INTO platform_dimensions(dimension_id, code, name) VALUES (@Id, 'DEPT', 'Department');",
            new { Id = dimId });

        await conn.ExecuteAsync(
            "INSERT INTO platform_dimension_sets(dimension_set_id) VALUES (@Id);",
            new { Id = setId });

        await conn.ExecuteAsync(
            "INSERT INTO platform_dimension_set_items(dimension_set_id, dimension_id, value_id) VALUES (@SetId, @DimId, @ValueId);",
            new { SetId = setId, DimId = dimId, ValueId = Guid.CreateVersion7() });

        var act = async () =>
        {
            await conn.ExecuteAsync(
                "INSERT INTO platform_dimension_set_items(dimension_set_id, dimension_id, value_id) VALUES (@SetId, @DimId, @ValueId);",
                new { SetId = setId, DimId = dimId, ValueId = Guid.CreateVersion7() });
        };

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23505");
        // We explicitly name this PK in migrations (instead of relying on Postgres default *_pkey naming).
        ex.Which.ConstraintName.Should().Be("pk_platform_dimset_items");
    }
}

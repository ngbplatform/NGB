using Dapper;
using FluentAssertions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.DatabaseGuards;

/// <summary>
/// P0: platform_dimensions must be protected by hard DB constraints.
/// - code/name cannot be blank
/// - code/name must be trimmed (no leading/trailing whitespace)
/// - code uniqueness is enforced case-insensitively across ALL dimensions (including deleted)
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DatabaseGuards_PlatformDimensions_DbConstraints_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CheckConstraints_Forbid_EmptyAndUntrimmedCodeAndName()
    {
        await Fixture.ResetDatabaseAsync();
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var id1 = Guid.CreateVersion7();

        var actCodeEmpty = async () =>
        {
            await conn.ExecuteAsync(
                "INSERT INTO platform_dimensions(dimension_id, code, name) VALUES (@Id, '', 'Department');",
                new { Id = id1 });
        };

        var exCodeEmpty = await actCodeEmpty.Should().ThrowAsync<PostgresException>();
        exCodeEmpty.Which.SqlState.Should().Be("23514");
        exCodeEmpty.Which.ConstraintName.Should().Be("ck_platform_dimensions_code_nonempty");

        var id2 = Guid.CreateVersion7();

        var actNameEmpty = async () =>
        {
            await conn.ExecuteAsync(
                "INSERT INTO platform_dimensions(dimension_id, code, name) VALUES (@Id, 'DEPT', '');",
                new { Id = id2 });
        };

        var exNameEmpty = await actNameEmpty.Should().ThrowAsync<PostgresException>();
        exNameEmpty.Which.SqlState.Should().Be("23514");
        exNameEmpty.Which.ConstraintName.Should().Be("ck_platform_dimensions_name_nonempty");

        var id3 = Guid.CreateVersion7();

        var actCodeNotTrimmed = async () =>
        {
            await conn.ExecuteAsync(
                "INSERT INTO platform_dimensions(dimension_id, code, name) VALUES (@Id, ' DEPT ', 'Department');",
                new { Id = id3 });
        };

        var exCodeNotTrimmed = await actCodeNotTrimmed.Should().ThrowAsync<PostgresException>();
        exCodeNotTrimmed.Which.SqlState.Should().Be("23514");
        exCodeNotTrimmed.Which.ConstraintName.Should().Be("ck_platform_dimensions_code_trimmed");

        var id4 = Guid.CreateVersion7();

        var actNameNotTrimmed = async () =>
        {
            await conn.ExecuteAsync(
                "INSERT INTO platform_dimensions(dimension_id, code, name) VALUES (@Id, 'DEPT', ' Department ');",
                new { Id = id4 });
        };

        var exNameNotTrimmed = await actNameNotTrimmed.Should().ThrowAsync<PostgresException>();
        exNameNotTrimmed.Which.SqlState.Should().Be("23514");
        exNameNotTrimmed.Which.ConstraintName.Should().Be("ck_platform_dimensions_name_trimmed");
    }

    [Fact]
    public async Task UniqueIndex_Forbids_DuplicateCodeNorm_AmongNotDeleted()
    {
        await Fixture.ResetDatabaseAsync();
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var id1 = Guid.CreateVersion7();
        var id2 = Guid.CreateVersion7();

        await conn.ExecuteAsync(
            "INSERT INTO platform_dimensions(dimension_id, code, name, is_deleted) VALUES (@Id, 'DEPT', 'Department', FALSE);",
            new { Id = id1 });

        var act = async () =>
        {
            await conn.ExecuteAsync(
                "INSERT INTO platform_dimensions(dimension_id, code, name, is_deleted) VALUES (@Id, 'dept', 'Department 2', FALSE);",
                new { Id = id2 });
        };

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23505");
        ex.Which.ConstraintName.Should().Be("ux_platform_dimensions_code_norm_not_deleted");
    }

    [Fact]
    public async Task SoftDelete_DoesNotRelease_CodeNorm_ForReuse()
    {
        await Fixture.ResetDatabaseAsync();
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var id1 = Guid.CreateVersion7();
        var id2 = Guid.CreateVersion7();

        await conn.ExecuteAsync(
            "INSERT INTO platform_dimensions(dimension_id, code, name, is_deleted) VALUES (@Id, 'DEPT', 'Department', FALSE);",
            new { Id = id1 });

        await conn.ExecuteAsync(
            "UPDATE platform_dimensions SET is_deleted = TRUE WHERE dimension_id = @Id;",
            new { Id = id1 });

        var act = async () =>
        {
            await conn.ExecuteAsync(
                "INSERT INTO platform_dimensions(dimension_id, code, name, is_deleted) VALUES (@Id, 'dept', 'Department 2', FALSE);",
                new { Id = id2 });
        };

        // code_norm must remain unique even if the original row is soft-deleted.
        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23505");
        ex.Which.ConstraintName.Should().Be("ux_platform_dimensions_code_norm_not_deleted");
    }

    [Fact]
    public async Task HardDelete_IsRestricted_WhenDimensionIsReferencedByDimensionSetItems()
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
                "DELETE FROM platform_dimensions WHERE dimension_id = @Id;",
                new { Id = dimId });
        };

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23503");
        ex.Which.ConstraintName.Should().Be("fk_platform_dimset_items_dimension");
    }
}

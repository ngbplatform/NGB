using Dapper;
using FluentAssertions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.DatabaseGuards;

/// <summary>
/// P1: Database-level hard constraints for platform_dimension_set_items.
/// Ensures PK/FK/check invariants are enforced even if higher-level services are bypassed.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DatabaseConstraints_PlatformDimensionSetItems_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static async Task<Guid> InsertDimensionAsync(NpgsqlConnection conn, Guid? explicitId = null)
    {
        var id = explicitId ?? Guid.CreateVersion7();

        // platform_dimensions is not append-only; for tests we prefer idempotent inserts.
        const string sql = """
                           INSERT INTO platform_dimensions(
                               dimension_id,
                               code,
                               name,
                               is_active,
                               is_deleted
                           )
                           VALUES (
                               @Id,
                               @Code,
                               @Name,
                               TRUE,
                               FALSE
                           )
                           ON CONFLICT (dimension_id) DO NOTHING;
                           """;

        await conn.ExecuteAsync(sql, new
        {
            Id = id,
            Code = $"IT-DIM-{(explicitId ?? id):N}".Substring(0, 16),
            Name = "IT dimension"
        });

        return id;
    }

    private static async Task<Guid> InsertDimensionSetAsync(NpgsqlConnection conn)
    {
        var id = Guid.CreateVersion7();

        const string sql = """
                           INSERT INTO platform_dimension_sets(dimension_set_id)
                           VALUES (@Id);
                           """;

        await conn.ExecuteAsync(sql, new { Id = id });
        return id;
    }

    [Fact]
    public async Task DimensionSetId_CannotBeEmpty_CheckIsEnforced_ByDb()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var dimId = await InsertDimensionAsync(conn);

        Func<Task> act = () => conn.ExecuteAsync(
            """
            INSERT INTO platform_dimension_set_items(dimension_set_id, dimension_id, value_id)
            VALUES (@SetId, @DimensionId, @ValueId);
            """,
            new
            {
                SetId = Guid.Empty, // reserved empty set is not allowed in items
                DimensionId = dimId,
                ValueId = Guid.CreateVersion7()
            });

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        ex.Which.ConstraintName.Should().Be("ck_platform_dimset_items_set_nonempty");
    }

    [Fact]
    public async Task DimensionId_CannotBeEmpty_CheckIsEnforced_ByDb()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        // Insert a real dimension row with Guid.Empty so the FK can pass and we assert the CHECK specifically.
        _ = await InsertDimensionAsync(conn, Guid.Empty);
        var setId = await InsertDimensionSetAsync(conn);

        Func<Task> act = () => conn.ExecuteAsync(
            """
            INSERT INTO platform_dimension_set_items(dimension_set_id, dimension_id, value_id)
            VALUES (@SetId, @DimensionId, @ValueId);
            """,
            new { SetId = setId, DimensionId = Guid.Empty, ValueId = Guid.CreateVersion7() });

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        ex.Which.ConstraintName.Should().Be("ck_platform_dimset_items_dimension_nonempty");
    }

    [Fact]
    public async Task ValueId_CannotBeEmpty_CheckIsEnforced_ByDb()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var dimId = await InsertDimensionAsync(conn);
        var setId = await InsertDimensionSetAsync(conn);

        Func<Task> act = () => conn.ExecuteAsync(
            """
            INSERT INTO platform_dimension_set_items(dimension_set_id, dimension_id, value_id)
            VALUES (@SetId, @DimensionId, @ValueId);
            """,
            new { SetId = setId, DimensionId = dimId, ValueId = Guid.Empty });

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        ex.Which.ConstraintName.Should().Be("ck_platform_dimset_items_value_nonempty");
    }

    [Fact]
    public async Task ForeignKey_ToDimensionSet_IsEnforced_ByDb()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var dimId = await InsertDimensionAsync(conn);

        Func<Task> act = () => conn.ExecuteAsync(
            """
            INSERT INTO platform_dimension_set_items(dimension_set_id, dimension_id, value_id)
            VALUES (@SetId, @DimensionId, @ValueId);
            """,
            new { SetId = Guid.CreateVersion7(), DimensionId = dimId, ValueId = Guid.CreateVersion7() });

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
        ex.Which.ConstraintName.Should().Be("fk_platform_dimset_items_set");
    }

    [Fact]
    public async Task ForeignKey_ToDimension_IsEnforced_ByDb()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var setId = await InsertDimensionSetAsync(conn);

        Func<Task> act = () => conn.ExecuteAsync(
            """
            INSERT INTO platform_dimension_set_items(dimension_set_id, dimension_id, value_id)
            VALUES (@SetId, @DimensionId, @ValueId);
            """,
            new { SetId = setId, DimensionId = Guid.CreateVersion7(), ValueId = Guid.CreateVersion7() });

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
        ex.Which.ConstraintName.Should().Be("fk_platform_dimset_items_dimension");
    }

    [Fact]
    public async Task PrimaryKey_EnforcesOneValuePerDimensionPerSet()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var dimId = await InsertDimensionAsync(conn);
        var setId = await InsertDimensionSetAsync(conn);

        const string insertSql = """
                                 INSERT INTO platform_dimension_set_items(dimension_set_id, dimension_id, value_id)
                                 VALUES (@SetId, @DimensionId, @ValueId);
                                 """;

        await conn.ExecuteAsync(insertSql, new { SetId = setId, DimensionId = dimId, ValueId = Guid.CreateVersion7() });

        Func<Task> act = () => conn.ExecuteAsync(insertSql, new { SetId = setId, DimensionId = dimId, ValueId = Guid.CreateVersion7() });

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        // Default name for unnamed PK in PostgreSQL.
        // We explicitly name this PK in migrations (instead of relying on Postgres default *_pkey naming).
        ex.Which.ConstraintName.Should().Be("pk_platform_dimset_items");
    }

    [Fact]
    public async Task DeletingDimensionReferencedByItems_IsRestricted_ByDb()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var dimId = await InsertDimensionAsync(conn);
        var setId = await InsertDimensionSetAsync(conn);

        await conn.ExecuteAsync(
            """
            INSERT INTO platform_dimension_set_items(dimension_set_id, dimension_id, value_id)
            VALUES (@SetId, @DimensionId, @ValueId);
            """,
            new { SetId = setId, DimensionId = dimId, ValueId = Guid.CreateVersion7() });

        Func<Task> act = () => conn.ExecuteAsync(
            """
            DELETE FROM platform_dimensions
            WHERE dimension_id = @Id;
            """,
            new { Id = dimId });

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
        ex.Which.ConstraintName.Should().Be("fk_platform_dimset_items_dimension");
    }

    [Fact]
    public async Task ValidRows_AreAccepted_ByDb()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var dimId = await InsertDimensionAsync(conn);
        var setId = await InsertDimensionSetAsync(conn);
        var valueId = Guid.CreateVersion7();

        await conn.ExecuteAsync(
            """
            INSERT INTO platform_dimension_set_items(dimension_set_id, dimension_id, value_id)
            VALUES (@SetId, @DimensionId, @ValueId);
            """,
            new { SetId = setId, DimensionId = dimId, ValueId = valueId });

        var count = await conn.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM platform_dimension_set_items
            WHERE dimension_set_id = @SetId AND dimension_id = @DimensionId AND value_id = @ValueId;
            """,
            new { SetId = setId, DimensionId = dimId, ValueId = valueId });

        count.Should().Be(1);
    }
}

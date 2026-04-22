using Dapper;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

[Collection(PostgresCollection.Name)]
public sealed class Migrations_UpgradeDrift_PlatformDimensionSetItems_CheckConstraints_P4_2_Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ApplyPlatformMigrations_RecreatesDroppedCheckConstraints_OnPlatformDimensionSetItems()
    {
        // IMPORTANT:
        // Most migrations are CREATE TABLE IF NOT EXISTS and won't re-add dropped CHECK constraints.
        // We have a drift-repair migration (PlatformDimensionSetItemsConstraintsDriftRepairMigration)
        // that must be able to restore these critical constraints.

        await Fixture.ResetDatabaseAsync();

        // Arrange: drop the 3 critical CHECK constraints.
        await ExecuteAsync(
            Fixture.ConnectionString,
            """
            ALTER TABLE public.platform_dimension_set_items DROP CONSTRAINT IF EXISTS ck_platform_dimset_items_set_nonempty;
            ALTER TABLE public.platform_dimension_set_items DROP CONSTRAINT IF EXISTS ck_platform_dimset_items_dimension_nonempty;
            ALTER TABLE public.platform_dimension_set_items DROP CONSTRAINT IF EXISTS ck_platform_dimset_items_value_nonempty;
            """);

        // Sanity: constraints are really gone.
        (await ConstraintExistsAsync(Fixture.ConnectionString, "platform_dimension_set_items", "ck_platform_dimset_items_set_nonempty"))
            .Should().BeFalse();
        (await ConstraintExistsAsync(Fixture.ConnectionString, "platform_dimension_set_items", "ck_platform_dimset_items_dimension_nonempty"))
            .Should().BeFalse();
        (await ConstraintExistsAsync(Fixture.ConnectionString, "platform_dimension_set_items", "ck_platform_dimset_items_value_nonempty"))
            .Should().BeFalse();

        // Sanity: without CHECK constraints, these "invalid" inserts are accepted by the DB.
        await SanityInsertInvalidRows_WithDroppedConstraints_RollbackAsync(Fixture.ConnectionString);

        // Act: re-apply platform migrations (idempotent) to repair drift.
        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        // Assert: constraints are restored.
        (await ConstraintExistsAsync(Fixture.ConnectionString, "platform_dimension_set_items", "ck_platform_dimset_items_set_nonempty"))
            .Should().BeTrue();
        (await ConstraintExistsAsync(Fixture.ConnectionString, "platform_dimension_set_items", "ck_platform_dimset_items_dimension_nonempty"))
            .Should().BeTrue();
        (await ConstraintExistsAsync(Fixture.ConnectionString, "platform_dimension_set_items", "ck_platform_dimset_items_value_nonempty"))
            .Should().BeTrue();

        // Assert: invalid inserts are rejected again with correct constraint names.
        await AssertInvalidInsertsAreRejected_WithRestoredConstraintsAsync(Fixture.ConnectionString);
    }

    private static async Task SanityInsertInvalidRows_WithDroppedConstraints_RollbackAsync(string cs)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        await using var tx = await conn.BeginTransactionAsync(CancellationToken.None);

        var setId = Guid.CreateVersion7();
        var dimId = Guid.CreateVersion7();
        var valueId = Guid.CreateVersion7();

        // Seed FK rows (in txn, will rollback).
        await conn.ExecuteAsync(
            "INSERT INTO platform_dimension_sets(dimension_set_id) VALUES (@Id);",
            new { Id = setId },
            tx);

        await conn.ExecuteAsync(
            "INSERT INTO platform_dimensions(dimension_id, code, name) VALUES (@Id, @Code, @Name);",
            new { Id = dimId, Code = "DIM_OK", Name = "Ok" },
            tx);

        await conn.ExecuteAsync(
            "INSERT INTO platform_dimensions(dimension_id, code, name) VALUES (@Id, @Code, @Name);",
            new { Id = Guid.Empty, Code = "DIM_EMPTY", Name = "Empty" },
            tx);

        // 1) Forbidden normally: empty set id (Guid.Empty is reserved for the empty set header only).
        //    With dropped constraint, it becomes insertable (FK is satisfied because empty-set row exists).
        await conn.ExecuteAsync(
            "INSERT INTO platform_dimension_set_items(dimension_set_id, dimension_id, value_id) VALUES (@SetId, @DimId, @ValueId);",
            new { SetId = Guid.Empty, DimId = dimId, ValueId = valueId },
            tx);

        // 2) Forbidden normally: empty dimension id.
        //    With dropped constraint, it becomes insertable if we also seed a dimension row with Guid.Empty.
        await conn.ExecuteAsync(
            "INSERT INTO platform_dimension_set_items(dimension_set_id, dimension_id, value_id) VALUES (@SetId, @DimId, @ValueId);",
            new { SetId = setId, DimId = Guid.Empty, ValueId = Guid.CreateVersion7() },
            tx);

        // 3) Forbidden normally: empty value id.
        //    With dropped constraint, it becomes insertable (no FK on value_id).
        await conn.ExecuteAsync(
            "INSERT INTO platform_dimension_set_items(dimension_set_id, dimension_id, value_id) VALUES (@SetId, @DimId, @ValueId);",
            new { SetId = setId, DimId = dimId, ValueId = Guid.Empty },
            tx);

        await tx.RollbackAsync(CancellationToken.None);
    }

    private static async Task AssertInvalidInsertsAreRejected_WithRestoredConstraintsAsync(string cs)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        var setId = Guid.CreateVersion7();
        var dimId = Guid.CreateVersion7();

        await conn.ExecuteAsync(
            "INSERT INTO platform_dimension_sets(dimension_set_id) VALUES (@Id);",
            new { Id = setId });

        await conn.ExecuteAsync(
            "INSERT INTO platform_dimensions(dimension_id, code, name) VALUES (@Id, @Code, @Name);",
            new { Id = dimId, Code = "DIM_OK_2", Name = "Ok" });

        await conn.ExecuteAsync(
            "INSERT INTO platform_dimensions(dimension_id, code, name) VALUES (@Id, @Code, @Name);",
            new { Id = Guid.Empty, Code = "DIM_EMPTY_2", Name = "Empty" });

        // 1) empty set id
        var actSetEmpty = async () =>
        {
            await conn.ExecuteAsync(
                "INSERT INTO platform_dimension_set_items(dimension_set_id, dimension_id, value_id) VALUES (@SetId, @DimId, @ValueId);",
                new { SetId = Guid.Empty, DimId = dimId, ValueId = Guid.CreateVersion7() });
        };

        var exSetEmpty = await actSetEmpty.Should().ThrowAsync<PostgresException>();
        exSetEmpty.Which.SqlState.Should().Be("23514");
        exSetEmpty.Which.ConstraintName.Should().Be("ck_platform_dimset_items_set_nonempty");

        // 2) empty dimension id
        var actDimEmpty = async () =>
        {
            await conn.ExecuteAsync(
                "INSERT INTO platform_dimension_set_items(dimension_set_id, dimension_id, value_id) VALUES (@SetId, @DimId, @ValueId);",
                new { SetId = setId, DimId = Guid.Empty, ValueId = Guid.CreateVersion7() });
        };

        var exDimEmpty = await actDimEmpty.Should().ThrowAsync<PostgresException>();
        exDimEmpty.Which.SqlState.Should().Be("23514");
        exDimEmpty.Which.ConstraintName.Should().Be("ck_platform_dimset_items_dimension_nonempty");

        // 3) empty value id
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

    private static async Task ExecuteAsync(string cs, string sql)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);
        await conn.ExecuteAsync(sql);
    }

    private static async Task<bool> ConstraintExistsAsync(string cs, string tableName, string constraintName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        var exists = await conn.ExecuteScalarAsync<int>(
            """
            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM pg_constraint c
                WHERE c.conname = @constraint
                  AND c.conrelid = to_regclass('public.' || @table)
            ) THEN 1 ELSE 0 END;
            """,
            new { constraint = constraintName, table = tableName });

        return exists == 1;
    }
}

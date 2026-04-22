using Dapper;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

/// <summary>
/// P1: Drift-repair for platform_dimensions generated code_norm column and trim constraints.
///
/// The base table is created once, and later migrations (PlatformDimensionsCodeNormMigration + Indexes)
/// add important computed column / constraints / indexes. Those must be re-creatable if accidentally dropped.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class Migrations_UpgradeDrift_PlatformDimensions_CodeNormAndTrimConstraints_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ApplyPlatformMigrations_Recreates_CodeNormColumn_TrimConstraints_AndIndexes_WhenDropped()
    {
        await Fixture.ResetDatabaseAsync();

        // Arrange: simulate drift.
        await ExecuteAsync(
            Fixture.ConnectionString,
            """
            ALTER TABLE public.platform_dimensions DROP CONSTRAINT IF EXISTS ck_platform_dimensions_code_trimmed;
            ALTER TABLE public.platform_dimensions DROP CONSTRAINT IF EXISTS ck_platform_dimensions_name_trimmed;

            -- Dropping the generated column implicitly drops dependent indexes.
            ALTER TABLE public.platform_dimensions DROP COLUMN IF EXISTS code_norm;

            DROP INDEX IF EXISTS public.ux_platform_dimensions_code_norm_not_deleted;
            DROP INDEX IF EXISTS public.ix_platform_dimensions_is_active;
            """);

        // Sanity: drift is real.
        (await ColumnExistsAsync(Fixture.ConnectionString, "platform_dimensions", "code_norm"))
            .Should().BeFalse();

        (await ConstraintExistsAsync(Fixture.ConnectionString, "platform_dimensions", "ck_platform_dimensions_code_trimmed"))
            .Should().BeFalse();
        (await ConstraintExistsAsync(Fixture.ConnectionString, "platform_dimensions", "ck_platform_dimensions_name_trimmed"))
            .Should().BeFalse();

        (await IndexExistsAsync(Fixture.ConnectionString, "ux_platform_dimensions_code_norm_not_deleted"))
            .Should().BeFalse();
        (await IndexExistsAsync(Fixture.ConnectionString, "ix_platform_dimensions_is_active"))
            .Should().BeFalse();

        // Act: re-apply platform migrations.
        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        // Assert: column + constraints + indexes are restored.
        (await ColumnExistsAsync(Fixture.ConnectionString, "platform_dimensions", "code_norm"))
            .Should().BeTrue();

        (await ColumnIsGeneratedAlwaysAsync(Fixture.ConnectionString, "platform_dimensions", "code_norm"))
            .Should().BeTrue();

        (await ConstraintExistsAsync(Fixture.ConnectionString, "platform_dimensions", "ck_platform_dimensions_code_trimmed"))
            .Should().BeTrue();
        (await ConstraintExistsAsync(Fixture.ConnectionString, "platform_dimensions", "ck_platform_dimensions_name_trimmed"))
            .Should().BeTrue();

        (await IndexExistsAsync(Fixture.ConnectionString, "ux_platform_dimensions_code_norm_not_deleted"))
            .Should().BeTrue();
        (await IndexExistsAsync(Fixture.ConnectionString, "ix_platform_dimensions_is_active"))
            .Should().BeTrue();

        // Assert: code_norm is actually computed by the DB.
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync(CancellationToken.None);

        var id = Guid.CreateVersion7();
        await conn.ExecuteAsync(
            "INSERT INTO platform_dimensions(dimension_id, code, name, is_deleted) VALUES (@Id, @Code, @Name, FALSE);",
            new { Id = id, Code = "DePt", Name = "Department" });

        var norm = await conn.ExecuteScalarAsync<string>(
            "SELECT code_norm FROM platform_dimensions WHERE dimension_id = @Id;",
            new { Id = id });

        norm.Should().Be("dept");
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

    private static async Task<bool> ColumnExistsAsync(string cs, string tableName, string columnName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        var exists = await conn.ExecuteScalarAsync<int>(
            """
            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = @table
                  AND column_name = @column
            ) THEN 1 ELSE 0 END;
            """,
            new { table = tableName, column = columnName });

        return exists == 1;
    }

    private static async Task<bool> ColumnIsGeneratedAlwaysAsync(string cs, string tableName, string columnName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        // information_schema.columns.is_generated = 'ALWAYS' for GENERATED ALWAYS AS (...) STORED columns.
        var isGen = await conn.ExecuteScalarAsync<int>(
            """
            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = @table
                  AND column_name = @column
                  AND is_generated = 'ALWAYS'
            ) THEN 1 ELSE 0 END;
            """,
            new { table = tableName, column = columnName });

        return isGen == 1;
    }

    private static async Task<bool> IndexExistsAsync(string cs, string indexName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        var exists = await conn.ExecuteScalarAsync<int>(
            "SELECT CASE WHEN to_regclass('public.' || @idx) IS NOT NULL THEN 1 ELSE 0 END;",
            new { idx = indexName });

        return exists == 1;
    }
}

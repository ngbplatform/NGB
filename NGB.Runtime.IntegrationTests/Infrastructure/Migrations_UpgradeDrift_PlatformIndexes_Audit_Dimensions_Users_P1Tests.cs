using Dapper;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

/// <summary>
/// P1 drift-repair: our migration runner is "CREATE IF NOT EXISTS" style.
/// Dropping indexes is recoverable by re-applying platform migrations.
///
/// This test covers platform-level indexes that are essential for:
/// - AuditLog paging and query performance
/// - Dimension sets filtering and lookups
/// - Users projection uniqueness
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class Migrations_UpgradeDrift_PlatformIndexes_Audit_Dimensions_Users_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ApplyPlatformMigrations_RecreatesDroppedPlatformIndexes()
    {
        // Platform Users
        await DropIndexIfExistsAsync(Fixture.ConnectionString, "ux_platform_users_auth_subject");
        await DropIndexIfExistsAsync(Fixture.ConnectionString, "ix_platform_users_email");

        // Platform Dimensions
        await DropIndexIfExistsAsync(Fixture.ConnectionString, "ux_platform_dimensions_code_norm_not_deleted");
        await DropIndexIfExistsAsync(Fixture.ConnectionString, "ix_platform_dimensions_is_active");

        // Platform Dimension Set Items
        await DropIndexIfExistsAsync(Fixture.ConnectionString, "ix_platform_dimset_items_dimension_value_set");

        // Platform Audit indexes
        await DropIndexIfExistsAsync(Fixture.ConnectionString, "ix_platform_audit_events_occurred_at");
        await DropIndexIfExistsAsync(Fixture.ConnectionString, "ix_platform_audit_events_entity");
        await DropIndexIfExistsAsync(Fixture.ConnectionString, "ix_platform_audit_events_action");
        await DropIndexIfExistsAsync(Fixture.ConnectionString, "ix_platform_audit_events_actor");
        await DropIndexIfExistsAsync(Fixture.ConnectionString, "ix_platform_audit_event_changes_event");
        await DropIndexIfExistsAsync(Fixture.ConnectionString, "ux_platform_audit_event_changes_event_ordinal");

        // Platform Audit paging indexes (critical for cursor pagination)
        await DropIndexIfExistsAsync(Fixture.ConnectionString, "ix_platform_audit_events_occurred_at_id_desc");
        await DropIndexIfExistsAsync(Fixture.ConnectionString, "ix_platform_audit_events_entity_occurred_at_id_desc");

        // Sanity: dropped.
        (await IndexExistsAsync(Fixture.ConnectionString, "ux_platform_users_auth_subject")).Should().BeFalse();
        (await IndexExistsAsync(Fixture.ConnectionString, "ix_platform_users_email")).Should().BeFalse();

        (await IndexExistsAsync(Fixture.ConnectionString, "ux_platform_dimensions_code_norm_not_deleted")).Should().BeFalse();
        (await IndexExistsAsync(Fixture.ConnectionString, "ix_platform_dimensions_is_active")).Should().BeFalse();

        (await IndexExistsAsync(Fixture.ConnectionString, "ix_platform_dimset_items_dimension_value_set")).Should().BeFalse();

        (await IndexExistsAsync(Fixture.ConnectionString, "ix_platform_audit_events_occurred_at")).Should().BeFalse();
        (await IndexExistsAsync(Fixture.ConnectionString, "ix_platform_audit_events_entity")).Should().BeFalse();
        (await IndexExistsAsync(Fixture.ConnectionString, "ix_platform_audit_events_action")).Should().BeFalse();
        (await IndexExistsAsync(Fixture.ConnectionString, "ix_platform_audit_events_actor")).Should().BeFalse();
        (await IndexExistsAsync(Fixture.ConnectionString, "ix_platform_audit_event_changes_event")).Should().BeFalse();
        (await IndexExistsAsync(Fixture.ConnectionString, "ux_platform_audit_event_changes_event_ordinal")).Should().BeFalse();

        (await IndexExistsAsync(Fixture.ConnectionString, "ix_platform_audit_events_occurred_at_id_desc")).Should().BeFalse();
        (await IndexExistsAsync(Fixture.ConnectionString, "ix_platform_audit_events_entity_occurred_at_id_desc")).Should().BeFalse();

        // Act
        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        // Assert: recreated.
        (await IndexExistsAsync(Fixture.ConnectionString, "ux_platform_users_auth_subject")).Should().BeTrue();
        (await IndexExistsAsync(Fixture.ConnectionString, "ix_platform_users_email")).Should().BeTrue();

        (await IndexExistsAsync(Fixture.ConnectionString, "ux_platform_dimensions_code_norm_not_deleted")).Should().BeTrue();
        (await IndexExistsAsync(Fixture.ConnectionString, "ix_platform_dimensions_is_active")).Should().BeTrue();

        (await IndexExistsAsync(Fixture.ConnectionString, "ix_platform_dimset_items_dimension_value_set")).Should().BeTrue();

        (await IndexExistsAsync(Fixture.ConnectionString, "ix_platform_audit_events_occurred_at")).Should().BeTrue();
        (await IndexExistsAsync(Fixture.ConnectionString, "ix_platform_audit_events_entity")).Should().BeTrue();
        (await IndexExistsAsync(Fixture.ConnectionString, "ix_platform_audit_events_action")).Should().BeTrue();
        (await IndexExistsAsync(Fixture.ConnectionString, "ix_platform_audit_events_actor")).Should().BeTrue();
        (await IndexExistsAsync(Fixture.ConnectionString, "ix_platform_audit_event_changes_event")).Should().BeTrue();
        (await IndexExistsAsync(Fixture.ConnectionString, "ux_platform_audit_event_changes_event_ordinal")).Should().BeTrue();

        (await IndexExistsAsync(Fixture.ConnectionString, "ix_platform_audit_events_occurred_at_id_desc")).Should().BeTrue();
        (await IndexExistsAsync(Fixture.ConnectionString, "ix_platform_audit_events_entity_occurred_at_id_desc")).Should().BeTrue();
    }

    private static async Task DropIndexIfExistsAsync(string cs, string indexName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);
        await conn.ExecuteAsync($"DROP INDEX IF EXISTS {indexName};");
    }

    private static async Task<bool> IndexExistsAsync(string cs, string indexName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        var exists = await conn.ExecuteScalarAsync<int>(
            """
            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = 'public'
                  AND c.relkind = 'i'
                  AND c.relname = @name
            ) THEN 1 ELSE 0 END;
            """,
            new { name = indexName });

        return exists == 1;
    }
}

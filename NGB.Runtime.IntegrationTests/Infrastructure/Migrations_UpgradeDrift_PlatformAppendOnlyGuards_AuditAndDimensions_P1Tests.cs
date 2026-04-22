using Dapper;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

/// <summary>
/// P1: drift-repair contract.
///
/// Our migration style is mostly CREATE IF NOT EXISTS.
/// For critical DB guards that must always exist (append-only), migrations must actively repair drift.
///
/// This test proves that if the append-only triggers (and even the shared guard function) are dropped,
/// re-applying platform migrations restores them.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class Migrations_UpgradeDrift_PlatformAppendOnlyGuards_AuditAndDimensions_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ApplyPlatformMigrations_RecreatesAppendOnlyGuards_ForAuditAndDimensions_IfDropped()
    {
        // Arrange: drop triggers + drift the shared guard function.
        await ExecuteAsync(
            Fixture.ConnectionString,
            """
            DROP TRIGGER IF EXISTS trg_platform_audit_events_append_only ON public.platform_audit_events;
            DROP TRIGGER IF EXISTS trg_platform_audit_event_changes_append_only ON public.platform_audit_event_changes;

            DROP TRIGGER IF EXISTS trg_platform_dimension_sets_append_only ON public.platform_dimension_sets;
            DROP TRIGGER IF EXISTS trg_platform_dimension_set_items_append_only ON public.platform_dimension_set_items;

            -- We cannot DROP the function in a full platform schema (many objects depend on it).
            -- Instead, we replace its body to simulate drift and ensure migrations repair it.
            CREATE OR REPLACE FUNCTION public.ngb_forbid_mutation_of_append_only_table()
            RETURNS trigger AS $$
            BEGIN
                RAISE EXCEPTION 'DRIFTED append-only guard: %', TG_TABLE_NAME
                    USING ERRCODE = '55000';
            END;
            $$ LANGUAGE plpgsql;
            """);

        // Sanity: dropped.
        (await TriggerExistsAsync(Fixture.ConnectionString, "trg_platform_audit_events_append_only"))
            .Should().BeFalse();
        (await TriggerExistsAsync(Fixture.ConnectionString, "trg_platform_audit_event_changes_append_only"))
            .Should().BeFalse();
        (await TriggerExistsAsync(Fixture.ConnectionString, "trg_platform_dimension_sets_append_only"))
            .Should().BeFalse();
        (await TriggerExistsAsync(Fixture.ConnectionString, "trg_platform_dimension_set_items_append_only"))
            .Should().BeFalse();

        (await FunctionExistsAsync(Fixture.ConnectionString, "ngb_forbid_mutation_of_append_only_table"))
            .Should().BeTrue();
        (await FunctionDefinitionContainsAsync(
                Fixture.ConnectionString,
                "ngb_forbid_mutation_of_append_only_table",
                "DRIFTED append-only guard"))
            .Should().BeTrue("we drifted the function body, not dropped it");

        // Act
        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        // Assert: restored.
        (await FunctionExistsAsync(Fixture.ConnectionString, "ngb_forbid_mutation_of_append_only_table"))
            .Should().BeTrue();
        (await FunctionDefinitionContainsAsync(
                Fixture.ConnectionString,
                "ngb_forbid_mutation_of_append_only_table",
                "Append-only table cannot be mutated"))
            .Should().BeTrue("platform migrations must repair the canonical function body");

        (await TriggerExistsAsync(Fixture.ConnectionString, "trg_platform_audit_events_append_only"))
            .Should().BeTrue();
        (await TriggerExistsAsync(Fixture.ConnectionString, "trg_platform_audit_event_changes_append_only"))
            .Should().BeTrue();
        (await TriggerExistsAsync(Fixture.ConnectionString, "trg_platform_dimension_sets_append_only"))
            .Should().BeTrue();
        (await TriggerExistsAsync(Fixture.ConnectionString, "trg_platform_dimension_set_items_append_only"))
            .Should().BeTrue();
    }

    private static async Task ExecuteAsync(string cs, string sql)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);
        await conn.ExecuteAsync(sql);
    }

    private static async Task<bool> TriggerExistsAsync(string cs, string triggerName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        var exists = await conn.ExecuteScalarAsync<int>(
            """
            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM pg_trigger t
                WHERE t.tgname = @name
                  AND NOT t.tgisinternal
            ) THEN 1 ELSE 0 END;
            """,
            new { name = triggerName });

        return exists == 1;
    }

    private static async Task<bool> FunctionExistsAsync(string cs, string functionName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        var exists = await conn.ExecuteScalarAsync<int>(
            """
            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM pg_proc p
                JOIN pg_namespace n ON n.oid = p.pronamespace
                WHERE n.nspname = 'public'
                  AND p.proname = @name
            ) THEN 1 ELSE 0 END;
            """,
            new { name = functionName });

        return exists == 1;
    }

    private static async Task<bool> FunctionDefinitionContainsAsync(string cs, string functionName, string marker)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        // pg_get_functiondef needs the exact signature; our guard is parameterless.
        var def = await conn.ExecuteScalarAsync<string?>(
            """
            SELECT pg_get_functiondef(('public.' || @name || '()')::regprocedure);
            """,
            new { name = functionName });

        return (def ?? throw new InvalidOperationException($"Function definition for '{functionName}' was not found."))
            .Contains(marker, StringComparison.Ordinal);
    }
}

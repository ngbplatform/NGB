using FluentAssertions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

/// <summary>
/// P0: rule-by-rule drift-repair contracts for platform append-only guards.
///
/// Append-only guards are production-critical defense-in-depth.
/// If triggers (or the shared guard function) are dropped/drifted,
/// re-applying platform migrations must restore them.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class Migrations_UpgradeDrift_PlatformAppendOnlyGuards_RuleByRule_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ApplyPlatformMigrations_Repairs_AppendOnlyGuardFunction_WhenDrifted()
    {
        // Arrange: drift the function body.
        await ExecuteAsync(
            Fixture.ConnectionString,
            """
            CREATE OR REPLACE FUNCTION public.ngb_forbid_mutation_of_append_only_table()
            RETURNS trigger AS $$
            BEGIN
                RAISE EXCEPTION 'DRIFTED append-only guard: %', TG_TABLE_NAME
                    USING ERRCODE = '55000';
            END;
            $$ LANGUAGE plpgsql;
            """);

        (await FunctionDefinitionContainsAsync(
                Fixture.ConnectionString,
                "ngb_forbid_mutation_of_append_only_table",
                "DRIFTED append-only guard"))
            .Should().BeTrue();

        // Act
        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        // Assert
        (await FunctionDefinitionContainsAsync(
                Fixture.ConnectionString,
                "ngb_forbid_mutation_of_append_only_table",
                "Append-only table cannot be mutated"))
            .Should().BeTrue();
    }

    [Fact]
    public async Task ApplyPlatformMigrations_Recreates_AuditEvents_AppendOnlyTrigger_WhenDropped()
    {
        const string trigger = "trg_platform_audit_events_append_only";

        await ExecuteAsync(
            Fixture.ConnectionString,
            "DROP TRIGGER IF EXISTS trg_platform_audit_events_append_only ON public.platform_audit_events;");

        (await TriggerExistsAsync(Fixture.ConnectionString, trigger)).Should().BeFalse();

        var eventId = Guid.CreateVersion7();
        await InsertAuditEventAsync(Fixture.ConnectionString, eventId);

        // Sanity: without trigger, UPDATE is allowed.
        await ExecuteAsync(
            Fixture.ConnectionString,
            "UPDATE public.platform_audit_events SET action_code = 'it.update_ok' WHERE audit_event_id = @id;",
            new NpgsqlParameter("id", eventId));

        // Act: drift-repair.
        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        // Assert: trigger is back, UPDATE/DELETE are forbidden.
        (await TriggerExistsAsync(Fixture.ConnectionString, trigger)).Should().BeTrue();

        var actUpdate = () => ExecuteAsync(
            Fixture.ConnectionString,
            "UPDATE public.platform_audit_events SET action_code = 'it.update_fail' WHERE audit_event_id = @id;",
            new NpgsqlParameter("id", eventId));

        var exUpdate = await actUpdate.Should().ThrowAsync<PostgresException>();
        exUpdate.Which.SqlState.Should().Be("55000");

        var actDelete = () => ExecuteAsync(
            Fixture.ConnectionString,
            "DELETE FROM public.platform_audit_events WHERE audit_event_id = @id;",
            new NpgsqlParameter("id", eventId));

        var exDelete = await actDelete.Should().ThrowAsync<PostgresException>();
        exDelete.Which.SqlState.Should().Be("55000");
    }

    [Fact]
    public async Task ApplyPlatformMigrations_Recreates_AuditEventChanges_AppendOnlyTrigger_WhenDropped()
    {
        const string trigger = "trg_platform_audit_event_changes_append_only";

        await ExecuteAsync(
            Fixture.ConnectionString,
            "DROP TRIGGER IF EXISTS trg_platform_audit_event_changes_append_only ON public.platform_audit_event_changes;");

        (await TriggerExistsAsync(Fixture.ConnectionString, trigger)).Should().BeFalse();

        var eventId = Guid.CreateVersion7();
        var changeId = Guid.CreateVersion7();
        await InsertAuditEventAsync(Fixture.ConnectionString, eventId);
        await InsertAuditChangeAsync(Fixture.ConnectionString, changeId, eventId);

        // Sanity: without trigger, UPDATE is allowed.
        await ExecuteAsync(
            Fixture.ConnectionString,
            "UPDATE public.platform_audit_event_changes SET field_path = 'x.y.z' WHERE audit_change_id = @id;",
            new NpgsqlParameter("id", changeId));

        // Act: drift-repair.
        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        // Assert: trigger is back, UPDATE/DELETE are forbidden.
        (await TriggerExistsAsync(Fixture.ConnectionString, trigger)).Should().BeTrue();

        var actUpdate = () => ExecuteAsync(
            Fixture.ConnectionString,
            "UPDATE public.platform_audit_event_changes SET field_path = 'will_fail' WHERE audit_change_id = @id;",
            new NpgsqlParameter("id", changeId));

        var exUpdate = await actUpdate.Should().ThrowAsync<PostgresException>();
        exUpdate.Which.SqlState.Should().Be("55000");

        var actDelete = () => ExecuteAsync(
            Fixture.ConnectionString,
            "DELETE FROM public.platform_audit_event_changes WHERE audit_change_id = @id;",
            new NpgsqlParameter("id", changeId));

        var exDelete = await actDelete.Should().ThrowAsync<PostgresException>();
        exDelete.Which.SqlState.Should().Be("55000");
    }

    [Fact]
    public async Task ApplyPlatformMigrations_Recreates_DimensionSets_AppendOnlyTrigger_WhenDropped()
    {
        const string trigger = "trg_platform_dimension_sets_append_only";

        await ExecuteAsync(
            Fixture.ConnectionString,
            "DROP TRIGGER IF EXISTS trg_platform_dimension_sets_append_only ON public.platform_dimension_sets;");

        (await TriggerExistsAsync(Fixture.ConnectionString, trigger)).Should().BeFalse();

        var setId = Guid.CreateVersion7();
        await ExecuteAsync(
            Fixture.ConnectionString,
            "INSERT INTO public.platform_dimension_sets(dimension_set_id) VALUES (@id);",
            new NpgsqlParameter("id", setId));

        // Sanity: without trigger, UPDATE is allowed.
        await ExecuteAsync(
            Fixture.ConnectionString,
            "UPDATE public.platform_dimension_sets SET created_at_utc = created_at_utc + interval '1 second' WHERE dimension_set_id = @id;",
            new NpgsqlParameter("id", setId));

        // Act: drift-repair.
        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        // Assert: trigger is back, UPDATE/DELETE are forbidden.
        (await TriggerExistsAsync(Fixture.ConnectionString, trigger)).Should().BeTrue();

        var actUpdate = () => ExecuteAsync(
            Fixture.ConnectionString,
            "UPDATE public.platform_dimension_sets SET created_at_utc = created_at_utc + interval '1 second' WHERE dimension_set_id = @id;",
            new NpgsqlParameter("id", setId));

        var exUpdate = await actUpdate.Should().ThrowAsync<PostgresException>();
        exUpdate.Which.SqlState.Should().Be("55000");

        var actDelete = () => ExecuteAsync(
            Fixture.ConnectionString,
            "DELETE FROM public.platform_dimension_sets WHERE dimension_set_id = @id;",
            new NpgsqlParameter("id", setId));

        var exDelete = await actDelete.Should().ThrowAsync<PostgresException>();
        exDelete.Which.SqlState.Should().Be("55000");
    }

    [Fact]
    public async Task ApplyPlatformMigrations_Recreates_DimensionSetItems_AppendOnlyTrigger_WhenDropped()
    {
        const string trigger = "trg_platform_dimension_set_items_append_only";

        await ExecuteAsync(
            Fixture.ConnectionString,
            "DROP TRIGGER IF EXISTS trg_platform_dimension_set_items_append_only ON public.platform_dimension_set_items;");

        (await TriggerExistsAsync(Fixture.ConnectionString, trigger)).Should().BeFalse();

        var setId = Guid.CreateVersion7();
        var dimensionId = Guid.CreateVersion7();
        var valueId = Guid.CreateVersion7();

        await ExecuteAsync(
            Fixture.ConnectionString,
            "INSERT INTO public.platform_dimension_sets(dimension_set_id) VALUES (@id);",
            new NpgsqlParameter("id", setId));

        await ExecuteAsync(
            Fixture.ConnectionString,
            "INSERT INTO public.platform_dimensions(dimension_id, code, name) VALUES (@id, 'DIM_IT', 'Dim IT');",
            new NpgsqlParameter("id", dimensionId));

        await ExecuteAsync(
            Fixture.ConnectionString,
            "INSERT INTO public.platform_dimension_set_items(dimension_set_id, dimension_id, value_id) VALUES (@set, @dim, @val);",
            new NpgsqlParameter("set", setId),
            new NpgsqlParameter("dim", dimensionId),
            new NpgsqlParameter("val", valueId));

        // Sanity: without trigger, UPDATE is allowed.
        var newValueId = Guid.CreateVersion7();
        await ExecuteAsync(
            Fixture.ConnectionString,
            "UPDATE public.platform_dimension_set_items SET value_id = @val WHERE dimension_set_id = @set AND dimension_id = @dim;",
            new NpgsqlParameter("val", newValueId),
            new NpgsqlParameter("set", setId),
            new NpgsqlParameter("dim", dimensionId));

        // Act: drift-repair.
        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        // Assert: trigger is back, UPDATE/DELETE are forbidden.
        (await TriggerExistsAsync(Fixture.ConnectionString, trigger)).Should().BeTrue();

        var actUpdate = () => ExecuteAsync(
            Fixture.ConnectionString,
            "UPDATE public.platform_dimension_set_items SET value_id = @val WHERE dimension_set_id = @set AND dimension_id = @dim;",
            new NpgsqlParameter("val", Guid.CreateVersion7()),
            new NpgsqlParameter("set", setId),
            new NpgsqlParameter("dim", dimensionId));

        var exUpdate = await actUpdate.Should().ThrowAsync<PostgresException>();
        exUpdate.Which.SqlState.Should().Be("55000");

        var actDelete = () => ExecuteAsync(
            Fixture.ConnectionString,
            "DELETE FROM public.platform_dimension_set_items WHERE dimension_set_id = @set AND dimension_id = @dim;",
            new NpgsqlParameter("set", setId),
            new NpgsqlParameter("dim", dimensionId));

        var exDelete = await actDelete.Should().ThrowAsync<PostgresException>();
        exDelete.Which.SqlState.Should().Be("55000");
    }

    private static async Task InsertAuditEventAsync(string cs, Guid eventId)
    {
        await ExecuteAsync(
            cs,
            """
            INSERT INTO public.platform_audit_events(
                audit_event_id,
                entity_kind,
                entity_id,
                action_code,
                actor_user_id,
                occurred_at_utc,
                correlation_id,
                metadata)
            VALUES (
                @audit_event_id,
                1,
                @entity_id,
                'it.insert',
                NULL,
                NOW(),
                NULL,
                NULL);
            """,
            new NpgsqlParameter("audit_event_id", eventId),
            new NpgsqlParameter("entity_id", Guid.CreateVersion7()));
    }

    private static async Task InsertAuditChangeAsync(string cs, Guid changeId, Guid eventId)
    {
        await ExecuteAsync(
            cs,
            """
            INSERT INTO public.platform_audit_event_changes(
                audit_change_id,
                audit_event_id,
                ordinal,
                field_path,
                old_value_jsonb,
                new_value_jsonb)
            VALUES (
                @audit_change_id,
                @audit_event_id,
                1,
                'a.b',
                NULL,
                NULL);
            """,
            new NpgsqlParameter("audit_change_id", changeId),
            new NpgsqlParameter("audit_event_id", eventId));
    }

    private static async Task ExecuteAsync(string cs, string sql, params NpgsqlParameter[] parameters)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var p in parameters)
            cmd.Parameters.Add(p);

        await cmd.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task<bool> TriggerExistsAsync(string cs, string triggerName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
                          SELECT CASE WHEN EXISTS (
                              SELECT 1
                              FROM pg_trigger t
                              WHERE t.tgname = @name
                                AND NOT t.tgisinternal
                          ) THEN 1 ELSE 0 END;
                          """;
        cmd.Parameters.AddWithValue("name", triggerName);

        var result = (int)(await cmd.ExecuteScalarAsync(CancellationToken.None) ?? 0);
        return result == 1;
    }

    private static async Task<bool> FunctionDefinitionContainsAsync(string cs, string functionName, string marker)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT pg_get_functiondef(('public.' || @name || '()')::regprocedure);";
        cmd.Parameters.AddWithValue("name", functionName);

        var def = (string)(await cmd.ExecuteScalarAsync(CancellationToken.None) ?? string.Empty);
        return def.Contains(marker, StringComparison.Ordinal);
    }
}

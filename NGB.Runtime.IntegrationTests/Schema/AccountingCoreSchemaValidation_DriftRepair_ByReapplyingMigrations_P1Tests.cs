using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.Schema;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Schema;

/// <summary>
/// Verifies that idempotent platform migrations can repair common schema drift
/// (indexes/functions/triggers) and that schema validation succeeds after repair.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AccountingCoreSchemaValidation_DriftRepair_ByReapplyingMigrations_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ValidateAsync_WhenDimensionValueSetIndexDropped_ReapplyingMigrationsRepairs_ThenValidationPasses()
    {
        await Fixture.ResetDatabaseAsync();

        // Arrange: drop a critical index used by dimension filters.
        await DropIndexAsync(Fixture.ConnectionString, "ix_platform_dimset_items_dimension_value_set");
        (await IndexExistsAsync(Fixture.ConnectionString, "ix_platform_dimset_items_dimension_value_set"))
            .Should().BeFalse("the test must simulate index drift");

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IAccountingCoreSchemaValidationService>();

        // Act + Assert: validator must fail before repair.
        var act = () => validator.ValidateAsync(CancellationToken.None);
        await act.Should().ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage("*ix_platform_dimset_items_dimension_value_set*platform_dimension_set_items*");

        // Act: re-apply platform migrations (idempotent) to repair drift.
        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        // Assert: index is restored and validation passes.
        (await IndexExistsAsync(Fixture.ConnectionString, "ix_platform_dimset_items_dimension_value_set"))
            .Should().BeTrue("bootstrapper must be able to restore dropped indexes via CREATE INDEX IF NOT EXISTS");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ValidateAsync_WhenAppendOnlyGuardFunctionDroppedCascade_ReapplyingMigrationsRepairs_ThenValidationPasses()
    {
        await Fixture.ResetDatabaseAsync();

        // Arrange: drop the shared append-only guard function.
        // CASCADE will remove dependent triggers (dimension sets/items + audit).
        await DropFunctionCascadeAsync(Fixture.ConnectionString, "ngb_forbid_mutation_of_append_only_table");

        (await FunctionExistsAsync(Fixture.ConnectionString, "ngb_forbid_mutation_of_append_only_table"))
            .Should().BeFalse("the test must simulate function drift");

        // Sanity: dependent triggers must be dropped.
        (await TriggerExistsAsync(Fixture.ConnectionString, "platform_dimension_sets", "trg_platform_dimension_sets_append_only"))
            .Should().BeFalse();
        (await TriggerExistsAsync(Fixture.ConnectionString, "platform_dimension_set_items", "trg_platform_dimension_set_items_append_only"))
            .Should().BeFalse();
        (await TriggerExistsAsync(Fixture.ConnectionString, "platform_audit_events", "trg_platform_audit_events_append_only"))
            .Should().BeFalse();
        (await TriggerExistsAsync(Fixture.ConnectionString, "platform_audit_event_changes", "trg_platform_audit_event_changes_append_only"))
            .Should().BeFalse();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IAccountingCoreSchemaValidationService>();

        // Act + Assert: validator must fail before repair.
        var act = () => validator.ValidateAsync(CancellationToken.None);
        var ex = await act.Should().ThrowAsync<NgbConfigurationViolationException>();
        ex.Which.Message.Should().Contain("ngb_forbid_mutation_of_append_only_table");

        // Act: re-apply platform migrations (idempotent) to repair drift.
        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        // Assert: function + append-only triggers are restored.
        (await FunctionExistsAsync(Fixture.ConnectionString, "ngb_forbid_mutation_of_append_only_table"))
            .Should().BeTrue("bootstrapper must restore the guard function via CREATE OR REPLACE");

        (await TriggerExistsAsync(Fixture.ConnectionString, "platform_dimension_sets", "trg_platform_dimension_sets_append_only"))
            .Should().BeTrue();

        (await TriggerExistsAsync(Fixture.ConnectionString, "platform_dimension_set_items", "trg_platform_dimension_set_items_append_only"))
            .Should().BeTrue();

        (await TriggerExistsAsync(Fixture.ConnectionString, "platform_audit_events", "trg_platform_audit_events_append_only"))
            .Should().BeTrue();

        (await TriggerExistsAsync(Fixture.ConnectionString, "platform_audit_event_changes", "trg_platform_audit_event_changes_append_only"))
            .Should().BeTrue();

        await act.Should().NotThrowAsync();
    }

    private static async Task DropIndexAsync(string cs, string indexName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP INDEX IF EXISTS public.{indexName};", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task DropFunctionCascadeAsync(string cs, string functionName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP FUNCTION IF EXISTS public.{functionName}() CASCADE;", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<bool> IndexExistsAsync(string cs, string indexName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand("SELECT to_regclass(@qname) IS NOT NULL;", conn);
        cmd.Parameters.AddWithValue("qname", $"public.{indexName}");
        var result = await cmd.ExecuteScalarAsync();
        return result is bool b && b;
    }

    private static async Task<bool> FunctionExistsAsync(string cs, string functionName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM pg_proc p
                JOIN pg_namespace ns ON ns.oid = p.pronamespace
                WHERE ns.nspname = 'public'
                  AND p.proname = @fname
            );
            """,
            conn);

        cmd.Parameters.AddWithValue("fname", functionName);
        var result = await cmd.ExecuteScalarAsync();
        return result is bool b && b;
    }

    private static async Task<bool> TriggerExistsAsync(string cs, string tableName, string triggerName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM pg_trigger t
                JOIN pg_class cl ON cl.oid = t.tgrelid
                JOIN pg_namespace ns ON ns.oid = cl.relnamespace
                WHERE ns.nspname = 'public'
                  AND cl.relname = @table
                  AND t.tgname = @trg
                  AND NOT t.tgisinternal
            );
            """,
            conn);

        cmd.Parameters.AddWithValue("table", tableName);
        cmd.Parameters.AddWithValue("trg", triggerName);
        var result = await cmd.ExecuteScalarAsync();
        return result is bool b && b;
    }
}

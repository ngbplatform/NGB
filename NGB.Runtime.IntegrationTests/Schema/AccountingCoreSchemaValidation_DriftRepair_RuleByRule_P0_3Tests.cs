using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.Schema;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Schema;

/// <summary>
/// Rule-by-rule drift-repair tests for Accounting core schema validation.
///
/// Pattern:
/// 1) break exactly one schema invariant (drop one index/trigger)
/// 2) expect <see cref="IAccountingCoreSchemaValidationService"/> to fail with a specific error
/// 3) re-apply platform migrations (idempotent drift repair)
/// 4) validation passes again
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AccountingCoreSchemaValidation_DriftRepair_RuleByRule_P0_3Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ValidateAsync_WhenAccountDimensionRulesUniqueIndexDropped_FailsThenBootstrapperRepairs()
    {
        await Fixture.ResetDatabaseAsync();

        await DropIndexAsync(Fixture.ConnectionString, "ux_acc_dim_rules_account_ordinal");
        (await IndexExistsAsync(Fixture.ConnectionString, "ux_acc_dim_rules_account_ordinal"))
            .Should().BeFalse("the test must simulate index drift");

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var validator = scope.ServiceProvider.GetRequiredService<IAccountingCoreSchemaValidationService>();
        var act = () => validator.ValidateAsync(CancellationToken.None);

        await act.Should().ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage("*Missing index 'ux_acc_dim_rules_account_ordinal' on table 'accounting_account_dimension_rules'*");

        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        (await IndexExistsAsync(Fixture.ConnectionString, "ux_acc_dim_rules_account_ordinal"))
            .Should().BeTrue("bootstrapper must be able to restore dropped indexes via CREATE INDEX IF NOT EXISTS");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ValidateAsync_WhenAccountDimensionRulesDimensionIndexDropped_FailsThenBootstrapperRepairs()
    {
        await Fixture.ResetDatabaseAsync();

        await DropIndexAsync(Fixture.ConnectionString, "ix_acc_dim_rules_dimension_id");
        (await IndexExistsAsync(Fixture.ConnectionString, "ix_acc_dim_rules_dimension_id"))
            .Should().BeFalse("the test must simulate index drift");

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var validator = scope.ServiceProvider.GetRequiredService<IAccountingCoreSchemaValidationService>();
        var act = () => validator.ValidateAsync(CancellationToken.None);

        await act.Should().ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage("*Missing index 'ix_acc_dim_rules_dimension_id' on table 'accounting_account_dimension_rules'*");

        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        (await IndexExistsAsync(Fixture.ConnectionString, "ix_acc_dim_rules_dimension_id"))
            .Should().BeTrue("bootstrapper must be able to restore dropped indexes via CREATE INDEX IF NOT EXISTS");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ValidateAsync_WhenDimensionSetItemsSetIndexDropped_FailsThenBootstrapperRepairs()
    {
        await Fixture.ResetDatabaseAsync();

        await DropIndexAsync(Fixture.ConnectionString, "ix_platform_dimset_items_set");
        (await IndexExistsAsync(Fixture.ConnectionString, "ix_platform_dimset_items_set"))
            .Should().BeFalse("the test must simulate index drift");

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var validator = scope.ServiceProvider.GetRequiredService<IAccountingCoreSchemaValidationService>();
        var act = () => validator.ValidateAsync(CancellationToken.None);

        await act.Should().ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage("*Missing index 'ix_platform_dimset_items_set' on table 'platform_dimension_set_items'*");

        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        (await IndexExistsAsync(Fixture.ConnectionString, "ix_platform_dimset_items_set"))
            .Should().BeTrue("bootstrapper must be able to restore dropped indexes via CREATE INDEX IF NOT EXISTS");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ValidateAsync_WhenDimensionSetItemsValueIndexDropped_FailsThenBootstrapperRepairs()
    {
        await Fixture.ResetDatabaseAsync();

        await DropIndexAsync(Fixture.ConnectionString, "ix_platform_dimset_items_dimension_value_set");
        (await IndexExistsAsync(Fixture.ConnectionString, "ix_platform_dimset_items_dimension_value_set"))
            .Should().BeFalse("the test must simulate index drift");

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var validator = scope.ServiceProvider.GetRequiredService<IAccountingCoreSchemaValidationService>();
        var act = () => validator.ValidateAsync(CancellationToken.None);

        await act.Should().ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage("*Missing index 'ix_platform_dimset_items_dimension_value_set' on table 'platform_dimension_set_items'*");

        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        (await IndexExistsAsync(Fixture.ConnectionString, "ix_platform_dimset_items_dimension_value_set"))
            .Should().BeTrue("bootstrapper must be able to restore dropped indexes via CREATE INDEX IF NOT EXISTS");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ValidateAsync_WhenClosedPeriodGuardTriggerDropped_FailsThenBootstrapperRepairs()
    {
        await Fixture.ResetDatabaseAsync();

        await DropTriggerAsync(Fixture.ConnectionString, "accounting_register_main", "trg_acc_reg_no_closed_period");
        (await TriggerExistsAsync(Fixture.ConnectionString, "accounting_register_main", "trg_acc_reg_no_closed_period"))
            .Should().BeFalse("the test must simulate trigger drift");

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var validator = scope.ServiceProvider.GetRequiredService<IAccountingCoreSchemaValidationService>();
        var act = () => validator.ValidateAsync(CancellationToken.None);

        await act.Should().ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage("*Missing trigger 'trg_acc_reg_no_closed_period' on 'accounting_register_main'*");

        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        (await TriggerExistsAsync(Fixture.ConnectionString, "accounting_register_main", "trg_acc_reg_no_closed_period"))
            .Should().BeTrue("bootstrapper must be able to restore dropped triggers via idempotent migrations");

        await act.Should().NotThrowAsync();
    }

    private static async Task DropIndexAsync(string cs, string indexName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP INDEX IF EXISTS public.{indexName};", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<bool> IndexExistsAsync(string cs, string indexName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*)
            FROM pg_indexes
            WHERE schemaname = 'public'
              AND indexname = @name;
            """,
            conn);

        cmd.Parameters.AddWithValue("name", indexName);
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        return count > 0;
    }

    private static async Task DropTriggerAsync(string cs, string tableName, string triggerName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP TRIGGER IF EXISTS {triggerName} ON public.{tableName};", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<bool> TriggerExistsAsync(string cs, string tableName, string triggerName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*)
            FROM pg_trigger t
            JOIN pg_class cl ON cl.oid = t.tgrelid
            JOIN pg_namespace ns ON ns.oid = cl.relnamespace
            WHERE ns.nspname = 'public'
              AND cl.relname = @table
              AND t.tgname = @trigger
              AND NOT t.tgisinternal;
            """,
            conn);

        cmd.Parameters.AddWithValue("table", tableName);
        cmd.Parameters.AddWithValue("trigger", triggerName);
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        return count > 0;
    }
}

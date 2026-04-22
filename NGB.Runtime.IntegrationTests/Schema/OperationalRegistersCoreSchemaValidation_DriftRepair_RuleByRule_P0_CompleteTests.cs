using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.Schema;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Schema;

[Collection(PostgresCollection.Name)]
public sealed class OperationalRegistersCoreSchemaValidation_DriftRepair_RuleByRule_P0_CompleteTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Theory]
    [InlineData("operational_registers")]
    [InlineData("operational_register_resources")]
    [InlineData("operational_register_dimension_rules")]
    [InlineData("operational_register_finalizations")]
    [InlineData("operational_register_write_state")]
    [InlineData("platform_dimensions")]
    [InlineData("platform_dimension_sets")]
    [InlineData("documents")]
    public async Task ValidateAsync_WhenRequiredTableMissing_FailsAndIsRepaired(string tableName)
    {
        var breakSql = $"DROP TABLE IF EXISTS {tableName} CASCADE;";
        await AssertDriftRepairAsync(breakSql, expectedMessagePart: $"Missing table '{tableName}'");
    }

    [Theory]
    // operational_registers
    [InlineData("operational_registers", "register_id")]
    [InlineData("operational_registers", "code")]
    [InlineData("operational_registers", "code_norm")]
    [InlineData("operational_registers", "name")]
    [InlineData("operational_registers", "table_code")]
    [InlineData("operational_registers", "has_movements")]
    // operational_register_resources
    [InlineData("operational_register_resources", "register_id")]
    [InlineData("operational_register_resources", "code")]
    [InlineData("operational_register_resources", "code_norm")]
    [InlineData("operational_register_resources", "column_code")]
    [InlineData("operational_register_resources", "name")]
    [InlineData("operational_register_resources", "ordinal")]
    // operational_register_dimension_rules
    [InlineData("operational_register_dimension_rules", "register_id")]
    [InlineData("operational_register_dimension_rules", "dimension_id")]
    [InlineData("operational_register_dimension_rules", "ordinal")]
    [InlineData("operational_register_dimension_rules", "is_required")]
    // operational_register_finalizations
    [InlineData("operational_register_finalizations", "register_id")]
    [InlineData("operational_register_finalizations", "period")]
    [InlineData("operational_register_finalizations", "status")]
    // operational_register_write_state
    [InlineData("operational_register_write_state", "register_id")]
    [InlineData("operational_register_write_state", "document_id")]
    [InlineData("operational_register_write_state", "operation")]
    [InlineData("operational_register_write_state", "started_at_utc")]
    [InlineData("operational_register_write_state", "completed_at_utc")]
    public async Task ValidateAsync_WhenRequiredColumnMissing_FailsAndIsRepaired(string tableName, string columnName)
    {
        var breakSql = $"ALTER TABLE {tableName} DROP COLUMN IF EXISTS {columnName} CASCADE;";
        await AssertDriftRepairAsync(breakSql, expectedMessagePart: $"Table '{tableName}' is missing column '{columnName}'");
    }

    [Theory]
    [InlineData("operational_registers", "ux_operational_registers_code_norm")]
    [InlineData("operational_registers", "ux_operational_registers_table_code")]
    [InlineData("operational_register_resources", "ix_opreg_resources_register_ordinal")]
    [InlineData("operational_register_dimension_rules", "ix_opreg_dim_rules_register_ordinal")]
    [InlineData("operational_register_finalizations", "ix_opreg_finalizations_register_period")]
    [InlineData("operational_register_write_state", "ix_opreg_write_log_document")]
    public async Task ValidateAsync_WhenCriticalIndexMissing_FailsAndIsRepaired(string tableName, string indexName)
    {
        var breakSql = $"DROP INDEX IF EXISTS {indexName};";
        await AssertDriftRepairAsync(breakSql, expectedMessagePart: $"Missing index '{indexName}' on table '{tableName}'");
    }

    [Theory]
    [InlineData("operational_register_resources", "ux_operational_register_resources__register_code_norm")]
    [InlineData("operational_register_resources", "ux_operational_register_resources__register_ordinal")]
    [InlineData("operational_register_dimension_rules", "ux_opreg_dim_rules__register_ordinal")]
    public async Task ValidateAsync_WhenUniqueConstraintMissing_FailsAndIsRepaired(string tableName, string constraintName)
    {
        var breakSql = $"ALTER TABLE {tableName} DROP CONSTRAINT IF EXISTS {constraintName};";
        await AssertDriftRepairAsync(breakSql, expectedMessagePart: $"Missing index '{constraintName}' on table '{tableName}'");
    }

    [Theory]
    [InlineData("operational_register_resources", "fk_opreg_resources__register", "register_id", "operational_registers", "register_id")]
    [InlineData("operational_register_dimension_rules", "fk_opreg_dim_rules_register", "register_id", "operational_registers", "register_id")]
    [InlineData("operational_register_dimension_rules", "fk_opreg_dim_rules_dimension", "dimension_id", "platform_dimensions", "dimension_id")]
    [InlineData("operational_register_finalizations", "fk_opreg_finalizations_register", "register_id", "operational_registers", "register_id")]
    [InlineData("operational_register_write_state", "fk_opreg_write_log_register", "register_id", "operational_registers", "register_id")]
    [InlineData("operational_register_write_state", "fk_opreg_write_log_document", "document_id", "documents", "id")]
    public async Task ValidateAsync_WhenForeignKeyMissing_FailsAndIsRepaired(
        string tableName,
        string constraintName,
        string columnName,
        string referencedTable,
        string referencedColumn)
    {
        var breakSql = $"ALTER TABLE {tableName} DROP CONSTRAINT IF EXISTS {constraintName};";
        await AssertDriftRepairAsync(
            breakSql,
            expectedMessagePart: $"Missing foreign key: {tableName}.{columnName} -> {referencedTable}.{referencedColumn}.");
    }

    [Theory]
    [InlineData("ngb_forbid_mutation_of_append_only_table")]
    [InlineData("ngb_opreg_forbid_resource_mutation_when_has_movements")]
    [InlineData("ngb_opreg_forbid_register_mutation_when_has_movements")]
    [InlineData("ngb_opreg_forbid_dim_rule_mutation_when_has_movements")]
    public async Task ValidateAsync_WhenGuardFunctionMissing_FailsAndIsRepaired(string functionName)
    {
        // CASCADE is required because trigger functions are referenced by triggers.
        var breakSql = $"DROP FUNCTION IF EXISTS {functionName}() CASCADE;";
        await AssertDriftRepairAsync(breakSql, expectedMessagePart: $"Missing function '{functionName}'.");
    }

    [Theory]
    [InlineData("operational_register_resources", "trg_opreg_resources_immutable_when_has_movements")]
    [InlineData("operational_registers", "trg_opreg_registers_immutable_when_has_movements")]
    [InlineData("operational_register_dimension_rules", "trg_opreg_dim_rules_immutable_when_has_movements")]
    public async Task ValidateAsync_WhenGuardTriggerMissing_FailsAndIsRepaired(string tableName, string triggerName)
    {
        var breakSql = $"DROP TRIGGER IF EXISTS {triggerName} ON {tableName};";
        await AssertDriftRepairAsync(breakSql, expectedMessagePart: $"Missing trigger '{triggerName}' on '{tableName}'.");
    }

    private async Task AssertDriftRepairAsync(string breakSql, string expectedMessagePart)
    {
        await Fixture.ResetDatabaseAsync();
        await ExecuteSqlAsync(breakSql);

        // Fail
        using (var host = IntegrationHostFactory.Create(Fixture.ConnectionString))
        {
            await using var scope = host.Services.CreateAsyncScope();
            var validator = scope.ServiceProvider.GetRequiredService<IOperationalRegistersCoreSchemaValidationService>();

            Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
            await act.Should().ThrowAsync<NgbConfigurationViolationException>()
                .WithMessage($"*{expectedMessagePart}*");
        }

        // Repair
        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        // Succeed
        using (var host = IntegrationHostFactory.Create(Fixture.ConnectionString))
        {
            await using var scope = host.Services.CreateAsyncScope();
            var validator = scope.ServiceProvider.GetRequiredService<IOperationalRegistersCoreSchemaValidationService>();

            Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
            await act.Should().NotThrowAsync();
        }
    }

    private async Task ExecuteSqlAsync(string sql)
    {
        // Some drift-repair tests perform heavy DDL churn; on some Docker-for-Mac setups
        // the Postgres process may transiently restart. Add a small retry to avoid flakes.
        const int maxAttempts = 8;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
                await conn.OpenAsync();

                await using var cmd = new NpgsqlCommand(sql, conn);
                await cmd.ExecuteNonQueryAsync();
                return;
            }
            catch (NpgsqlException ex) when (attempt < maxAttempts && ex.InnerException is System.Net.Sockets.SocketException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt));
            }
        }
    }
}

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.Schema;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Schema;

[Collection(PostgresCollection.Name)]
public sealed class OperationalRegistersCoreSchemaValidation_DriftRepair_RuleByRule_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ValidateAsync_WhenWriteLogDocumentIndexMissing_FailsThenBootstrapperRepairs()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await DropIndexAsync(Fixture.ConnectionString, "ix_opreg_write_log_document");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var validator = scope.ServiceProvider.GetRequiredService<IOperationalRegistersCoreSchemaValidationService>();

            Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
            await act.Should().ThrowAsync<NgbConfigurationViolationException>()
                .WithMessage("*Missing index 'ix_opreg_write_log_document' on table 'operational_register_write_state'*");
        }

        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var validator = scope.ServiceProvider.GetRequiredService<IOperationalRegistersCoreSchemaValidationService>();

            Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
            await act.Should().NotThrowAsync();
        }
    }

    [Fact]
    public async Task ValidateAsync_WhenWriteLogDocumentForeignKeyMissing_FailsThenBootstrapperRepairs()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await DropConstraintAsync(
            Fixture.ConnectionString,
            tableName: "operational_register_write_state",
            constraintName: "fk_opreg_write_log_document");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var validator = scope.ServiceProvider.GetRequiredService<IOperationalRegistersCoreSchemaValidationService>();

            Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
            await act.Should().ThrowAsync<NgbConfigurationViolationException>()
                .WithMessage("*Missing foreign key: operational_register_write_state.document_id -> documents.id*");
        }

        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var validator = scope.ServiceProvider.GetRequiredService<IOperationalRegistersCoreSchemaValidationService>();

            Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
            await act.Should().NotThrowAsync();
        }
    }

    [Fact]
    public async Task ValidateAsync_WhenResourcesImmutabilityTriggerMissing_FailsThenBootstrapperRepairs()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await DropTriggerAsync(
            Fixture.ConnectionString,
            tableName: "operational_register_resources",
            triggerName: "trg_opreg_resources_immutable_when_has_movements");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var validator = scope.ServiceProvider.GetRequiredService<IOperationalRegistersCoreSchemaValidationService>();

            Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
            await act.Should().ThrowAsync<NgbConfigurationViolationException>()
                .WithMessage("*Missing trigger 'trg_opreg_resources_immutable_when_has_movements' on 'operational_register_resources'*");
        }

        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var validator = scope.ServiceProvider.GetRequiredService<IOperationalRegistersCoreSchemaValidationService>();

            Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
            await act.Should().NotThrowAsync();
        }
    }

    [Fact]
    public async Task ValidateAsync_WhenRegisterImmutabilityFunctionMissing_FailsThenBootstrapperRepairs()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        // Make the trigger name exist (validator requires it), but redirect it to a known existing function.
        await DropTriggerAsync(
            Fixture.ConnectionString,
            tableName: "operational_registers",
            triggerName: "trg_opreg_registers_immutable_when_has_movements");

        await CreateTriggerUsingAppendOnlyGuardAsync(
            Fixture.ConnectionString,
            tableName: "operational_registers",
            triggerName: "trg_opreg_registers_immutable_when_has_movements",
            eventsSql: "BEFORE UPDATE OR DELETE");

        await DropFunctionAsync(Fixture.ConnectionString, "ngb_opreg_forbid_register_mutation_when_has_movements");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var validator = scope.ServiceProvider.GetRequiredService<IOperationalRegistersCoreSchemaValidationService>();

            Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
            await act.Should().ThrowAsync<NgbConfigurationViolationException>()
                .WithMessage("*Missing function 'ngb_opreg_forbid_register_mutation_when_has_movements'*");
        }

        // Remove the dummy trigger so bootstrapper can restore the correct one.
        await DropTriggerAsync(
            Fixture.ConnectionString,
            tableName: "operational_registers",
            triggerName: "trg_opreg_registers_immutable_when_has_movements");

        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var validator = scope.ServiceProvider.GetRequiredService<IOperationalRegistersCoreSchemaValidationService>();

            Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
            await act.Should().NotThrowAsync();
        }
    }

    [Fact]
    public async Task ValidateAsync_WhenDimensionRulesTableMissing_FailsThenBootstrapperRepairs()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await DropTableAsync(Fixture.ConnectionString, "operational_register_dimension_rules");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var validator = scope.ServiceProvider.GetRequiredService<IOperationalRegistersCoreSchemaValidationService>();

            Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
            await act.Should().ThrowAsync<NgbConfigurationViolationException>()
                .WithMessage("*Missing table 'operational_register_dimension_rules'*");
        }

        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var validator = scope.ServiceProvider.GetRequiredService<IOperationalRegistersCoreSchemaValidationService>();

            Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
            await act.Should().NotThrowAsync();
        }
    }

    private static async Task DropIndexAsync(string cs, string indexName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand($"DROP INDEX IF EXISTS {indexName};", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task DropTriggerAsync(string cs, string tableName, string triggerName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand($"DROP TRIGGER IF EXISTS {triggerName} ON {tableName};", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task DropConstraintAsync(string cs, string tableName, string constraintName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand($"ALTER TABLE {tableName} DROP CONSTRAINT IF EXISTS {constraintName};", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task DropFunctionAsync(string cs, string functionName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand($"DROP FUNCTION IF EXISTS {functionName}();", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task CreateTriggerUsingAppendOnlyGuardAsync(string cs, string tableName, string triggerName, string eventsSql)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        var sql = $"""
                   CREATE TRIGGER {triggerName}
                       {eventsSql}
                       ON {tableName}
                       FOR EACH ROW
                       EXECUTE FUNCTION ngb_forbid_mutation_of_append_only_table();
                   """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task DropTableAsync(string cs, string tableName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand($"DROP TABLE IF EXISTS {tableName} CASCADE;", conn);
        await cmd.ExecuteNonQueryAsync();
    }
}

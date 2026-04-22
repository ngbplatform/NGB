using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.Schema;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Schema;

/// <summary>
/// P0: rule-by-rule validator coverage for Operational Registers core schema.
/// Covers extra invariants not yet covered by the first rule-by-rule pack.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegistersCoreSchemaValidation_DriftRepair_RuleByRule_P0_2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ValidateAsync_WhenFinalizationsIndexMissing_FailsThenBootstrapperRepairs()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await DropIndexAsync(Fixture.ConnectionString, "ix_opreg_finalizations_register_period");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var validator = scope.ServiceProvider.GetRequiredService<IOperationalRegistersCoreSchemaValidationService>();

            Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
            await act.Should().ThrowAsync<NgbConfigurationViolationException>()
                .WithMessage("*ix_opreg_finalizations_register_period*");
        }

        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var validator = scope.ServiceProvider.GetRequiredService<IOperationalRegistersCoreSchemaValidationService>();
            await validator.Invoking(x => x.ValidateAsync(CancellationToken.None)).Should().NotThrowAsync();
        }
    }

    [Fact]
    public async Task ValidateAsync_WhenCodeNormIndexMissing_FailsThenBootstrapperRepairs()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await DropIndexAsync(Fixture.ConnectionString, "ux_operational_registers_code_norm");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var validator = scope.ServiceProvider.GetRequiredService<IOperationalRegistersCoreSchemaValidationService>();

            Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
            await act.Should().ThrowAsync<NgbConfigurationViolationException>()
                .WithMessage("*ux_operational_registers_code_norm*");
        }

        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var validator = scope.ServiceProvider.GetRequiredService<IOperationalRegistersCoreSchemaValidationService>();
            await validator.Invoking(x => x.ValidateAsync(CancellationToken.None)).Should().NotThrowAsync();
        }
    }

    [Fact]
    public async Task ValidateAsync_WhenDimRulesImmutabilityTriggerMissing_FailsThenBootstrapperRepairs()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await DropTriggerAsync(
            Fixture.ConnectionString,
            tableName: "operational_register_dimension_rules",
            triggerName: "trg_opreg_dim_rules_immutable_when_has_movements");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var validator = scope.ServiceProvider.GetRequiredService<IOperationalRegistersCoreSchemaValidationService>();

            Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
            await act.Should().ThrowAsync<NgbConfigurationViolationException>()
                .WithMessage("*trg_opreg_dim_rules_immutable_when_has_movements*");
        }

        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var validator = scope.ServiceProvider.GetRequiredService<IOperationalRegistersCoreSchemaValidationService>();
            await validator.Invoking(x => x.ValidateAsync(CancellationToken.None)).Should().NotThrowAsync();
        }
    }

    [Fact]
    public async Task ValidateAsync_WhenDimRulesImmutabilityFunctionMissing_FailsThenBootstrapperRepairs()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        // The trigger depends on the function, so drop the trigger first.
        await DropTriggerAsync(
            Fixture.ConnectionString,
            tableName: "operational_register_dimension_rules",
            triggerName: "trg_opreg_dim_rules_immutable_when_has_movements");

        await DropFunctionAsync(Fixture.ConnectionString, "ngb_opreg_forbid_dim_rule_mutation_when_has_movements");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var validator = scope.ServiceProvider.GetRequiredService<IOperationalRegistersCoreSchemaValidationService>();

            Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
            await act.Should().ThrowAsync<NgbConfigurationViolationException>()
                .WithMessage("*ngb_opreg_forbid_dim_rule_mutation_when_has_movements*");
        }

        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var validator = scope.ServiceProvider.GetRequiredService<IOperationalRegistersCoreSchemaValidationService>();
            await validator.Invoking(x => x.ValidateAsync(CancellationToken.None)).Should().NotThrowAsync();
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

    private static async Task DropFunctionAsync(string cs, string functionName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand($"DROP FUNCTION IF EXISTS {functionName}();", conn);
        await cmd.ExecuteNonQueryAsync();
    }
}

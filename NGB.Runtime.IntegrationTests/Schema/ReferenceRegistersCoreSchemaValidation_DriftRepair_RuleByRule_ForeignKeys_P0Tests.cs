using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.Schema;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Schema;

/// <summary>
/// Rule-by-rule drift-repair tests for Reference Registers core schema validation.
///
/// Pattern per test:
/// 1) break exactly one schema invariant,
/// 2) observe the exact validator error,
/// 3) re-apply platform migrations (bootstrapper),
/// 4) validate again (OK).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReferenceRegistersCoreSchemaValidation_DriftRepair_RuleByRule_ForeignKeys_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ValidateAsync_WhenRefRegFieldsRegisterFkDropped_FailsThenBootstrapperRepairs()
    {
        await Fixture.ResetDatabaseAsync();

        await DropConstraintAsync(Fixture.ConnectionString, "reference_register_fields", "fk_refreg_fields__register");

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var validator = scope.ServiceProvider.GetRequiredService<IReferenceRegistersCoreSchemaValidationService>();
        var act = () => validator.ValidateAsync(CancellationToken.None);

        await act.Should().ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage("*Missing foreign key: reference_register_fields.register_id -> reference_registers.register_id*");

        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ValidateAsync_WhenRefRegDimRulesRegisterFkDropped_FailsThenBootstrapperRepairs()
    {
        await Fixture.ResetDatabaseAsync();

        await DropConstraintAsync(Fixture.ConnectionString, "reference_register_dimension_rules", "fk_refreg_dim_rules__register");

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var validator = scope.ServiceProvider.GetRequiredService<IReferenceRegistersCoreSchemaValidationService>();
        var act = () => validator.ValidateAsync(CancellationToken.None);

        await act.Should().ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage("*Missing foreign key: reference_register_dimension_rules.register_id -> reference_registers.register_id*");

        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ValidateAsync_WhenRefRegDimRulesDimensionFkDropped_FailsThenBootstrapperRepairs()
    {
        await Fixture.ResetDatabaseAsync();

        await DropConstraintAsync(Fixture.ConnectionString, "reference_register_dimension_rules", "fk_refreg_dim_rules__dimension");

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var validator = scope.ServiceProvider.GetRequiredService<IReferenceRegistersCoreSchemaValidationService>();
        var act = () => validator.ValidateAsync(CancellationToken.None);

        await act.Should().ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage("*Missing foreign key: reference_register_dimension_rules.dimension_id -> platform_dimensions.dimension_id*");

        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        await act.Should().NotThrowAsync();
    }

    private static async Task DropConstraintAsync(string cs, string tableName, string constraintName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            $"ALTER TABLE public.{tableName} DROP CONSTRAINT IF EXISTS {constraintName};",
            conn);

        await cmd.ExecuteNonQueryAsync();
    }
}

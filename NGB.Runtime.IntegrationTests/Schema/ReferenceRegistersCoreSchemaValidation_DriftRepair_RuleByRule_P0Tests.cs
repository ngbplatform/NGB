using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.Schema;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Schema;

[Collection(PostgresCollection.Name)]
public sealed class ReferenceRegistersCoreSchemaValidation_DriftRepair_RuleByRule_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ValidateAsync_WhenWriteLogDocumentIndexMissing_FailsThenBootstrapperRepairs()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await DropIndexAsync(Fixture.ConnectionString, "ix_refreg_write_log_document");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var validator = scope.ServiceProvider.GetRequiredService<IReferenceRegistersCoreSchemaValidationService>();

            Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
            await act.Should().ThrowAsync<NgbConfigurationViolationException>()
                .WithMessage("*Missing index 'ix_refreg_write_log_document'*");
        }

        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var validator = scope.ServiceProvider.GetRequiredService<IReferenceRegistersCoreSchemaValidationService>();

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
            tableName: "reference_register_write_state",
            constraintName: "fk_refreg_write_log_document");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var validator = scope.ServiceProvider.GetRequiredService<IReferenceRegistersCoreSchemaValidationService>();

            Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
            await act.Should().ThrowAsync<NgbConfigurationViolationException>()
                .WithMessage("*Missing foreign key: reference_register_write_state.document_id -> documents.id*");
        }

        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var validator = scope.ServiceProvider.GetRequiredService<IReferenceRegistersCoreSchemaValidationService>();

            Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
            await act.Should().NotThrowAsync();
        }
    }

    [Fact]
    public async Task ValidateAsync_WhenFieldsImmutabilityTriggerMissing_FailsThenBootstrapperRepairs()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await DropTriggerAsync(
            Fixture.ConnectionString,
            tableName: "reference_register_fields",
            triggerName: "trg_refreg_fields_immutable_when_has_records");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var validator = scope.ServiceProvider.GetRequiredService<IReferenceRegistersCoreSchemaValidationService>();

            Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
            await act.Should().ThrowAsync<NgbConfigurationViolationException>()
                .WithMessage("*Missing trigger 'trg_refreg_fields_immutable_when_has_records' on 'reference_register_fields'*");
        }

        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var validator = scope.ServiceProvider.GetRequiredService<IReferenceRegistersCoreSchemaValidationService>();

            Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
            await act.Should().NotThrowAsync();
        }
    }

    [Fact]
    public async Task ValidateAsync_WhenDimensionRulesTableMissing_FailsThenBootstrapperRepairs()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await DropTableAsync(Fixture.ConnectionString, "reference_register_dimension_rules");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var validator = scope.ServiceProvider.GetRequiredService<IReferenceRegistersCoreSchemaValidationService>();

            Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
            await act.Should().ThrowAsync<NgbConfigurationViolationException>()
                .WithMessage("*Missing table 'reference_register_dimension_rules'*");
        }

        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var validator = scope.ServiceProvider.GetRequiredService<IReferenceRegistersCoreSchemaValidationService>();

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

    private static async Task DropTableAsync(string cs, string tableName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand($"DROP TABLE IF EXISTS {tableName} CASCADE;", conn);
        await cmd.ExecuteNonQueryAsync();
    }
}

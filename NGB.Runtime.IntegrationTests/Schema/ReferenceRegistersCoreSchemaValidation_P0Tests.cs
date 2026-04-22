using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.Schema;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Schema;

[Collection(PostgresCollection.Name)]
public sealed class ReferenceRegistersCoreSchemaValidation_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ValidateAsync_WhenSchemaIsIntact_Succeeds()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IReferenceRegistersCoreSchemaValidationService>();

        Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ValidateAsync_WhenHasRecordsTriggerMissing_ThrowsWithHelpfulMessage()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await DropTriggerAsync(
            Fixture.ConnectionString,
            tableName: "reference_register_fields",
            triggerName: "trg_refreg_fields_immutable_when_has_records");

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IReferenceRegistersCoreSchemaValidationService>();

        Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
        await act.Should().ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage("*trg_refreg_fields_immutable_when_has_records*");
    }

    [Fact]
    public async Task ValidateAsync_WhenCodeNormIndexMissing_ThrowsWithHelpfulMessage()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await DropIndexAsync(
            Fixture.ConnectionString,
            indexName: "ux_reference_registers_code_norm");

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IReferenceRegistersCoreSchemaValidationService>();

        Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
        await act.Should().ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage("*ux_reference_registers_code_norm*");
    }

    [Fact]
    public async Task ValidateAsync_WhenImmutabilityGuardFunctionMissing_ThrowsWithHelpfulMessage()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        // The trigger depends on the function; CASCADE will drop the trigger as well.
        await DropFunctionAsync(
            Fixture.ConnectionString,
            functionNameAndArgs: "ngb_refreg_forbid_field_mutation_when_has_records()",
            cascade: true);

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IReferenceRegistersCoreSchemaValidationService>();

        Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
        await act.Should().ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage("*ngb_refreg_forbid_field_mutation_when_has_records*");
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

    private static async Task DropFunctionAsync(string cs, string functionNameAndArgs, bool cascade)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        var cascadeSql = cascade ? " CASCADE" : string.Empty;
        await using var cmd = new NpgsqlCommand($"DROP FUNCTION IF EXISTS {functionNameAndArgs}{cascadeSql};", conn);
        await cmd.ExecuteNonQueryAsync();
    }
}

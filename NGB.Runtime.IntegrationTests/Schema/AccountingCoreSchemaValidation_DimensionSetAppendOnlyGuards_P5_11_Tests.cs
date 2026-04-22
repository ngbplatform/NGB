using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.Schema;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Schema;

[Collection(PostgresCollection.Name)]
public sealed class AccountingCoreSchemaValidation_DimensionSetAppendOnlyGuards_P5_11_Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ValidateAsync_WhenAppendOnlyGuardFunctionMissing_ThrowsWithHelpfulMessage()
    {
        await Fixture.ResetDatabaseAsync();

        // The guard function is shared (AuditLog + DimensionSets). We drop it with CASCADE for this test.
        await DropFunctionCascadeAsync(Fixture.ConnectionString, "ngb_forbid_mutation_of_append_only_table");

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IAccountingCoreSchemaValidationService>();

        var act = () => validator.ValidateAsync(CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbConfigurationViolationException>();
        ex.Which.Message.Should().Contain("ngb_forbid_mutation_of_append_only_table");
    }

    [Fact]
    public async Task ValidateAsync_WhenDimensionSetsAppendOnlyTriggerMissing_ThrowsWithHelpfulMessage()
    {
        await Fixture.ResetDatabaseAsync();

        await DropTriggerAsync(Fixture.ConnectionString, "platform_dimension_sets", "trg_platform_dimension_sets_append_only");

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IAccountingCoreSchemaValidationService>();

        var act = () => validator.ValidateAsync(CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbConfigurationViolationException>();
        ex.Which.Message.Should().Contain("trg_platform_dimension_sets_append_only");
        ex.Which.Message.Should().Contain("platform_dimension_sets");
    }

    [Fact]
    public async Task ValidateAsync_WhenDimensionSetItemsAppendOnlyTriggerMissing_ThrowsWithHelpfulMessage()
    {
        await Fixture.ResetDatabaseAsync();

        await DropTriggerAsync(Fixture.ConnectionString, "platform_dimension_set_items", "trg_platform_dimension_set_items_append_only");

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IAccountingCoreSchemaValidationService>();

        var act = () => validator.ValidateAsync(CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbConfigurationViolationException>();
        ex.Which.Message.Should().Contain("trg_platform_dimension_set_items_append_only");
        ex.Which.Message.Should().Contain("platform_dimension_set_items");
    }

    private static async Task DropTriggerAsync(string cs, string tableName, string triggerName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand($"DROP TRIGGER IF EXISTS {triggerName} ON public.{tableName};", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task DropFunctionCascadeAsync(string cs, string functionName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand($"DROP FUNCTION IF EXISTS public.{functionName}() CASCADE;", conn);
        await cmd.ExecuteNonQueryAsync();
    }
}

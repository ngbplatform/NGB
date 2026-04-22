using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.Schema;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Schema;

[Collection(PostgresCollection.Name)]
public sealed class AccountingCoreSchemaValidation_EmptyDimensionSetMustHaveNoItems_P5_12_Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ValidateAsync_WhenEmptyDimensionSetHasItems_ThrowsWithHelpfulMessage()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        // DB normally forbids items with Guid.Empty via CHECK constraint. We drop it to simulate schema drift.
        await DropConstraintAsync(Fixture.ConnectionString, "platform_dimension_set_items", "ck_platform_dimset_items_set_nonempty");

        var dimensionId = Guid.CreateVersion7();
        var valueId = Guid.CreateVersion7();

        await InsertDimensionAsync(Fixture.ConnectionString, dimensionId, "TEST_DIM", "Test Dimension");
        await InsertDimensionSetItemAsync(Fixture.ConnectionString, Guid.Empty, dimensionId, valueId);

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IAccountingCoreSchemaValidationService>();

        Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
        await act.Should().ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage("*Reserved empty-set row (Guid.Empty) must have zero items*platform_dimension_set_items*");
    }

    private static async Task DropConstraintAsync(string cs, string tableName, string constraintName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand($"ALTER TABLE {tableName} DROP CONSTRAINT IF EXISTS {constraintName};", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task InsertDimensionAsync(string cs, Guid dimensionId, string code, string name)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "INSERT INTO platform_dimensions (dimension_id, code, name) VALUES (@id, @code, @name);",
            conn);
        cmd.Parameters.AddWithValue("id", dimensionId);
        cmd.Parameters.AddWithValue("code", code);
        cmd.Parameters.AddWithValue("name", name);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task InsertDimensionSetItemAsync(string cs, Guid dimensionSetId, Guid dimensionId, Guid valueId)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "INSERT INTO platform_dimension_set_items (dimension_set_id, dimension_id, value_id) VALUES (@setId, @dimId, @valueId);",
            conn);
        cmd.Parameters.AddWithValue("setId", dimensionSetId);
        cmd.Parameters.AddWithValue("dimId", dimensionId);
        cmd.Parameters.AddWithValue("valueId", valueId);
        await cmd.ExecuteNonQueryAsync();
    }
}

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.Schema;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Schema;

[Collection(PostgresCollection.Name)]
public sealed class AccountingCoreSchemaValidation_DimensionSets_P5_5_Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ValidateAsync_WhenReservedEmptyDimensionSetRowMissing_ThrowsWithHelpfulMessage()
    {
        await Fixture.ResetDatabaseAsync();

        // Dimension sets are append-only, but TRUNCATE bypasses row-level triggers and removes the reserved Guid.Empty row.
        await TruncateTableCascadeAsync(Fixture.ConnectionString, "platform_dimension_sets");

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IAccountingCoreSchemaValidationService>();

        var act = () => validator.ValidateAsync(CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbConfigurationViolationException>();

        ex.Which.Message.Should().Contain("platform_dimension_sets");
        ex.Which.Message.Should().MatchRegex("(?s).*(Guid.Empty|00000000-0000-0000-0000-000000000000).*");
    }

    private static async Task TruncateTableCascadeAsync(string cs, string tableName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        // CASCADE is required because accounting tables reference platform_dimension_sets via FK.
        await using var cmd = new NpgsqlCommand($"TRUNCATE TABLE {tableName} CASCADE;", conn);
        await cmd.ExecuteNonQueryAsync();
    }
}

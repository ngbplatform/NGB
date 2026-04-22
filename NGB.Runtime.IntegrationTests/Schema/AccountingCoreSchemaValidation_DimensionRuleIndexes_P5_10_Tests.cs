using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.Schema;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Schema;

[Collection(PostgresCollection.Name)]
public sealed class AccountingCoreSchemaValidation_DimensionRuleIndexes_P5_10_Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ValidateAsync_WhenDimensionRulesUniqueOrdinalIndexMissing_ThrowsWithHelpfulMessage()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await DropIndexAsync(Fixture.ConnectionString, "ux_acc_dim_rules_account_ordinal");

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IAccountingCoreSchemaValidationService>();

        Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
        await act.Should().ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage("*ux_acc_dim_rules_account_ordinal*");
    }

    [Fact]
    public async Task ValidateAsync_WhenDimensionRulesDimensionIdIndexMissing_ThrowsWithHelpfulMessage()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await DropIndexAsync(Fixture.ConnectionString, "ix_acc_dim_rules_dimension_id");

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IAccountingCoreSchemaValidationService>();

        Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
        await act.Should().ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage("*ix_acc_dim_rules_dimension_id*");
    }

    private static async Task DropIndexAsync(string cs, string indexName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand($"DROP INDEX IF EXISTS {indexName};", conn);
        await cmd.ExecuteNonQueryAsync();
    }
}

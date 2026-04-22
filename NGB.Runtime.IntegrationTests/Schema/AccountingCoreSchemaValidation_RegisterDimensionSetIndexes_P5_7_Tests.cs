using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.Schema;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Schema;

[Collection(PostgresCollection.Name)]
public sealed class AccountingCoreSchemaValidation_RegisterDimensionSetIndexes_P5_7_Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ValidateAsync_WhenRegisterDebitDimensionSetIndexMissing_ThrowsWithHelpfulMessage()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await DropIndexAsync(Fixture.ConnectionString, "ix_acc_reg_debit_month_dimset");

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IAccountingCoreSchemaValidationService>();

        Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
        await act.Should().ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage("*ix_acc_reg_debit_month_dimset*");
    }

    [Fact]
    public async Task ValidateAsync_WhenRegisterCreditDimensionSetIndexMissing_ThrowsWithHelpfulMessage()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await DropIndexAsync(Fixture.ConnectionString, "ix_acc_reg_credit_month_dimset");

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IAccountingCoreSchemaValidationService>();

        Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
        await act.Should().ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage("*ix_acc_reg_credit_month_dimset*");
    }

    private static async Task DropIndexAsync(string cs, string indexName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand($"DROP INDEX IF EXISTS {indexName};", conn);
        await cmd.ExecuteNonQueryAsync();
    }
}

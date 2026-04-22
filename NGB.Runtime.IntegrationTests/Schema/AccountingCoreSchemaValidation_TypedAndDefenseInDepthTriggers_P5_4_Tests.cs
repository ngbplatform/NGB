using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.Schema;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Schema;

[Collection(PostgresCollection.Name)]
public sealed class AccountingCoreSchemaValidation_TypedAndDefenseInDepthTriggers_P5_4_Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ValidateAsync_WhenTypedDocumentImmutabilityTriggerMissing_ThrowsWithHelpfulMessage()
    {
        await Fixture.ResetDatabaseAsync();

        // Remove one of the required typed-document immutability triggers.
        await DropTriggerAsync(Fixture.ConnectionString, "doc_general_journal_entry", "trg_posted_immutable");

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IAccountingCoreSchemaValidationService>();

        var act = () => validator.ValidateAsync(CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbConfigurationViolationException>();

        ex.Which.Message.Should().Contain("trg_posted_immutable");
        ex.Which.Message.Should().Contain("doc_general_journal_entry");
    }

    [Fact]
    public async Task ValidateAsync_WhenDefenseInDepthTurnoversTriggerMissing_ThrowsWithHelpfulMessage()
    {
        await Fixture.ResetDatabaseAsync();

        await DropTriggerAsync(Fixture.ConnectionString, "accounting_turnovers", "trg_acc_turnovers_no_closed_period");

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IAccountingCoreSchemaValidationService>();

        var act = () => validator.ValidateAsync(CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbConfigurationViolationException>();

        ex.Which.Message.Should().Contain("trg_acc_turnovers_no_closed_period");
    }

    [Fact]
    public async Task ValidateAsync_WhenDefenseInDepthBalancesTriggerMissing_ThrowsWithHelpfulMessage()
    {
        await Fixture.ResetDatabaseAsync();

        await DropTriggerAsync(Fixture.ConnectionString, "accounting_balances", "trg_acc_balances_no_closed_period");

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IAccountingCoreSchemaValidationService>();

        var act = () => validator.ValidateAsync(CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbConfigurationViolationException>();

        ex.Which.Message.Should().Contain("trg_acc_balances_no_closed_period");
    }

    private static async Task DropTriggerAsync(string cs, string tableName, string triggerName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand($"DROP TRIGGER IF EXISTS {triggerName} ON public.{tableName};", conn);
        await cmd.ExecuteNonQueryAsync();
    }
}

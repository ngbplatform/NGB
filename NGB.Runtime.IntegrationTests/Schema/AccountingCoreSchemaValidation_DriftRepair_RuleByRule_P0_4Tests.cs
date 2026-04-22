using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.Schema;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Schema;

/// <summary>
/// Additional rule-by-rule drift-repair tests for Accounting core schema validation.
///
/// Pattern per test:
/// 1) break exactly one schema invariant (drop one index / trigger / function),
/// 2) observe the exact validator error,
/// 3) re-apply platform migrations (bootstrapper),
/// 4) validate again (OK).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AccountingCoreSchemaValidation_DriftRepair_RuleByRule_P0_4Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ValidateAsync_WhenPlatformDimensionsUniqueIndexDropped_FailsThenBootstrapperRepairs()
    {
        await Fixture.ResetDatabaseAsync();

        await DropIndexAsync(Fixture.ConnectionString, "ux_platform_dimensions_code_norm_not_deleted");

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var validator = scope.ServiceProvider.GetRequiredService<IAccountingCoreSchemaValidationService>();
        var act = () => validator.ValidateAsync(CancellationToken.None);

        await act.Should().ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage("*Missing index 'ux_platform_dimensions_code_norm_not_deleted' on table 'platform_dimensions'*");

        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ValidateAsync_WhenDocumentsTypeNumberUniqueIndexDropped_FailsThenBootstrapperRepairs()
    {
        await Fixture.ResetDatabaseAsync();

        await DropIndexAsync(Fixture.ConnectionString, "ux_documents_type_number_not_null");

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var validator = scope.ServiceProvider.GetRequiredService<IAccountingCoreSchemaValidationService>();
        var act = () => validator.ValidateAsync(CancellationToken.None);

        await act.Should().ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage("*Missing index 'ux_documents_type_number_not_null' on table 'documents'*");

        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ValidateAsync_WhenTurnoversClosedPeriodDeleteTriggerDropped_FailsThenBootstrapperRepairs()
    {
        await Fixture.ResetDatabaseAsync();

        await DropTriggerAsync(Fixture.ConnectionString, "accounting_turnovers", "trg_acc_turnovers_no_closed_period_delete");

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var validator = scope.ServiceProvider.GetRequiredService<IAccountingCoreSchemaValidationService>();
        var act = () => validator.ValidateAsync(CancellationToken.None);

        await act.Should().ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage("*Missing trigger 'trg_acc_turnovers_no_closed_period_delete' on 'accounting_turnovers'*");

        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ValidateAsync_WhenBalancesClosedPeriodDeleteTriggerDropped_FailsThenBootstrapperRepairs()
    {
        await Fixture.ResetDatabaseAsync();

        await DropTriggerAsync(Fixture.ConnectionString, "accounting_balances", "trg_acc_balances_no_closed_period_delete");

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var validator = scope.ServiceProvider.GetRequiredService<IAccountingCoreSchemaValidationService>();
        var act = () => validator.ValidateAsync(CancellationToken.None);

        await act.Should().ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage("*Missing trigger 'trg_acc_balances_no_closed_period_delete' on 'accounting_balances'*");

        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ValidateAsync_WhenPostedDocumentImmutabilityGuardFunctionMissing_FailsThenBootstrapperRepairs()
    {
        await Fixture.ResetDatabaseAsync();

        // The validator checks both: (a) existence of ngb_forbid_mutation_of_posted_document()
        // and (b) presence of trg_posted_immutable on all typed doc_* tables.
        //
        // To isolate the "function missing" rule, we keep trg_posted_immutable on all typed tables,
        // but temporarily point it to a harmless stub trigger function.
        const string stub = "ngb_test_allow_all_mutations";
        await CreateOrReplaceAllowAllTriggerFunctionAsync(Fixture.ConnectionString, stub);

        var typedTables = await GetTypedDocumentTablesAsync(Fixture.ConnectionString);

        foreach (var t in typedTables)
        {
            await DropTriggerAsync(Fixture.ConnectionString, t, "trg_posted_immutable");
            await CreateTriggerAsync(
                Fixture.ConnectionString,
                tableName: t,
                triggerName: "trg_posted_immutable",
                eventsSql: "BEFORE INSERT OR UPDATE OR DELETE",
                functionSql: $"{stub}()");
        }

        await DropFunctionAsync(Fixture.ConnectionString, "ngb_forbid_mutation_of_posted_document");

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var validator = scope.ServiceProvider.GetRequiredService<IAccountingCoreSchemaValidationService>();
        var act = () => validator.ValidateAsync(CancellationToken.None);

        await act.Should().ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage("*Missing function 'ngb_forbid_mutation_of_posted_document'*");

        // Clean up triggers so migrations can re-install real guards.
        foreach (var t in typedTables)
            await DropTriggerAsync(Fixture.ConnectionString, t, "trg_posted_immutable");

        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ValidateAsync_WhenTypedDocumentImmutabilityInstallerFunctionMissing_FailsThenBootstrapperRepairs()
    {
        await Fixture.ResetDatabaseAsync();

        await DropFunctionAsync(Fixture.ConnectionString, "ngb_install_typed_document_immutability_guards");

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var validator = scope.ServiceProvider.GetRequiredService<IAccountingCoreSchemaValidationService>();
        var act = () => validator.ValidateAsync(CancellationToken.None);

        await act.Should().ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage("*Missing function 'ngb_install_typed_document_immutability_guards'*");

        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ValidateAsync_WhenAppendOnlyGuardFunctionMissing_FailsThenBootstrapperRepairs()
    {
        await Fixture.ResetDatabaseAsync();

        // To isolate "function missing", keep the expected trigger names present,
        // but temporarily point them to a harmless stub trigger function.
        const string stub = "ngb_test_allow_update_delete";
        await CreateOrReplaceAllowAllTriggerFunctionAsync(Fixture.ConnectionString, stub);

        await DropTriggerAsync(Fixture.ConnectionString, "platform_dimension_sets", "trg_platform_dimension_sets_append_only");
        await DropTriggerAsync(Fixture.ConnectionString, "platform_dimension_set_items", "trg_platform_dimension_set_items_append_only");

        await CreateTriggerAsync(
            Fixture.ConnectionString,
            tableName: "platform_dimension_sets",
            triggerName: "trg_platform_dimension_sets_append_only",
            eventsSql: "BEFORE UPDATE OR DELETE",
            functionSql: $"{stub}()");

        await CreateTriggerAsync(
            Fixture.ConnectionString,
            tableName: "platform_dimension_set_items",
            triggerName: "trg_platform_dimension_set_items_append_only",
            eventsSql: "BEFORE UPDATE OR DELETE",
            functionSql: $"{stub}()");

        await DropFunctionCascadeAsync(Fixture.ConnectionString, "ngb_forbid_mutation_of_append_only_table");

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var validator = scope.ServiceProvider.GetRequiredService<IAccountingCoreSchemaValidationService>();
        var act = () => validator.ValidateAsync(CancellationToken.None);

        await act.Should().ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage("*Missing function 'ngb_forbid_mutation_of_append_only_table'*");

        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ValidateAsync_WhenDimensionSetAppendOnlyTriggerDropped_FailsThenBootstrapperRepairs()
    {
        await Fixture.ResetDatabaseAsync();

        await DropTriggerAsync(Fixture.ConnectionString, "platform_dimension_set_items", "trg_platform_dimension_set_items_append_only");

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var validator = scope.ServiceProvider.GetRequiredService<IAccountingCoreSchemaValidationService>();
        var act = () => validator.ValidateAsync(CancellationToken.None);

        await act.Should().ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage("*Missing trigger 'trg_platform_dimension_set_items_append_only' on 'platform_dimension_set_items'*");

        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString);

        await act.Should().NotThrowAsync();
    }

    private static async Task DropIndexAsync(string cs, string indexName)
        => await ExecuteNonQueryAsync(cs, $"DROP INDEX IF EXISTS public.{indexName};");

    private static async Task DropTriggerAsync(string cs, string tableName, string triggerName)
        => await ExecuteNonQueryAsync(cs, $"DROP TRIGGER IF EXISTS {triggerName} ON public.{tableName};");

    private static async Task DropFunctionAsync(string cs, string functionName)
        => await ExecuteNonQueryAsync(cs, $"DROP FUNCTION IF EXISTS public.{functionName}();");

    private static async Task DropFunctionCascadeAsync(string cs, string functionName)
        => await ExecuteNonQueryAsync(cs, $"DROP FUNCTION IF EXISTS public.{functionName}() CASCADE;");

    private static async Task CreateTriggerAsync(


        string cs,
        string tableName,
        string triggerName,
        string eventsSql,
        string functionSql)
    {
        var sql = $"""
                  CREATE TRIGGER {triggerName}
                      {eventsSql} ON public.{tableName}
                      FOR EACH ROW
                      EXECUTE FUNCTION {functionSql};
                  """;

        await ExecuteNonQueryAsync(cs, sql);
    }

    private static async Task CreateOrReplaceAllowAllTriggerFunctionAsync(string cs, string functionName)
    {
        var sql = $"""
                  CREATE OR REPLACE FUNCTION public.{functionName}()
                  RETURNS trigger AS $$
                  BEGIN
                      IF TG_OP = 'DELETE' THEN
                          RETURN OLD;
                      END IF;

                      RETURN NEW;
                  END;
                  $$ LANGUAGE plpgsql;
                  """;

        await ExecuteNonQueryAsync(cs, sql);
    }

    private static async Task<string[]> GetTypedDocumentTablesAsync(string cs)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            """
            SELECT DISTINCT c.table_name
            FROM information_schema.columns c
            WHERE c.table_schema = 'public'
              AND c.column_name = 'document_id'
              AND c.table_name LIKE E'doc\\_%' ESCAPE E'\\'
            ORDER BY c.table_name;
            """,
            conn);

        var list = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add(reader.GetString(0));

        list.Should().NotBeEmpty("at least one typed document table must exist in the platform schema");
        return list.ToArray();
    }

    private static async Task ExecuteNonQueryAsync(string cs, string sql)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}

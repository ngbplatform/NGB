using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.Schema;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Schema;

/// <summary>
/// P0 drift-repair tests: each validator rule is covered by an isolated test that:
/// 1) breaks one specific schema object,
/// 2) observes the exact failure message from the validator,
/// 3) re-applies platform migrations (bootstrapper) to repair,
/// 4) validates again (OK).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AccountingCoreSchemaValidation_DriftRepair_RuleByRule_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ValidateAsync_WhenRegisterMonthIndexMissing_FailsThenBootstrapperRepairs()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await DropIndexIfExistsAsync(Fixture.ConnectionString, "ix_acc_reg_period_month");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var validator = scope.ServiceProvider.GetRequiredService<IAccountingCoreSchemaValidationService>();

            Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
            await act.Should().ThrowAsync<NgbConfigurationViolationException>()
                .WithMessage("*Missing index 'ix_acc_reg_period_month' on table 'accounting_register_main'*" );
        }

        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString, CancellationToken.None);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var validator = scope.ServiceProvider.GetRequiredService<IAccountingCoreSchemaValidationService>();

            Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
            await act.Should().NotThrowAsync();
        }
    }

    [Fact]
    public async Task ValidateAsync_WhenRegisterDebitDimensionSetForeignKeyMissing_FailsThenBootstrapperRepairs()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await DropConstraintIfExistsAsync(Fixture.ConnectionString, "accounting_register_main", "fk_acc_reg_debit_dimension_set");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var validator = scope.ServiceProvider.GetRequiredService<IAccountingCoreSchemaValidationService>();

            Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
            await act.Should().ThrowAsync<NgbConfigurationViolationException>()
                .WithMessage("*Missing foreign key: accounting_register_main.debit_dimension_set_id -> platform_dimension_sets.dimension_set_id*" );
        }

        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString, CancellationToken.None);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var validator = scope.ServiceProvider.GetRequiredService<IAccountingCoreSchemaValidationService>();

            Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
            await act.Should().NotThrowAsync();
        }
    }

    [Fact]
    public async Task ValidateAsync_WhenClosedPeriodGuardFunctionMissing_FailsThenBootstrapperRepairs()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        // NOTE: the validator checks triggers *and* the function. To isolate the "function missing" rule,
        // we temporarily replace the closed-period triggers with harmless stubs that keep the trigger names.
        const string noopFunction = "ngb_test_allow_all_trigger";
        await CreateOrReplaceNoopTriggerFunctionAsync(Fixture.ConnectionString, noopFunction);

        var triggerDefs = new (string Table, string Trigger, string Events)[]
        {
            ("accounting_register_main", "trg_acc_reg_no_closed_period", "BEFORE INSERT OR UPDATE"),
            ("accounting_register_main", "trg_acc_reg_no_closed_period_delete", "BEFORE DELETE"),
            ("accounting_turnovers", "trg_acc_turnovers_no_closed_period", "BEFORE INSERT OR UPDATE"),
            ("accounting_turnovers", "trg_acc_turnovers_no_closed_period_delete", "BEFORE DELETE"),
            ("accounting_balances", "trg_acc_balances_no_closed_period", "BEFORE INSERT OR UPDATE"),
            ("accounting_balances", "trg_acc_balances_no_closed_period_delete", "BEFORE DELETE")
        };

        foreach (var (table, trg, _) in triggerDefs)
            await DropTriggerIfExistsAsync(Fixture.ConnectionString, table, trg);

        foreach (var (table, trg, eventsSql) in triggerDefs)
            await CreateTriggerAsync(Fixture.ConnectionString, table, trg, eventsSql, $"{noopFunction}()");

        await DropFunctionIfExistsAsync(Fixture.ConnectionString, "ngb_forbid_posting_into_closed_period");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var validator = scope.ServiceProvider.GetRequiredService<IAccountingCoreSchemaValidationService>();

            Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
            await act.Should().ThrowAsync<NgbConfigurationViolationException>()
                .WithMessage("*Missing function 'ngb_forbid_posting_into_closed_period'*" );
        }

        // Clean up stub triggers so reapplying migrations can recreate the proper guards.
        foreach (var (table, trg, _) in triggerDefs)
            await DropTriggerIfExistsAsync(Fixture.ConnectionString, table, trg);

        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString, CancellationToken.None);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var validator = scope.ServiceProvider.GetRequiredService<IAccountingCoreSchemaValidationService>();

            Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
            await act.Should().NotThrowAsync();
        }
    }

    [Fact]
    public async Task ValidateAsync_WhenReservedEmptyDimensionSetRowMissing_FailsThenBootstrapperRepairs()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        // platform_dimension_sets is append-only. Temporarily remove the guard trigger to delete Guid.Empty row.
        await DropTriggerIfExistsAsync(Fixture.ConnectionString, "platform_dimension_sets", "trg_platform_dimension_sets_append_only");

        await ExecuteNonQueryAsync(
            Fixture.ConnectionString,
            "DELETE FROM platform_dimension_sets WHERE dimension_set_id = '00000000-0000-0000-0000-000000000000';");

        await CreateTriggerAsync(
            Fixture.ConnectionString,
            tableName: "platform_dimension_sets",
            triggerName: "trg_platform_dimension_sets_append_only",
            eventsSql: "BEFORE UPDATE OR DELETE",
            functionSql: "ngb_forbid_mutation_of_append_only_table()");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var validator = scope.ServiceProvider.GetRequiredService<IAccountingCoreSchemaValidationService>();

            Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
            await act.Should().ThrowAsync<NgbConfigurationViolationException>()
                .WithMessage("*Missing reserved empty dimension set row in platform_dimension_sets for id '00000000-0000-0000-0000-000000000000'*" );
        }

        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString, CancellationToken.None);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var validator = scope.ServiceProvider.GetRequiredService<IAccountingCoreSchemaValidationService>();

            Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
            await act.Should().NotThrowAsync();
        }
    }

    [Fact]
    public async Task ValidateAsync_WhenTypedDocumentTableMissingImmutabilityTrigger_FailsThenBootstrapperRepairs()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        // Create a new typed document table that matches the validator pattern (public doc_* with document_id).
        await ExecuteNonQueryAsync(
            Fixture.ConnectionString,
            """
            DROP TABLE IF EXISTS public.doc_schema_validation_test;
            CREATE TABLE public.doc_schema_validation_test (
                document_id uuid NOT NULL,
                payload text NULL
            );
            """);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var validator = scope.ServiceProvider.GetRequiredService<IAccountingCoreSchemaValidationService>();

            Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
            await act.Should().ThrowAsync<NgbConfigurationViolationException>()
                .WithMessage("*Missing trigger 'trg_posted_immutable' on typed document table 'doc_schema_validation_test'*" );
        }

        await MigrationSet.ApplyPlatformMigrationsAsync(Fixture.ConnectionString, CancellationToken.None);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var validator = scope.ServiceProvider.GetRequiredService<IAccountingCoreSchemaValidationService>();

            Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
            await act.Should().NotThrowAsync();
        }
    }

    private static async Task DropIndexIfExistsAsync(string connectionString, string indexName)
        => await ExecuteNonQueryAsync(connectionString, $"DROP INDEX IF EXISTS {indexName};");

    private static async Task DropConstraintIfExistsAsync(string connectionString, string tableName, string constraintName)
        => await ExecuteNonQueryAsync(connectionString, $"ALTER TABLE {tableName} DROP CONSTRAINT IF EXISTS {constraintName};");

    private static async Task DropTriggerIfExistsAsync(string connectionString, string tableName, string triggerName)
        => await ExecuteNonQueryAsync(connectionString, $"DROP TRIGGER IF EXISTS {triggerName} ON public.{tableName};");

    private static async Task DropFunctionIfExistsAsync(string connectionString, string functionName)
        => await ExecuteNonQueryAsync(connectionString, $"DROP FUNCTION IF EXISTS public.{functionName}();");

    private static async Task CreateOrReplaceNoopTriggerFunctionAsync(string connectionString, string functionName)
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

        await ExecuteNonQueryAsync(connectionString, sql);
    }

    private static async Task CreateTriggerAsync(
        string connectionString,
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

        await ExecuteNonQueryAsync(connectionString, sql);
    }

    private static async Task ExecuteNonQueryAsync(string connectionString, string sql)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}

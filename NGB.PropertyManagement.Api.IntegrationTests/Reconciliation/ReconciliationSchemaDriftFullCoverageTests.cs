using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Contracts.Payables;
using NGB.PropertyManagement.Contracts.Receivables;
using NGB.PropertyManagement.Payables;
using NGB.PropertyManagement.PostgreSql.Payables;
using NGB.PropertyManagement.PostgreSql.Receivables;
using NGB.PropertyManagement.PostgreSql.Reporting;
using NGB.PropertyManagement.Receivables;
using NGB.PropertyManagement.Reporting;
using NGB.PropertyManagement.Runtime;
using NGB.PostgreSql.OperationalRegisters;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Reconciliation;

[Collection(PmSchemaIntegrationCollection.Name)]
public sealed class ReconciliationSchemaDriftFullCoverageTests(PmSchemaIntegrationFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Reconciliation_rejects_policy_register_references_that_no_longer_resolve()
    {
        var factory = new PmApiFactory(fixture);
        try
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var defaults = await scope.ServiceProvider
                .GetRequiredService<IPropertyManagementSetupService>()
                .EnsureDefaultsAsync(CancellationToken.None);

            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            var constraintNames = (await connection.QueryAsync<string>("""
                SELECT DISTINCT constraint_row.conname
                FROM pg_constraint constraint_row
                JOIN unnest(constraint_row.conkey) AS key_column(attnum) ON TRUE
                JOIN pg_attribute attribute_row
                  ON attribute_row.attrelid = constraint_row.conrelid
                 AND attribute_row.attnum = key_column.attnum
                WHERE constraint_row.conrelid = 'cat_pm_accounting_policy'::regclass
                  AND constraint_row.contype = 'f'
                  AND attribute_row.attname = ANY(@Columns);
                """, new
                {
                    Columns = new[]
                    {
                        "receivables_open_items_register_id",
                        "payables_open_items_register_id"
                    }
                })).AsList();

            constraintNames.Should().HaveCount(2);
            foreach (var constraintName in constraintNames)
            {
                var quotedName = $"\"{constraintName.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
                await connection.ExecuteAsync(
                    $"ALTER TABLE cat_pm_accounting_policy DROP CONSTRAINT {quotedName};");
            }

            var missingReceivablesRegisterId = Guid.NewGuid();
            var missingPayablesRegisterId = Guid.NewGuid();
            await connection.ExecuteAsync("""
                UPDATE cat_pm_accounting_policy
                SET receivables_open_items_register_id = @MissingReceivablesRegisterId,
                    payables_open_items_register_id = @MissingPayablesRegisterId
                WHERE catalog_id = @PolicyId;
                """, new
                {
                    MissingReceivablesRegisterId = missingReceivablesRegisterId,
                    MissingPayablesRegisterId = missingPayablesRegisterId,
                    PolicyId = defaults.AccountingPolicyCatalogId
                });

            var requestMonth = new DateOnly(2026, 2, 1);
            var receivables = (PostgresReceivablesReconciliationService)scope.ServiceProvider
                .GetRequiredService<IReceivablesReconciliationService>();
            var payables = (PostgresPayablesReconciliationService)scope.ServiceProvider
                .GetRequiredService<IPayablesReconciliationService>();

            Func<Task> readReceivables = () => receivables.GetAsync(
                new ReceivablesReconciliationRequest(requestMonth, requestMonth));
            Func<Task> readPayables = () => payables.GetAsync(
                new PayablesReconciliationRequest(requestMonth, requestMonth));

            await readReceivables.Should().ThrowAsync<NgbConfigurationViolationException>()
                .WithMessage("*Receivables open-items operational register does not exist*");
            await readPayables.Should().ThrowAsync<NgbConfigurationViolationException>()
                .WithMessage("*Payables open-items operational register does not exist*");
        }
        finally
        {
            await factory.DisposeAsync();
            factory.Dispose();
        }
    }

    [Fact]
    public async Task Receivables_report_reader_handles_physical_schema_absence_and_rejects_invalid_metadata()
    {
        var factory = new PmApiFactory(fixture);
        try
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var defaults = await scope.ServiceProvider
                .GetRequiredService<IPropertyManagementSetupService>()
                .EnsureDefaultsAsync(CancellationToken.None);
            var registerId = defaults.ReceivablesOpenItemsOperationalRegisterId;
            var leaseId = Guid.NewGuid();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            var tableCode = await connection.QuerySingleAsync<string>("""
                SELECT table_code
                FROM operational_registers
                WHERE register_id = @RegisterId;
                """, new { RegisterId = registerId });
            var movementsTable = OperationalRegisterNaming.MovementsTable(tableCode);
            var balancesTable = OperationalRegisterNaming.BalancesTable(tableCode);

            await connection.ExecuteAsync($"DROP TABLE {QuoteIdentifier(balancesTable)};");
            var movementBacked = new PostgresReceivablesReportReader(
                uow,
                new OperationalRegisterReadContextCache(TimeProvider.System));
            var movementPage = await movementBacked.GetPageAsync(
                registerId,
                leaseId,
                ReceivablesReportMode.OpenItemsDetails,
                offset: 0,
                limit: int.MaxValue);
            movementPage.Rows.Should().BeEmpty();

            await connection.ExecuteAsync($"DROP TABLE {QuoteIdentifier(movementsTable)};");
            var missingMovements = new PostgresReceivablesReportReader(
                uow,
                new OperationalRegisterReadContextCache(TimeProvider.System));
            var missingMovementsPage = await missingMovements.GetPageAsync(
                registerId,
                leaseId,
                ReceivablesReportMode.Aging,
                offset: int.MaxValue,
                limit: 1);
            missingMovementsPage.Should().Be(new ReceivablesReportPage([], 0, 0m, 0m, 0m, null, null, null));

            var unknownRegisterReader = new PostgresReceivablesReportReader(
                uow,
                new OperationalRegisterReadContextCache(TimeProvider.System));
            await ((Func<Task>)(() => unknownRegisterReader.GetPageAsync(
                    Guid.NewGuid(),
                    leaseId,
                    ReceivablesReportMode.OpenItemsDetails,
                    offset: 0,
                    limit: 1)))
                .Should().ThrowAsync<NGB.OperationalRegisters.Exceptions.OperationalRegisterNotFoundException>();

            await connection.ExecuteAsync("""
                DELETE FROM operational_register_resources
                WHERE register_id = @RegisterId
                  AND column_code = 'amount';
                """, new { RegisterId = registerId });
            var missingResourceReader = new PostgresReceivablesReportReader(
                uow,
                new OperationalRegisterReadContextCache(TimeProvider.System));
            await ((Func<Task>)(() => missingResourceReader.GetPageAsync(
                    registerId,
                    leaseId,
                    ReceivablesReportMode.OpenItemsDetails,
                    offset: 0,
                    limit: 1)))
                .Should().ThrowAsync<NgbConfigurationViolationException>()
                .WithMessage("*does not define resource column 'amount'*");
        }
        finally
        {
            await factory.DisposeAsync();
            factory.Dispose();
        }
    }

    [Fact]
    public void Receivables_report_cursor_keys_cover_charge_credit_and_missing_date_boundaries()
    {
        PostgresReceivablesReportReader.GetNextKindOrder(chargesOnly: true, netAmount: -1m)
            .Should().Be(0);
        PostgresReceivablesReportReader.GetNextKindOrder(chargesOnly: false, netAmount: 1m)
            .Should().Be(0);
        PostgresReceivablesReportReader.GetNextKindOrder(chargesOnly: false, netAmount: 0m)
            .Should().Be(1);

        var dueOnUtc = new DateOnly(2026, 8, 1);
        var receivedOnUtc = new DateOnly(2026, 8, 2);
        PostgresReceivablesReportReader.GetNextSortDate(dueOnUtc, receivedOnUtc)
            .Should().Be(dueOnUtc);
        PostgresReceivablesReportReader.GetNextSortDate(null, receivedOnUtc)
            .Should().Be(receivedOnUtc);
        PostgresReceivablesReportReader.GetNextSortDate(null, null)
            .Should().Be(DateOnly.MaxValue);
    }

    private static string QuoteIdentifier(string identifier)
        => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}

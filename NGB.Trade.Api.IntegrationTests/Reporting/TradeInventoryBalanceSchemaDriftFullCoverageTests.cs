using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.OperationalRegisters;
using NGB.PostgreSql.Schema;
using NGB.ReferenceRegisters;
using NGB.Tools.Exceptions;
using NGB.Trade.Api.IntegrationTests.Infrastructure;
using NGB.Trade.PostgreSql.Reporting;
using NGB.Trade.Reporting;
using NGB.Trade.Runtime;
using Npgsql;
using Xunit;

namespace NGB.Trade.Api.IntegrationTests.Reporting;

[Collection(TradeSchemaPostgresCollection.Name)]
public sealed class TradeInventoryBalanceSchemaDriftFullCoverageTests(TradeSchemaPostgresFixture fixture)
    : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Reader_handles_missing_balance_and_movement_tables_and_rejects_missing_quantity_resource()
    {
        using var host = TradeHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ITradeSetupService>()
            .EnsureDefaultsAsync(CancellationToken.None);

        var registerId = OperationalRegisterId.FromCode(TradeCodes.InventoryMovementsRegisterCode);
        await using var schemaConnection = new NpgsqlConnection(fixture.ConnectionString);
        await schemaConnection.OpenAsync();
        var tableCode = await schemaConnection.QuerySingleAsync<string>("""
            SELECT table_code
            FROM operational_registers
            WHERE register_id = @RegisterId;
            """, new { RegisterId = registerId });
        var movementsTable = OperationalRegisterNaming.MovementsTable(tableCode);
        var balancesTable = OperationalRegisterNaming.BalancesTable(tableCode);

        await schemaConnection.ExecuteAsync($"DROP TABLE {QuoteIdentifier(balancesTable)};");
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var movementBacked = new PostgresTradeInventoryBalanceReader(
            uow,
            new OperationalRegisterReadContextCache(TimeProvider.System));

        var movementPage = await movementBacked.GetPageAsync(
            registerId,
            DateOnly.MaxValue,
            null,
            null,
            TradeInventoryBalanceSort.ItemWarehouse,
            offset: 0,
            limit: 10);
        movementPage.Rows.Should().BeEmpty();

        await schemaConnection.ExecuteAsync($"DROP TABLE {QuoteIdentifier(movementsTable)};");
        var missingMovements = new PostgresTradeInventoryBalanceReader(
            uow,
            new OperationalRegisterReadContextCache(TimeProvider.System));
        var missingMovementsPage = await missingMovements.GetPageAsync(
            registerId,
            new DateOnly(2026, 8, 1),
            null,
            null,
            TradeInventoryBalanceSort.ItemWarehouse,
            offset: 0,
            limit: 10);
        missingMovementsPage.Should().Be(new TradeInventoryBalancePage([], 0, 0m));

        await schemaConnection.ExecuteAsync("""
            DELETE FROM operational_register_resources
            WHERE register_id = @RegisterId
              AND column_code = 'qty_delta';
            """, new { RegisterId = registerId });
        var missingResource = new PostgresTradeInventoryBalanceReader(
            uow,
            new OperationalRegisterReadContextCache(TimeProvider.System));

        await ((Func<Task>)(() => missingResource.GetPageAsync(
                registerId,
                new DateOnly(2026, 8, 1),
                null,
                null,
                TradeInventoryBalanceSort.ItemWarehouse,
                offset: 0,
                limit: 10)))
            .Should().ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage("*does not define resource column 'qty_delta'*");
    }

    [Fact]
    public async Task Current_item_price_reader_validates_paging_and_handles_missing_physical_register_table()
    {
        using var host = TradeHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ITradeSetupService>()
            .EnsureDefaultsAsync(CancellationToken.None);

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var reader = new PostgresTradeCurrentItemPriceReader(
            uow,
            new PostgresRelationPresenceCache(TimeProvider.System));
        var asOfUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);

        await ((Func<Task>)(() => reader.GetPageAsync(asOfUtc, null, null, -1, 1)))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => reader.GetPageAsync(asOfUtc, null, null, 0, 0)))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();

        var recordsTable = ReferenceRegisterNaming.RecordsTable(TradeCodes.ItemPricesRegisterCode);
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync($"DROP TABLE {QuoteIdentifier(recordsTable)};");

        var page = await reader.GetPageAsync(
            asOfUtc,
            [Guid.Empty, Guid.Empty],
            [Guid.Empty],
            offset: int.MaxValue,
            limit: int.MaxValue);
        page.Should().Be(new TradeCurrentItemPricePage([], 0));
    }

    private static string QuoteIdentifier(string identifier)
        => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}

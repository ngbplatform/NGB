using FluentAssertions;
using NGB.Accounting.PostingState;
using NGB.Trade.Api.IntegrationTests.Infrastructure;
using NGB.Trade.Migrator.Seed;
using Npgsql;
using Xunit;

namespace NGB.Trade.Api.IntegrationTests.Seed;

[Collection(TradePostgresCollection.Name)]
public sealed class TradeMigrator_SeedDemo_Smoke_P0Tests(TradePostgresFixture fixture) : IAsyncLifetime
{
    private static readonly TimeProvider Frozen2026Clock =
        new FixedTimeProvider(new DateTimeOffset(2026, 4, 11, 15, 0, 0, TimeSpan.Zero));

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SeedDemo_WithExplicitCounts_CreatesHistoricalTradeData_And_ClosesPeriods()
    {
        var args = new[]
        {
            "--connection", fixture.ConnectionString,
            "--seed", "123456",
            "--from", "2024-05-01",
            "--to", "2026-12-31",
            "--warehouses", "3",
            "--customers", "8",
            "--vendors", "5",
            "--items", "12",
            "--price-updates", "6",
            "--purchase-receipts", "15",
            "--sales-invoices", "18",
            "--customer-payments", "10",
            "--vendor-payments", "9",
            "--inventory-transfers", "6",
            "--inventory-adjustments", "5",
            "--customer-returns", "4",
            "--vendor-returns", "3",
            "--close-periods", "true"
        };

        var exit = await TradeSeedDemoCli.RunAsync(args, Frozen2026Clock);

        exit.Should().Be(0);

        await using var conn = new NpgsqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        (await CountAsync(conn, "select count(*) from cat_trd_warehouse;")).Should().Be(3);
        (await CountAsync(conn, "select count(*) from cat_trd_item;")).Should().Be(12);
        (await CountAsync(conn, "select count(*) from cat_trd_party where is_customer = true and is_vendor = false;")).Should().Be(8);
        (await CountAsync(conn, "select count(*) from cat_trd_party where is_vendor = true and is_customer = false;")).Should().Be(5);

        await AssertPostedDocCountAsync(conn, TradeCodes.ItemPriceUpdate, 6);
        await AssertPostedDocCountAsync(conn, TradeCodes.PurchaseReceipt, 15);
        await AssertPostedDocCountAsync(conn, TradeCodes.SalesInvoice, 18);
        await AssertPostedDocCountAsync(conn, TradeCodes.CustomerPayment, 10);
        await AssertPostedDocCountAsync(conn, TradeCodes.VendorPayment, 9);
        await AssertPostedDocCountAsync(conn, TradeCodes.InventoryTransfer, 6);
        await AssertPostedDocCountAsync(conn, TradeCodes.InventoryAdjustment, 5);
        await AssertPostedDocCountAsync(conn, TradeCodes.CustomerReturn, 4);
        await AssertPostedDocCountAsync(conn, TradeCodes.VendorReturn, 3);

        (await CountAsync(
            conn,
            """
            select count(*)
              from cat_trd_party
             where display ilike '%demo%'
                or name ilike '%demo%'
                or coalesce(notes, '') ilike '%demo%';
            """)).Should().Be(0);

        (await CountAsync(
            conn,
            """
            select count(*)
              from cat_trd_item
             where display ilike '%demo%'
                or name ilike '%demo%'
                or coalesce(notes, '') ilike '%demo%';
            """)).Should().Be(0);

        (await CountAsync(
            conn,
            """
            select count(*)
              from (
                    select coalesce(notes, '') as note from doc_trd_purchase_receipt
                    union all
                    select coalesce(notes, '') from doc_trd_sales_invoice
                    union all
                    select coalesce(notes, '') from doc_trd_customer_payment
                    union all
                    select coalesce(notes, '') from doc_trd_vendor_payment
                    union all
                    select coalesce(notes, '') from doc_trd_inventory_transfer
                    union all
                    select coalesce(notes, '') from doc_trd_inventory_adjustment
                    union all
                    select coalesce(notes, '') from doc_trd_customer_return
                    union all
                    select coalesce(notes, '') from doc_trd_vendor_return
                    union all
                    select coalesce(notes, '') from doc_trd_item_price_update
                ) n
             where note ilike '%demo%';
            """)).Should().Be(0);

        var minDate = await ScalarDateAsync(conn, "select min(date_utc) from documents where type_code like 'trd.%';");
        var maxDate = await ScalarDateAsync(conn, "select max(date_utc) from documents where type_code like 'trd.%';");

        minDate.Should().BeOnOrAfter(new DateOnly(2024, 5, 1));
        maxDate.Should().BeOnOrBefore(new DateOnly(2026, 12, 31));

        (await CountAsync(conn, "select count(*) from accounting_closed_periods;")).Should().Be(24);

        var fiscalYearClosings = await CountAsync(
            conn,
            """
            select count(*)
              from accounting_posting_state
             where operation = @operation
               and completed_at_utc is not null;
            """,
            new Dictionary<string, object?>
            {
                ["operation"] = (short)PostingOperation.CloseFiscalYear
            });

        fiscalYearClosings.Should().Be(2);
    }

    [Fact]
    public async Task SeedDemo_Rejects_Second_Run_When_Trade_Activity_Already_Exists()
    {
        var args = new[]
        {
            "--connection", fixture.ConnectionString,
            "--seed", "654321",
            "--from", "2024-01-01",
            "--to", "2024-12-31",
            "--warehouses", "2",
            "--customers", "4",
            "--vendors", "3",
            "--items", "6",
            "--price-updates", "2",
            "--purchase-receipts", "4",
            "--sales-invoices", "5",
            "--customer-payments", "3",
            "--vendor-payments", "3",
            "--inventory-transfers", "2",
            "--inventory-adjustments", "2",
            "--customer-returns", "1",
            "--vendor-returns", "1",
            "--close-periods", "false"
        };

        var exit1 = await TradeSeedDemoCli.RunAsync(args, Frozen2026Clock);
        var exit2 = await TradeSeedDemoCli.RunAsync(args, Frozen2026Clock);

        exit1.Should().Be(0);
        exit2.Should().Be(1);
    }

    private static async Task AssertPostedDocCountAsync(NpgsqlConnection conn, string typeCode, int expected)
    {
        var count = await CountAsync(
            conn,
            "select count(*) from documents where type_code = @typeCode and status = 2;",
            new Dictionary<string, object?> { ["typeCode"] = typeCode });

        count.Should().Be(expected, $"'{typeCode}' should be seeded with the requested exact count");
    }

    private static async Task<int> CountAsync(
        NpgsqlConnection conn,
        string sql,
        IReadOnlyDictionary<string, object?>? parameters = null)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParameters(cmd, parameters);
        return Convert.ToInt32((await cmd.ExecuteScalarAsync())!);
    }

    private static async Task<DateOnly> ScalarDateAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        var value = (DateTime)(await cmd.ExecuteScalarAsync())!;
        return DateOnly.FromDateTime(value);
    }

    private static void AddParameters(NpgsqlCommand cmd, IReadOnlyDictionary<string, object?>? parameters)
    {
        if (parameters is null)
            return;

        foreach (var pair in parameters)
            cmd.Parameters.AddWithValue(pair.Key, pair.Value ?? DBNull.Value);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}

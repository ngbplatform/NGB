using Dapper;
using FluentAssertions;
using NGB.Accounting.PostingState;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Migrator.Seed;
using Npgsql;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Admin;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmMigrator_SeedDemo_Smoke_P0Tests(PmIntegrationFixture fixture)
{
    private static readonly TimeProvider Frozen2026Clock =
        new FixedTimeProvider(new DateTimeOffset(2026, 4, 5, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task SeedDemo_AfterCleanPackMigrate_CreatesHistoricalData_AndRejectsDuplicateDataset()
    {
        await using var db = await TemporaryDatabase.CreateAsync(fixture.ConnectionString, "ngb_pm_seed_demo");

        await PmMigrationSet.ApplyPlatformAndPmMigrationsAsync(db.ConnectionString);

        var args = new[]
        {
            "--connection", db.ConnectionString,
            "--dataset", "smoke",
            "--seed", "123456",
            "--from", "2024-01-01",
            "--to", "2024-12-31",
            "--buildings", "3",
            "--units-min", "4",
            "--units-max", "5",
            "--tenants", "10",
            "--vendors", "4",
            "--occupancy-rate", "0.80"
        };

        var exit1 = await PropertyManagementSeedDemoCli.RunAsync(args, Frozen2026Clock);
        var exit2 = await PropertyManagementSeedDemoCli.RunAsync(args, Frozen2026Clock);

        exit1.Should().Be(0);
        exit2.Should().Be(1, "the same dataset code must not be seeded twice into the same database");

        await using var conn = new NpgsqlConnection(db.ConnectionString);
        await conn.OpenAsync();

        const string marker = "Dataset smoke";

        (await conn.ExecuteScalarAsync<int>(
            "select count(*) from cat_pm_property where kind = 'Building' and address_line2 = @marker;",
            new { marker = marker }))
            .Should().Be(3);

        (await conn.ExecuteScalarAsync<int>(
            """
            select count(*)
            from cat_pm_property u
            join cat_pm_property b on b.catalog_id = u.parent_property_id
            where u.kind = 'Unit'
              and b.kind = 'Building'
              and b.address_line2 = @marker;
            """,
            new { marker = marker }))
            .Should().BeGreaterThanOrEqualTo(12);

        (await conn.ExecuteScalarAsync<int>(
            "select count(*) from cat_pm_party where is_tenant = true and coalesce(is_vendor, false) = false;"))
            .Should().Be(10);

        (await conn.ExecuteScalarAsync<int>(
            "select count(*) from cat_pm_party where is_vendor = true and coalesce(is_tenant, false) = false;"))
            .Should().Be(4);

        (await conn.ExecuteScalarAsync<int>(
            "select count(*) from cat_pm_bank_account;"))
            .Should().BeGreaterThanOrEqualTo(4);

        (await conn.ExecuteScalarAsync<int>(
            """
            select count(*)
            from accounting_accounts
            where code in ('1010', '1020', '1030')
              and cash_flow_role = 1
              and cash_flow_line_code is null;
            """))
            .Should().Be(3);

        (await conn.ExecuteScalarAsync<int>(
            """
            select count(*)
            from (
                select display
                from cat_pm_party
                where is_tenant = true
                   or is_vendor = true
                group by display
                having count(*) > 1
            ) dup;
            """))
            .Should().Be(0);

        (await conn.ExecuteScalarAsync<int>(
            """
            select count(*)
              from cat_pm_party
             where email like '_.%@ngbplatform.com';
            """))
            .Should().Be(14);

        (await conn.ExecuteScalarAsync<int>(
            """
            select count(*)
            from (
                select email
                  from cat_pm_party
                 where is_tenant = true
                    or is_vendor = true
                 group by email
                having count(*) > 1
            ) dup;
            """))
            .Should().Be(0);

        await AssertPostedDocCountAsync(conn, PropertyManagementCodes.Lease, minimum: 1);
        await AssertPostedDocCountAsync(conn, PropertyManagementCodes.RentCharge, minimum: 1);
        await AssertPostedDocCountAsync(conn, PropertyManagementCodes.ReceivableCreditMemo, minimum: 1);
        await AssertPostedDocCountAsync(conn, PropertyManagementCodes.ReceivablePayment, minimum: 1);
        await AssertPostedDocCountAsync(conn, PropertyManagementCodes.ReceivableReturnedPayment, minimum: 1);
        await AssertPostedDocCountAsync(conn, PropertyManagementCodes.ReceivableApply, minimum: 1);
        await AssertPostedDocCountAsync(conn, PropertyManagementCodes.PayableCharge, minimum: 1);
        await AssertPostedDocCountAsync(conn, PropertyManagementCodes.PayableCreditMemo, minimum: 1);
        await AssertPostedDocCountAsync(conn, PropertyManagementCodes.PayablePayment, minimum: 1);
        await AssertPostedDocCountAsync(conn, PropertyManagementCodes.PayableApply, minimum: 1);
        await AssertPostedDocCountAsync(conn, PropertyManagementCodes.MaintenanceRequest, minimum: 1);
        await AssertPostedDocCountAsync(conn, PropertyManagementCodes.WorkOrder, minimum: 1);
        await AssertPostedDocCountAsync(conn, PropertyManagementCodes.WorkOrderCompletion, minimum: 1);

        (await conn.ExecuteScalarAsync<int>("select count(*) from doc_pm_lease__parties;"))
            .Should().BeGreaterThan(0);

        (await conn.ExecuteScalarAsync<int>("select count(*) from accounting_register_main;"))
            .Should().BeGreaterThan(0);

        (await conn.ExecuteScalarAsync<int>("select count(*) from accounting_closed_periods;"))
            .Should().Be(12);

        (await conn.ExecuteScalarAsync<int>("select count(*) from document_relationships;"))
            .Should().BeGreaterThan(0);

        var minDate = await conn.ExecuteScalarAsync<DateTime>(
            "select min(date_utc) from documents where type_code like 'pm.%';");
        var maxDate = await conn.ExecuteScalarAsync<DateTime>(
            "select max(date_utc) from documents where type_code like 'pm.%';");

        DateOnly.FromDateTime(minDate).Should().BeOnOrAfter(new DateOnly(2024, 1, 1));
        DateOnly.FromDateTime(maxDate).Should().BeOnOrBefore(new DateOnly(2024, 12, 31));
    }

    [Fact]
    public async Task SeedDemo_MidYearRangeAcrossFiscalYearEnd_Succeeds_AndClosesPrerequisiteMonths()
    {
        await using var db = await TemporaryDatabase.CreateAsync(fixture.ConnectionString, "ngb_pm_seed_demo_midyear");

        await PmMigrationSet.ApplyPlatformAndPmMigrationsAsync(db.ConnectionString);

        var args = new[]
        {
            "--connection", db.ConnectionString,
            "--dataset", "midyear",
            "--seed", "654321",
            "--from", "2025-05-01",
            "--to", "2026-12-31",
            "--buildings", "1",
            "--units-min", "2",
            "--units-max", "2",
            "--tenants", "2",
            "--vendors", "2",
            "--occupancy-rate", "1.00"
        };

        var exit = await PropertyManagementSeedDemoCli.RunAsync(args, Frozen2026Clock);

        exit.Should().Be(0);

        await using var conn = new NpgsqlConnection(db.ConnectionString);
        await conn.OpenAsync();

        (await conn.ExecuteScalarAsync<int>("select count(*) from accounting_closed_periods;"))
            .Should().Be(12);

        (await conn.ExecuteScalarAsync<int>(
            """
            select count(*)
              from accounting_closed_periods
             where period >= date '2026-01-01';
            """))
            .Should().Be(0);

        var fiscalYearClosings = await conn.ExecuteScalarAsync<int>(
            """
            select count(*)
              from accounting_posting_state
             where operation = @operation
               and completed_at_utc is not null;
            """,
            new { operation = (short)PostingOperation.CloseFiscalYear });

        fiscalYearClosings.Should().Be(1);

        var minDate = await conn.ExecuteScalarAsync<DateTime>(
            "select min(date_utc) from documents where type_code like 'pm.%';");
        var maxDate = await conn.ExecuteScalarAsync<DateTime>(
            "select max(date_utc) from documents where type_code like 'pm.%';");

        DateOnly.FromDateTime(minDate).Should().BeOnOrAfter(new DateOnly(2025, 5, 1));
        DateOnly.FromDateTime(maxDate).Should().BeOnOrBefore(new DateOnly(2026, 12, 31));
    }

    private static async Task AssertPostedDocCountAsync(NpgsqlConnection conn, string typeCode, int minimum)
    {
        var count = await conn.ExecuteScalarAsync<int>(
            "select count(*) from documents where type_code = @typeCode and status = 2;",
            new { typeCode });

        count.Should().BeGreaterThanOrEqualTo(minimum, $"'{typeCode}' demo documents should be posted");
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}

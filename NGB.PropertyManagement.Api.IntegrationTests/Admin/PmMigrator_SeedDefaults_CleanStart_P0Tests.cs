using Dapper;
using FluentAssertions;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Migrator.Seed;
using Npgsql;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Admin;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmMigrator_SeedDefaults_CleanStart_P0Tests(PmIntegrationFixture fixture)
{
    [Fact]
    public async Task SeedDefaults_AfterCleanPackMigrate_IsIdempotent_AndCreatesExpectedPmDefaults()
    {
        await using var db = await TemporaryDatabase.CreateAsync(fixture.ConnectionString, "ngb_pm_seed");

        await PmMigrationSet.ApplyPlatformAndPmMigrationsAsync(db.ConnectionString);

        var exit1 = await PropertyManagementSeedDefaultsCli.RunAsync(["--connection", db.ConnectionString]);
        var exit2 = await PropertyManagementSeedDefaultsCli.RunAsync(["--connection", db.ConnectionString]);

        exit1.Should().Be(0);
        exit2.Should().Be(0);

        await using var conn = new NpgsqlConnection(db.ConnectionString);
        await conn.OpenAsync();

        var criticalTables = new[]
        {
            "cat_pm_party",
            "cat_pm_property",
            "cat_pm_accounting_policy",
            "cat_pm_receivable_charge_type",
            "cat_pm_payable_charge_type",
            "doc_pm_lease",
            "doc_pm_lease__parties",
            "doc_pm_rent_charge",
            "doc_pm_receivable_charge",
            "doc_pm_receivable_payment",
            "doc_pm_payable_charge",
            "doc_pm_payable_payment",
            "doc_pm_receivable_apply"
        };

        foreach (var table in criticalTables)
        {
            var exists = await conn.ExecuteScalarAsync<bool>($"SELECT to_regclass('public.{table}') IS NOT NULL;");
            exists.Should().BeTrue($"table public.{table} should exist after clean PM baseline migrate");
        }

        (await conn.ExecuteScalarAsync<bool>("SELECT to_regclass('public.migration_changelog__platform') IS NOT NULL;"))
            .Should().BeTrue();

        (await conn.ExecuteScalarAsync<bool>("SELECT to_regclass('public.migration_changelog__pm') IS NOT NULL;"))
            .Should().BeTrue();

        foreach (var code in new[] { "1000", "1100", "2000", "4000", "4010", "4020", "4030", "4040", "4050", "4100", "5100", "5200", "5300", "5400", "5990" })
        {
            (await conn.ExecuteScalarAsync<int>("select count(*) from accounting_accounts where code = @code;", new { code }))
                .Should().Be(1, $"CoA account {code} must be seeded exactly once");
        }

        (await conn.ExecuteScalarAsync<int>(
                "select count(*) from operational_registers where code = @code;",
                new { code = PropertyManagementCodes.TenantBalancesRegisterCode }))
            .Should().Be(1);

        (await conn.ExecuteScalarAsync<int>(
                "select count(*) from operational_registers where code = @code;",
                new { code = PropertyManagementCodes.ReceivablesOpenItemsRegisterCode }))
            .Should().Be(1);

        (await conn.ExecuteScalarAsync<int>(
                "select count(*) from catalogs where catalog_code = @code;",
                new { code = PropertyManagementCodes.AccountingPolicy }))
            .Should().Be(1);

        (await conn.ExecuteScalarAsync<int>("select count(*) from cat_pm_accounting_policy;"))
            .Should().Be(1);

        (await conn.ExecuteScalarAsync<int>(
                "select count(*) from catalogs where catalog_code = @code;",
                new { code = PropertyManagementCodes.ReceivableChargeType }))
            .Should().Be(7);

        (await conn.ExecuteScalarAsync<int>("select count(*) from cat_pm_receivable_charge_type;"))
            .Should().Be(7);

        foreach (var display in new[] { "Rent", "Late Fee", "Utility", "Parking", "Damage", "Move out", "Misc" })
        {
            (await conn.ExecuteScalarAsync<int>("select count(*) from cat_pm_receivable_charge_type where display = @display;", new { display }))
                .Should().Be(1, $"Charge type '{display}' must be seeded exactly once");
        }

        foreach (var display in new[] { "Repair", "Utility", "Cleaning", "Supply", "Misc" })
        {
            (await conn.ExecuteScalarAsync<int>("select count(*) from cat_pm_payable_charge_type where display = @display;", new { display }))
                .Should().Be(1, $"Payable charge type '{display}' must be seeded exactly once");
        }

        (await conn.ExecuteScalarAsync<int>(
                "select count(*) from operational_registers where code = @code;",
                new { code = PropertyManagementCodes.PayablesOpenItemsRegisterCode }))
            .Should().Be(1);
    }
}

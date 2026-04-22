using System.Net;
using System.Net.Http.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NGB.PostgreSql.DependencyInjection;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Contracts;
using Npgsql;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Admin;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmSetup_ApplyDefaults_Idempotency_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmSetup_ApplyDefaults_Idempotency_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ApplyDefaults_Twice_IsIdempotent_AndDoesNotCreateDuplicates()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            // PM API enables HTTPS redirection. Using https scheme avoids redirects in TestServer.
            BaseAddress = new Uri("https://localhost")
        });

        var r1 = await ApplyDefaultsAsync(client);
        var r2 = await ApplyDefaultsAsync(client);

        // ApplyDefaults is idempotent:
        // - IDs must be stable
        // - the second call must not report any new creations
        r2.AccountingPolicyCatalogId.Should().Be(r1.AccountingPolicyCatalogId);
        r2.TenantBalancesOperationalRegisterId.Should().Be(r1.TenantBalancesOperationalRegisterId);
        r2.ReceivablesOpenItemsOperationalRegisterId.Should().Be(r1.ReceivablesOpenItemsOperationalRegisterId);
        r2.PayablesOpenItemsOperationalRegisterId.Should().Be(r1.PayablesOpenItemsOperationalRegisterId);
        r2.CashAccountId.Should().Be(r1.CashAccountId);
        r2.DefaultBankAccountCatalogId.Should().Be(r1.DefaultBankAccountCatalogId);
        r2.AccountsReceivableTenantsAccountId.Should().Be(r1.AccountsReceivableTenantsAccountId);
        r2.AccountsPayableVendorsAccountId.Should().Be(r1.AccountsPayableVendorsAccountId);
        r2.RentalIncomeAccountId.Should().Be(r1.RentalIncomeAccountId);
        r2.LateFeeIncomeAccountId.Should().Be(r1.LateFeeIncomeAccountId);

        r2.CreatedAccountingPolicy.Should().BeFalse();
        r2.CreatedTenantBalancesOperationalRegister.Should().BeFalse();
        r2.CreatedReceivablesOpenItemsOperationalRegister.Should().BeFalse();
        r2.CreatedPayablesOpenItemsOperationalRegister.Should().BeFalse();
        r2.CreatedCashAccount.Should().BeFalse();
        r2.CreatedDefaultBankAccount.Should().BeFalse();

        r2.CreatedAccountsReceivableTenants.Should().BeFalse();
        r2.CreatedAccountsPayableVendors.Should().BeFalse();
        r2.CreatedRentalIncome.Should().BeFalse();
        r2.CreatedLateFeeIncome.Should().BeFalse();

        // Basic sanity: IDs must be real.
        r1.AccountingPolicyCatalogId.Should().NotBe(Guid.Empty);
        r1.DefaultBankAccountCatalogId.Should().NotBe(Guid.Empty);
        r1.TenantBalancesOperationalRegisterId.Should().NotBe(Guid.Empty);
        r1.ReceivablesOpenItemsOperationalRegisterId.Should().NotBe(Guid.Empty);
        r1.PayablesOpenItemsOperationalRegisterId.Should().NotBe(Guid.Empty);
        r1.AccountsReceivableTenantsAccountId.Should().NotBe(Guid.Empty);
        r1.AccountsPayableVendorsAccountId.Should().NotBe(Guid.Empty);
        r1.RentalIncomeAccountId.Should().NotBe(Guid.Empty);
        r1.LateFeeIncomeAccountId.Should().NotBe(Guid.Empty);

        // Guard against accidental misconfiguration in WebApplicationFactory overrides.
        await using var scope = factory.Services.CreateAsyncScope();
        var cfg = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var cs = cfg.GetConnectionString("DefaultConnection");
        cs.Should().NotBeNullOrWhiteSpace();
        cs.Should().Be(_fixture.ConnectionString, "PM API must run against the same Testcontainers database as the fixture");

        // IMPORTANT: Program.cs passes the connection string into AddNgbPostgres() at startup.
        // Ensure the effective PostgresOptions match the fixture too (otherwise the app may talk to a different DB).
        var pg = scope.ServiceProvider.GetRequiredService<IOptions<PostgresOptions>>().Value;
        pg.ConnectionString.Should().Be(_fixture.ConnectionString);

        await using var conn = new NpgsqlConnection(pg.ConnectionString);
        await conn.OpenAsync();

        (await conn.ExecuteScalarAsync<int>(
                "select count(*) from catalogs where catalog_code = @code;",
                new { code = PropertyManagementCodes.AccountingPolicy }))
            .Should().Be(1);

        (await conn.ExecuteScalarAsync<int>("select count(*) from cat_pm_accounting_policy;"))
            .Should().Be(1);

        (await conn.ExecuteScalarAsync<int>("select count(*) from cat_pm_bank_account;"))
            .Should().Be(1);

        (await conn.ExecuteScalarAsync<int>("select count(*) from accounting_accounts where code = '1000';"))
            .Should().Be(1);

        (await conn.ExecuteScalarAsync<int>("select count(*) from accounting_accounts where code = '1100';"))
            .Should().Be(1);

        (await conn.ExecuteScalarAsync<int>("select count(*) from accounting_accounts where code = '2000';"))
            .Should().Be(1);

        (await conn.ExecuteScalarAsync<int>("select count(*) from accounting_accounts where code = '4000';"))
            .Should().Be(1);

        foreach (var code in new[] { "4010", "4020", "4030", "4040", "4050", "4100", "5100", "5200", "5300", "5400", "5990" })
        {
            (await conn.ExecuteScalarAsync<int>("select count(*) from accounting_accounts where code = @code;", new { code }))
                .Should().Be(1);
        }

        var seededChargeTypes = (await conn.QueryAsync<string>(
                "select display from cat_pm_receivable_charge_type ct join catalogs c on c.id = ct.catalog_id where c.catalog_code = @code and c.is_deleted = false order by display;",
                new { code = PropertyManagementCodes.ReceivableChargeType }))
            .ToArray();

        seededChargeTypes.Should().Equal("Damage", "Late Fee", "Misc", "Move out", "Parking", "Rent", "Utility");

        var seededPayableChargeTypes = (await conn.QueryAsync<string>(
                "select display from cat_pm_payable_charge_type ct join catalogs c on c.id = ct.catalog_id where c.catalog_code = @code and c.is_deleted = false order by display;",
                new { code = PropertyManagementCodes.PayableChargeType }))
            .ToArray();

        seededPayableChargeTypes.Should().Equal("Cleaning", "Misc", "Repair", "Supply", "Utility");

        (await conn.ExecuteScalarAsync<int>(
                "select count(*) from operational_registers where code = @code;",
                new { code = PropertyManagementCodes.TenantBalancesRegisterCode }))
            .Should().Be(1);

        (await conn.ExecuteScalarAsync<int>(
                "select count(*) from operational_registers where code = @code;",
                new { code = PropertyManagementCodes.ReceivablesOpenItemsRegisterCode }))
            .Should().Be(1);

        (await conn.ExecuteScalarAsync<int>(
                "select count(*) from operational_registers where code = @code;",
                new { code = PropertyManagementCodes.PayablesOpenItemsRegisterCode }))
            .Should().Be(1);
    }

    private static async Task<PropertyManagementSetupResult> ApplyDefaultsAsync(HttpClient client)
    {
        using var resp = await client.PostAsJsonAsync("/api/admin/setup/apply-defaults", new { });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await resp.Content.ReadFromJsonAsync<PropertyManagementSetupResult>();
        result.Should().NotBeNull();
        return result!;
    }
}

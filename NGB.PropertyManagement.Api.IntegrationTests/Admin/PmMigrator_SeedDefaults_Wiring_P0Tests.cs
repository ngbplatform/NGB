using Dapper;
using FluentAssertions;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Migrator.Seed;
using Npgsql;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Admin;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmMigrator_SeedDefaults_Wiring_P0Tests(PmIntegrationFixture fixture)
{
    [Fact]
    public async Task SeedDefaults_AfterCleanPackMigrate_WiresPolicyChargeTypesDimensions_AndOpenItemsSchema()
    {
        await using var db = await TemporaryDatabase.CreateAsync(fixture.ConnectionString, "ngb_pm_seed_wiring");

        await PmMigrationSet.ApplyPlatformAndPmMigrationsAsync(db.ConnectionString);

        var exit = await PropertyManagementSeedDefaultsCli.RunAsync(["--connection", db.ConnectionString]);
        exit.Should().Be(0);

        await using var conn = new NpgsqlConnection(db.ConnectionString);
        await conn.OpenAsync();

        var cashAccountId = await conn.ExecuteScalarAsync<Guid>("select account_id from accounting_accounts where code = '1000';");
        var arAccountId = await conn.ExecuteScalarAsync<Guid>("select account_id from accounting_accounts where code = '1100';");
        var apAccountId = await conn.ExecuteScalarAsync<Guid>("select account_id from accounting_accounts where code = '2000';");
        var rentalIncomeAccountId = await conn.ExecuteScalarAsync<Guid>("select account_id from accounting_accounts where code = '4000';");
        var utilityIncomeAccountId = await conn.ExecuteScalarAsync<Guid>("select account_id from accounting_accounts where code = '4010';");
        var parkingIncomeAccountId = await conn.ExecuteScalarAsync<Guid>("select account_id from accounting_accounts where code = '4020';");
        var damageIncomeAccountId = await conn.ExecuteScalarAsync<Guid>("select account_id from accounting_accounts where code = '4030';");
        var moveOutIncomeAccountId = await conn.ExecuteScalarAsync<Guid>("select account_id from accounting_accounts where code = '4040';");
        var miscIncomeAccountId = await conn.ExecuteScalarAsync<Guid>("select account_id from accounting_accounts where code = '4050';");
        var lateFeeIncomeAccountId = await conn.ExecuteScalarAsync<Guid>("select account_id from accounting_accounts where code = '4100';");
        var repairsExpenseAccountId = await conn.ExecuteScalarAsync<Guid>("select account_id from accounting_accounts where code = '5100';");
        var utilitiesExpenseAccountId = await conn.ExecuteScalarAsync<Guid>("select account_id from accounting_accounts where code = '5200';");
        var cleaningExpenseAccountId = await conn.ExecuteScalarAsync<Guid>("select account_id from accounting_accounts where code = '5300';");
        var suppliesExpenseAccountId = await conn.ExecuteScalarAsync<Guid>("select account_id from accounting_accounts where code = '5400';");
        var miscExpenseAccountId = await conn.ExecuteScalarAsync<Guid>("select account_id from accounting_accounts where code = '5990';");

        var cashRequiredDimCount = await conn.ExecuteScalarAsync<int>(
            "select count(*) from accounting_account_dimension_rules where account_id = @AccountId and is_required = true;",
            new { AccountId = cashAccountId });
        cashRequiredDimCount.Should().Be(0, "PM cash control account must accept DimensionBag.Empty on the cash side");

        var arDims = (await conn.QueryAsync<DimensionRuleRow>(
            """
            select d.code as Code, r.ordinal as Ordinal, r.is_required as IsRequired
            from accounting_account_dimension_rules r
            join platform_dimensions d on d.dimension_id = r.dimension_id
            where r.account_id = @AccountId
            order by r.ordinal;
            """,
            new { AccountId = arAccountId })).ToArray();

        arDims.Should().BeEquivalentTo(ExpectedPmAccountDimensions(), o => o.WithStrictOrdering());

        var incomeDims = (await conn.QueryAsync<DimensionRuleRow>(
            """
            select d.code as Code, r.ordinal as Ordinal, r.is_required as IsRequired
            from accounting_account_dimension_rules r
            join platform_dimensions d on d.dimension_id = r.dimension_id
            where r.account_id = @AccountId
            order by r.ordinal;
            """,
            new { AccountId = rentalIncomeAccountId })).ToArray();

        incomeDims.Should().BeEquivalentTo(ExpectedPmAccountDimensions(), o => o.WithStrictOrdering());

        var tenantBalances = await conn.QuerySingleAsync<RegisterRow>(
            "select register_id as RegisterId, table_code as TableCode from operational_registers where code = @Code;",
            new { Code = PropertyManagementCodes.TenantBalancesRegisterCode });

        var openItems = await conn.QuerySingleAsync<RegisterRow>(
            "select register_id as RegisterId, table_code as TableCode from operational_registers where code = @Code;",
            new { Code = PropertyManagementCodes.ReceivablesOpenItemsRegisterCode });

        var payablesOpenItems = await conn.QuerySingleAsync<RegisterRow>(
            "select register_id as RegisterId, table_code as TableCode from operational_registers where code = @Code;",
            new { Code = PropertyManagementCodes.PayablesOpenItemsRegisterCode });

        var tenantBalanceResources = (await conn.QueryAsync<ResourceRow>(
            "select code as Code, ordinal as Ordinal from operational_register_resources where register_id = @RegisterId order by ordinal;",
            new { tenantBalances.RegisterId })).ToArray();
        tenantBalanceResources.Should().BeEquivalentTo([new ResourceRow("amount", 1)], o => o.WithStrictOrdering());

        var openItemsResources = (await conn.QueryAsync<ResourceRow>(
            "select code as Code, ordinal as Ordinal from operational_register_resources where register_id = @RegisterId order by ordinal;",
            new { RegisterId = openItems.RegisterId })).ToArray();
        openItemsResources.Should().BeEquivalentTo([new ResourceRow("amount", 1)], o => o.WithStrictOrdering());

        var tenantBalanceDims = (await conn.QueryAsync<DimensionRuleRow>(
            """
            select d.code as Code, r.ordinal as Ordinal, r.is_required as IsRequired
            from operational_register_dimension_rules r
            join platform_dimensions d on d.dimension_id = r.dimension_id
            where r.register_id = @RegisterId
            order by r.ordinal;
            """,
            new { tenantBalances.RegisterId })).ToArray();
        tenantBalanceDims.Should().BeEquivalentTo(ExpectedPmAccountDimensions(), o => o.WithStrictOrdering());

        var openItemsDims = (await conn.QueryAsync<DimensionRuleRow>(
            """
            select d.code as Code, r.ordinal as Ordinal, r.is_required as IsRequired
            from operational_register_dimension_rules r
            join platform_dimensions d on d.dimension_id = r.dimension_id
            where r.register_id = @RegisterId
            order by r.ordinal;
            """,
            new { RegisterId = openItems.RegisterId })).ToArray();
        openItemsDims.Should().BeEquivalentTo(ExpectedOpenItemsDimensions(), o => o.WithStrictOrdering());

        var policy = await conn.QuerySingleAsync<AccountingPolicyRow>(
            """
            select cash_account_id as CashAccountId,
                   ar_tenants_account_id as ArTenantsAccountId,
                   ap_vendors_account_id as ApVendorsAccountId,
                   rent_income_account_id as RentIncomeAccountId,
                   late_fee_income_account_id as LateFeeIncomeAccountId,
                   tenant_balances_register_id as TenantBalancesRegisterId,
                   receivables_open_items_register_id as ReceivablesOpenItemsRegisterId,
                   payables_open_items_register_id as PayablesOpenItemsRegisterId
            from cat_pm_accounting_policy;
            """);

        policy.CashAccountId.Should().Be(cashAccountId);
        policy.ArTenantsAccountId.Should().Be(arAccountId);
        policy.ApVendorsAccountId.Should().Be(apAccountId);
        policy.RentIncomeAccountId.Should().Be(rentalIncomeAccountId);
        policy.LateFeeIncomeAccountId.Should().Be(lateFeeIncomeAccountId);
        policy.TenantBalancesRegisterId.Should().Be(tenantBalances.RegisterId);
        policy.ReceivablesOpenItemsRegisterId.Should().Be(openItems.RegisterId);
        policy.PayablesOpenItemsRegisterId.Should().Be(payablesOpenItems.RegisterId);

        var bankAccount = await conn.QuerySingleAsync<BankAccountRow>(
            """
            select catalog_id as CatalogId,
                   display as Display,
                   bank_name as BankName,
                   account_name as AccountName,
                   last4 as Last4,
                   gl_account_id as GlAccountId,
                   is_default as IsDefault
            from cat_pm_bank_account;
            """);

        bankAccount.GlAccountId.Should().Be(cashAccountId);
        bankAccount.IsDefault.Should().BeTrue();
        bankAccount.Last4.Should().Be("1000");

        (await conn.ExecuteScalarAsync<Guid>("select credit_account_id from cat_pm_receivable_charge_type where display = 'Utility';"))
            .Should().Be(utilityIncomeAccountId);
        (await conn.ExecuteScalarAsync<Guid>("select credit_account_id from cat_pm_receivable_charge_type where display = 'Parking';"))
            .Should().Be(parkingIncomeAccountId);
        (await conn.ExecuteScalarAsync<Guid>("select credit_account_id from cat_pm_receivable_charge_type where display = 'Damage';"))
            .Should().Be(damageIncomeAccountId);
        (await conn.ExecuteScalarAsync<Guid>("select credit_account_id from cat_pm_receivable_charge_type where display = 'Move out';"))
            .Should().Be(moveOutIncomeAccountId);
        (await conn.ExecuteScalarAsync<Guid>("select credit_account_id from cat_pm_receivable_charge_type where display = 'Misc';"))
            .Should().Be(miscIncomeAccountId);

        (await conn.ExecuteScalarAsync<Guid>("select debit_account_id from cat_pm_payable_charge_type where display = 'Repair';"))
            .Should().Be(repairsExpenseAccountId);
        (await conn.ExecuteScalarAsync<Guid>("select debit_account_id from cat_pm_payable_charge_type where display = 'Utility';"))
            .Should().Be(utilitiesExpenseAccountId);
        (await conn.ExecuteScalarAsync<Guid>("select debit_account_id from cat_pm_payable_charge_type where display = 'Cleaning';"))
            .Should().Be(cleaningExpenseAccountId);
        (await conn.ExecuteScalarAsync<Guid>("select debit_account_id from cat_pm_payable_charge_type where display = 'Supply';"))
            .Should().Be(suppliesExpenseAccountId);
        (await conn.ExecuteScalarAsync<Guid>("select debit_account_id from cat_pm_payable_charge_type where display = 'Misc';"))
            .Should().Be(miscExpenseAccountId);

        var payablesOpenItemsDims = (await conn.QueryAsync<DimensionRuleRow>(
            """
            select d.code as Code, r.ordinal as Ordinal, r.is_required as IsRequired
            from operational_register_dimension_rules r
            join platform_dimensions d on d.dimension_id = r.dimension_id
            where r.register_id = @RegisterId
            order by r.ordinal;
            """,
            new { RegisterId = payablesOpenItems.RegisterId })).ToArray();
        payablesOpenItemsDims.Should().BeEquivalentTo(ExpectedPayablesOpenItemsDimensions(), o => o.WithStrictOrdering());

        var openItemsMovementsTable = await conn.ExecuteScalarAsync<bool>(
            "select to_regclass('public.' || @TableName) is not null;",
            new { TableName = $"opreg_{openItems.TableCode}__movements" });
        openItemsMovementsTable.Should().BeTrue();

        var openItemsTurnoversTable = await conn.ExecuteScalarAsync<bool>(
            "select to_regclass('public.' || @TableName) is not null;",
            new { TableName = $"opreg_{openItems.TableCode}__turnovers" });
        openItemsTurnoversTable.Should().BeTrue();

        var openItemsBalancesTable = await conn.ExecuteScalarAsync<bool>(
            "select to_regclass('public.' || @TableName) is not null;",
            new { TableName = $"opreg_{openItems.TableCode}__balances" });
        openItemsBalancesTable.Should().BeTrue();

        var payablesOpenItemsMovementsTable = await conn.ExecuteScalarAsync<bool>(
            "select to_regclass('public.' || @TableName) is not null;",
            new { TableName = $"opreg_{payablesOpenItems.TableCode}__movements" });
        payablesOpenItemsMovementsTable.Should().BeTrue();

        var payablesOpenItemsTurnoversTable = await conn.ExecuteScalarAsync<bool>(
            "select to_regclass('public.' || @TableName) is not null;",
            new { TableName = $"opreg_{payablesOpenItems.TableCode}__turnovers" });
        payablesOpenItemsTurnoversTable.Should().BeTrue();

        var payablesOpenItemsBalancesTable = await conn.ExecuteScalarAsync<bool>(
            "select to_regclass('public.' || @TableName) is not null;",
            new { TableName = $"opreg_{payablesOpenItems.TableCode}__balances" });
        payablesOpenItemsBalancesTable.Should().BeTrue();
    }

    private static IReadOnlyList<DimensionRuleRow> ExpectedPmAccountDimensions()
        =>
        [
            new(PropertyManagementCodes.Party, 1, true),
            new(PropertyManagementCodes.Property, 2, true),
            new(PropertyManagementCodes.Lease, 3, true)
        ];

    private static IReadOnlyList<DimensionRuleRow> ExpectedOpenItemsDimensions()
        =>
        [
            new(PropertyManagementCodes.Party, 1, true),
            new(PropertyManagementCodes.Property, 2, true),
            new(PropertyManagementCodes.Lease, 3, true),
            new(PropertyManagementCodes.ReceivableItem, 4, true)
        ];

    private static IReadOnlyList<DimensionRuleRow> ExpectedPayablesOpenItemsDimensions()
        =>
        [
            new(PropertyManagementCodes.Party, 1, true),
            new(PropertyManagementCodes.Property, 2, true),
            new(PropertyManagementCodes.PayableItem, 3, true)
        ];

    private sealed record DimensionRuleRow(string Code, int Ordinal, bool IsRequired);

    private sealed record ResourceRow(string Code, int Ordinal);

    private sealed record RegisterRow(Guid RegisterId, string TableCode);

    private sealed record BankAccountRow(Guid CatalogId, string Display, string BankName, string AccountName, string Last4, Guid GlAccountId, bool IsDefault);

    private sealed record AccountingPolicyRow(
        Guid CashAccountId,
        Guid ArTenantsAccountId,
        Guid ApVendorsAccountId,
        Guid RentIncomeAccountId,
        Guid LateFeeIncomeAccountId,
        Guid TenantBalancesRegisterId,
        Guid ReceivablesOpenItemsRegisterId,
        Guid PayablesOpenItemsRegisterId);
}

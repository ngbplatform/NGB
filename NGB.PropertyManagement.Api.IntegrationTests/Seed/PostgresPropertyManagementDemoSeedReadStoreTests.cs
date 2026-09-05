using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Seeding;
using NGB.Runtime.UnitOfWork;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Seed;

[Collection(PmIntegrationCollection.Name)]
public sealed class PostgresPropertyManagementDemoSeedReadStoreTests(PmIntegrationFixture fixture)
    : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Read_store_returns_dataset_lookups_and_party_identities_in_one_scope()
    {
        await using var factory = new PmApiFactory(fixture);
        await using var scope = factory.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var store = scope.ServiceProvider.GetRequiredService<IPropertyManagementDemoSeedReadStore>();

        Func<Task> blankMarker = () => store.DatasetExistsAsync(" ", CancellationToken.None);
        await blankMarker.Should().ThrowAsync<ArgumentException>();
        (await store.DatasetExistsAsync("missing", CancellationToken.None)).Should().BeFalse();

        var accountId = Guid.CreateVersion7();
        var buildingId = Guid.CreateVersion7();
        var partyId = Guid.CreateVersion7();
        var bankAccountId = Guid.CreateVersion7();
        var utilityReceivableId = Guid.CreateVersion7();
        var parkingReceivableId = Guid.CreateVersion7();
        var repairPayableId = Guid.CreateVersion7();
        var utilityPayableId = Guid.CreateVersion7();
        var maintenanceCategoryId = Guid.CreateVersion7();

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await uow.Connection.ExecuteAsync(new CommandDefinition(
                """
                insert into accounting_accounts
                    (account_id, code, name, account_type, statement_section, negative_balance_policy)
                values
                    (@AccountId, 'seed-read', 'Seed read cash', @AccountType, @StatementSection, @NegativeBalancePolicy);

                insert into catalogs (id, catalog_code) values
                    (@BuildingId, 'pm.property'),
                    (@PartyId, 'pm.party'),
                    (@BankAccountId, 'pm.bank_account'),
                    (@UtilityReceivableId, 'pm.receivable_charge_type'),
                    (@ParkingReceivableId, 'pm.receivable_charge_type'),
                    (@RepairPayableId, 'pm.payable_charge_type'),
                    (@UtilityPayableId, 'pm.payable_charge_type'),
                    (@MaintenanceCategoryId, 'pm.maintenance_category');

                insert into cat_pm_property
                    (catalog_id, kind, display, address_line1, address_line2, city, state, zip)
                values
                    (@BuildingId, 'Building', 'Seed Building', '1 Seed St', 'dataset-marker', 'Hoboken', 'NJ', '07030');

                insert into cat_pm_party (catalog_id, display, email)
                values (@PartyId, ' Seed Tenant ', ' seed@example.test ');

                insert into cat_pm_bank_account
                    (catalog_id, display, bank_name, account_name, last4, gl_account_id, is_default)
                values
                    (@BankAccountId, 'Operating', 'Seed Bank', 'Seed read cash', '1234', @AccountId, true);

                insert into cat_pm_receivable_charge_type (catalog_id, display) values
                    (@UtilityReceivableId, 'Utility'),
                    (@ParkingReceivableId, 'Parking');

                insert into cat_pm_payable_charge_type (catalog_id, display) values
                    (@RepairPayableId, 'Repair'),
                    (@UtilityPayableId, 'Utility');

                insert into cat_pm_maintenance_category (catalog_id, display)
                values (@MaintenanceCategoryId, 'Maintenance');
                """,
                new
                {
                    AccountId = accountId,
                    AccountType = (short)AccountType.Asset,
                    StatementSection = (short)StatementSection.Assets,
                    NegativeBalancePolicy = (short)NegativeBalancePolicy.Allow,
                    BuildingId = buildingId,
                    PartyId = partyId,
                    BankAccountId = bankAccountId,
                    UtilityReceivableId = utilityReceivableId,
                    ParkingReceivableId = parkingReceivableId,
                    RepairPayableId = repairPayableId,
                    UtilityPayableId = utilityPayableId,
                    MaintenanceCategoryId = maintenanceCategoryId
                },
                uow.Transaction,
                cancellationToken: ct));

            (await store.DatasetExistsAsync("dataset-marker", ct)).Should().BeTrue();

            var lookup = await store.LoadLookupsAsync(ct);
            lookup.DefaultBankAccountId.Should().Be(bankAccountId);
            lookup.BankAccounts.Should().ContainSingle().Which.Should().BeEquivalentTo(
                new PropertyManagementDemoSeedLookupRow(bankAccountId, "Seed Bank Seed read cash **** 1234"));
            lookup.ReceivableChargeTypes.Select(x => x.Id)
                .Should().BeEquivalentTo([utilityReceivableId, parkingReceivableId]);
            lookup.PayableChargeTypes.Select(x => x.Id)
                .Should().BeEquivalentTo([repairPayableId, utilityPayableId]);
            lookup.MaintenanceCategories.Should().ContainSingle(x => x.Id == maintenanceCategoryId);

            var parties = await store.LoadPartyIdentitiesAsync(ct);
            parties.Should().ContainSingle(x => x.Display == " Seed Tenant " && x.Email == " seed@example.test ");
        }, CancellationToken.None);
    }
}

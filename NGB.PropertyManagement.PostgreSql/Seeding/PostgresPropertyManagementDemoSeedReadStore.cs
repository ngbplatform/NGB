using Dapper;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Seeding;

namespace NGB.PropertyManagement.PostgreSql.Seeding;

internal sealed class PostgresPropertyManagementDemoSeedReadStore(IUnitOfWork uow)
    : IPropertyManagementDemoSeedReadStore
{
    public async Task<bool> DatasetExistsAsync(string datasetMarker, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetMarker);

        await uow.EnsureConnectionOpenAsync(ct);

        return await uow.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            select exists (
                select 1
                  from cat_pm_property
                 where kind = 'Building'
                   and address_line2 = @DatasetMarker
            );
            """,
            new { DatasetMarker = datasetMarker },
            uow.Transaction,
            cancellationToken: ct));
    }

    public async Task<PropertyManagementDemoSeedLookupSnapshot> LoadLookupsAsync(CancellationToken ct = default)
    {
        await uow.EnsureConnectionOpenAsync(ct);

        var command = new CommandDefinition(
            """
            select catalog_id as Id, display as Name
              from cat_pm_bank_account
             order by is_default desc, display;

            select catalog_id
              from cat_pm_bank_account
             where is_default = true
             order by catalog_id
             limit 1;

            select catalog_id as Id, display as Name
              from cat_pm_receivable_charge_type
             order by display;

            select catalog_id as Id, display as Name
              from cat_pm_payable_charge_type
             order by display;

            select catalog_id as Id, display as Name
              from cat_pm_maintenance_category
             order by display;
            """,
            transaction: uow.Transaction,
            cancellationToken: ct);

        await using var result = await uow.Connection.QueryMultipleAsync(command);
        var bankAccounts = (await result.ReadAsync<PropertyManagementDemoSeedLookupRow>()).AsList();
        var defaultBankAccountId = await result.ReadSingleOrDefaultAsync<Guid?>();
        var receivableChargeTypes = (await result.ReadAsync<PropertyManagementDemoSeedLookupRow>()).AsList();
        var payableChargeTypes = (await result.ReadAsync<PropertyManagementDemoSeedLookupRow>()).AsList();
        var maintenanceCategories = (await result.ReadAsync<PropertyManagementDemoSeedLookupRow>()).AsList();

        return new PropertyManagementDemoSeedLookupSnapshot(
            defaultBankAccountId,
            bankAccounts,
            receivableChargeTypes,
            payableChargeTypes,
            maintenanceCategories);
    }

    public async Task<IReadOnlyList<PropertyManagementDemoSeedPartyIdentity>> LoadPartyIdentitiesAsync(
        CancellationToken ct = default)
    {
        await uow.EnsureConnectionOpenAsync(ct);

        var rows = await uow.Connection.QueryAsync<PropertyManagementDemoSeedPartyIdentity>(new CommandDefinition(
            """
            select display as Display,
                   email as Email
              from cat_pm_party
             order by display nulls last, email nulls last;
            """,
            transaction: uow.Transaction,
            cancellationToken: ct));

        return rows.AsList();
    }
}

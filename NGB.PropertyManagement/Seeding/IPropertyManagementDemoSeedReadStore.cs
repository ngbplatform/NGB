namespace NGB.PropertyManagement.Seeding;

public interface IPropertyManagementDemoSeedReadStore
{
    Task<bool> DatasetExistsAsync(string datasetMarker, CancellationToken ct = default);

    Task<PropertyManagementDemoSeedLookupSnapshot> LoadLookupsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<PropertyManagementDemoSeedPartyIdentity>> LoadPartyIdentitiesAsync(CancellationToken ct = default);
}

public sealed record PropertyManagementDemoSeedLookupRow(Guid Id, string Name);

public sealed record PropertyManagementDemoSeedLookupSnapshot(
    Guid? DefaultBankAccountId,
    IReadOnlyList<PropertyManagementDemoSeedLookupRow> BankAccounts,
    IReadOnlyList<PropertyManagementDemoSeedLookupRow> ReceivableChargeTypes,
    IReadOnlyList<PropertyManagementDemoSeedLookupRow> PayableChargeTypes,
    IReadOnlyList<PropertyManagementDemoSeedLookupRow> MaintenanceCategories);

public sealed record PropertyManagementDemoSeedPartyIdentity(string? Display, string? Email);

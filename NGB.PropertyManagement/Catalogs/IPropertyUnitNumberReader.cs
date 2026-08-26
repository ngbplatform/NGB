namespace NGB.PropertyManagement.Catalogs;

/// <summary>
/// Read model for resolving active unit numbers within a building without
/// materializing every unit that belongs to that building.
/// </summary>
public interface IPropertyUnitNumberReader
{
    Task<IReadOnlySet<string>> GetExistingAsync(
        Guid buildingId,
        IReadOnlyCollection<string> unitNumbers,
        CancellationToken ct = default);
}

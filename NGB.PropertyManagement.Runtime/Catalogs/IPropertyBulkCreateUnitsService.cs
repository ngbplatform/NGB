using NGB.PropertyManagement.Contracts.Catalogs;

namespace NGB.PropertyManagement.Runtime.Catalogs;

public interface IPropertyBulkCreateUnitsService
{
    Task<PropertyBulkCreateUnitsResponse> BulkCreateUnitsAsync(
        PropertyBulkCreateUnitsRequest request,
        CancellationToken ct);

    /// <summary>
    /// Computes a preview (requested + duplicates + would-create) without writing anything.
    /// Useful for UI wizards.
    /// </summary>
    Task<PropertyBulkCreateUnitsResponse> DryRunAsync(PropertyBulkCreateUnitsRequest request, CancellationToken ct);
}

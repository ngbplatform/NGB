namespace NGB.Definitions.Catalogs.Validation;

/// <summary>
/// Catalog validation context for Create/Update operations.
/// </summary>
public sealed record CatalogUpsertValidationContext(
    string TypeCode,
    Guid CatalogId,
    bool IsCreate,
    IReadOnlyDictionary<string, object?> Fields);

using NGB.Metadata.Catalogs.Hybrid;
using NGB.Tools.Exceptions;

namespace NGB.Definitions.Catalogs;

/// <summary>
/// Immutable definition of a catalog type.
/// </summary>
public sealed class CatalogTypeDefinition
{
    public CatalogTypeDefinition(
        string typeCode,
        CatalogTypeMetadata metadata,
        Type? typedStorageType = null,
        IReadOnlyList<Type>? validatorTypes = null)
    {
        if (string.IsNullOrWhiteSpace(typeCode))
            throw new NgbArgumentInvalidException(nameof(typeCode), "Type code must be non-empty.");
        
        TypeCode = typeCode;
        Metadata = metadata ?? throw new NgbArgumentRequiredException(nameof(metadata));

        TypedStorageType = typedStorageType;
        ValidatorTypes = validatorTypes ?? Array.Empty<Type>();
    }

    public string TypeCode { get; }
    public CatalogTypeMetadata Metadata { get; }

    /// <summary>
    /// Optional type that implements the per-catalog-type storage (typed tables).
    /// Registered by an industry solution provider module.
    /// </summary>
    public Type? TypedStorageType { get; }

    /// <summary>
    /// Optional validators.
    /// </summary>
    public IReadOnlyList<Type> ValidatorTypes { get; }
}

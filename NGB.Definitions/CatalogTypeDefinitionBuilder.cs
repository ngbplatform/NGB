using NGB.Metadata.Catalogs.Hybrid;
using NGB.Tools.Exceptions;

namespace NGB.Definitions;

public sealed class CatalogTypeDefinitionBuilder
{
    private readonly DefinitionsBuilder.MutableCatalogTypeDefinition _mutable;

    internal CatalogTypeDefinitionBuilder(DefinitionsBuilder.MutableCatalogTypeDefinition mutable)
        => _mutable = mutable;

    public CatalogTypeDefinitionBuilder Metadata(CatalogTypeMetadata metadata)
    {
        if (metadata is null)
            throw new NgbArgumentRequiredException(nameof(metadata));

        if (_mutable.Metadata is not null)
            throw new NgbConfigurationViolationException(
                $"Catalog type '{_mutable.TypeCode}' already has metadata configured.",
                context: new Dictionary<string, object?>
                {
                    ["definitionKind"] = "catalog",
                    ["typeCode"] = _mutable.TypeCode,
                    ["field"] = "metadata"
                });

        _mutable.Metadata = metadata;
        return this;
    }

    public CatalogTypeDefinitionBuilder TypedStorage(Type typedStorageType)
    {
        if (typedStorageType is null)
            throw new NgbArgumentRequiredException(nameof(typedStorageType));

        if (_mutable.TypedStorageType is not null)
            throw new NgbConfigurationViolationException(
                $"Catalog type '{_mutable.TypeCode}' typed storage is already configured.",
                context: new Dictionary<string, object?>
                {
                    ["definitionKind"] = "catalog",
                    ["typeCode"] = _mutable.TypeCode,
                    ["field"] = "typedStorageType"
                });

        _mutable.TypedStorageType = typedStorageType;
        return this;
    }

    public CatalogTypeDefinitionBuilder TypedStorage<TTypedStorage>()
        where TTypedStorage : class
        => TypedStorage(typeof(TTypedStorage));

    public CatalogTypeDefinitionBuilder AddValidator(Type validatorType)
    {
        if (validatorType is null)
            throw new NgbArgumentRequiredException(nameof(validatorType));

        if (!_mutable.ValidatorSet.Add(validatorType))
            return this;

        _mutable.ValidatorTypes.Add(validatorType);
        return this;
    }

    public CatalogTypeDefinitionBuilder AddValidator<TValidator>()
        where TValidator : class
        => AddValidator(typeof(TValidator));
}

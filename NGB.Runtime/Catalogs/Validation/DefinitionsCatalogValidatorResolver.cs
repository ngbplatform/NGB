using NGB.Definitions;
using NGB.Definitions.Catalogs.Validation;
using NGB.Runtime.Internal;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Catalogs.Validation;

internal sealed class DefinitionsCatalogValidatorResolver(
    DefinitionsRegistry definitions,
    IEnumerable<ICatalogUpsertValidator> validators)
    : ICatalogValidatorResolver
{
    private readonly IReadOnlyList<ICatalogUpsertValidator> _allValidators = DefinitionRuntimeBindingHelpers.ToReadOnlyList(validators);
    private readonly Dictionary<string, IReadOnlyList<ICatalogUpsertValidator>> _validatorsByTypeCode = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _validatorsGate = new();

    public IReadOnlyList<ICatalogUpsertValidator> ResolveUpsertValidators(string typeCode)
    {
        if (string.IsNullOrWhiteSpace(typeCode))
            throw new NgbArgumentRequiredException(nameof(typeCode));

        if (!definitions.TryGetCatalog(typeCode, out var def) || def.ValidatorTypes.Count == 0)
            return [];

        return ResolveUpsertValidators(def);
    }

    private IReadOnlyList<ICatalogUpsertValidator> ResolveUpsertValidators(NGB.Definitions.Catalogs.CatalogTypeDefinition def)
    {
        if (_validatorsByTypeCode.TryGetValue(def.TypeCode, out var cached))
            return cached;

        lock (_validatorsGate)
        {
            if (_validatorsByTypeCode.TryGetValue(def.TypeCode, out cached))
                return cached;

            var resolved = BuildValidators(def);
            _validatorsByTypeCode[def.TypeCode] = resolved;
            return resolved;
        }
    }

    private IReadOnlyList<ICatalogUpsertValidator> BuildValidators(NGB.Definitions.Catalogs.CatalogTypeDefinition def)
    {
        var resolved = new List<ICatalogUpsertValidator>(def.ValidatorTypes.Count);
        foreach (var validatorType in def.ValidatorTypes)
        {
            if (!typeof(ICatalogUpsertValidator).IsAssignableFrom(validatorType))
            {
                throw new NgbConfigurationViolationException(
                    $"Catalog validator '{validatorType.FullName}' must implement ICatalogUpsertValidator for catalog type '{def.TypeCode}'.",
                    context: new Dictionary<string, object?>
                    {
                        ["typeCode"] = def.TypeCode,
                        ["validatorType"] = validatorType.FullName
                    });
            }

            var matches = DefinitionRuntimeBindingHelpers.FindMatches(validatorType, _allValidators);

            if (matches.Length == 0)
            {
                throw new NgbConfigurationViolationException(
                    $"Catalog validator '{validatorType.FullName}' is not registered for catalog type '{def.TypeCode}'.",
                    context: new Dictionary<string, object?>
                    {
                        ["typeCode"] = def.TypeCode,
                        ["validatorType"] = validatorType.FullName
                    });
            }

            if (matches.Length > 1)
            {
                throw new NgbConfigurationViolationException(
                    $"Catalog validator '{validatorType.FullName}' has multiple registrations for catalog type '{def.TypeCode}'.",
                    context: new Dictionary<string, object?>
                    {
                        ["typeCode"] = def.TypeCode,
                        ["validatorType"] = validatorType.FullName,
                        ["matches"] = matches.Select(validator => validator.GetType().FullName ?? validator.GetType().Name).ToArray()
                    });
            }

            var typed = matches[0];
            if (!string.Equals(typed.TypeCode, def.TypeCode, StringComparison.Ordinal))
            {
                throw new NgbConfigurationViolationException(
                    $"Catalog validator '{validatorType.FullName}' TypeCode does not match. Expected '{def.TypeCode}', actual '{typed.TypeCode}'.",
                    context: new Dictionary<string, object?>
                    {
                        ["typeCode"] = def.TypeCode,
                        ["validatorType"] = validatorType.FullName,
                        ["expectedTypeCode"] = def.TypeCode,
                        ["actualTypeCode"] = typed.TypeCode
                    });
            }

            resolved.Add(typed);
        }

        return resolved;
    }
}

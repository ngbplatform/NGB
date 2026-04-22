using NGB.Definitions;
using NGB.Definitions.Catalogs.Validation;

namespace NGB.Runtime.Catalogs.Validation;

/// <summary>
/// Resolves per-catalog-type validators configured in <see cref="DefinitionsRegistry"/>.
/// </summary>
public interface ICatalogValidatorResolver
{
    IReadOnlyList<ICatalogUpsertValidator> ResolveUpsertValidators(string typeCode);
}

using NGB.Definitions;
using NGB.Trade.Runtime.Catalogs.Validation;

namespace NGB.Trade.Runtime;

public sealed class TradeCatalogValidationDefinitionsContributor : IDefinitionsContributor
{
    public void Contribute(DefinitionsBuilder builder)
        => builder.ExtendCatalog(TradeCodes.Party, c => c.AddValidator<PartyCatalogUpsertValidator>());
}

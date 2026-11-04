using EquipmentInspection.Definitions;
using NGB.Definitions;

namespace EquipmentInspection.Runtime;

public sealed class EquipmentCatalogRuntimeDefinitions : IDefinitionsContributor
{
    public void Contribute(DefinitionsBuilder builder)
    {
        builder.ExtendCatalog(EquipmentDefinitions.EquipmentCode, catalog =>
            catalog.AddValidator<EquipmentNameValidator>());
    }
}

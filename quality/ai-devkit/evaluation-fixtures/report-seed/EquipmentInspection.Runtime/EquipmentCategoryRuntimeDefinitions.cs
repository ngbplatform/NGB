using EquipmentInspection.Definitions;
using NGB.Definitions;

namespace EquipmentInspection.Runtime;

public sealed class EquipmentCategoryRuntimeDefinitions : IDefinitionsContributor
{
    public void Contribute(DefinitionsBuilder builder)
    {
        builder.ExtendCatalog(EquipmentCategoryDefinitions.TypeCode, catalog =>
            catalog.AddValidator<EquipmentCategoryValidator>());
    }
}

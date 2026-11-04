using EquipmentInspection.Definitions;
using NGB.Definitions;

namespace EquipmentInspection.Runtime;

public sealed class InspectionRuntimeDefinitions : IDefinitionsContributor
{
    public void Contribute(DefinitionsBuilder builder)
    {
        builder.ExtendDocument(EquipmentDefinitions.InspectionCode,
            document => document
                .AddPostValidator<InspectionPostValidator>()
                .NumberingPolicy<InspectionNumberingPolicy>());
    }
}

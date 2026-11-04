using EquipmentInspection.Definitions;
using NGB.Definitions;

namespace EquipmentInspection.Runtime;

public sealed class EquipmentConditionRuntimeDefinitions : IDefinitionsContributor
{
    public void Contribute(DefinitionsBuilder builder)
    {
        builder.ExtendDocument(EquipmentConditionDefinitions.DocumentCode, document => document
            .AddPostValidator<EquipmentConditionPostValidator>()
            .ReferenceRegisterPostingHandler<EquipmentConditionPostingHandler>());
    }
}

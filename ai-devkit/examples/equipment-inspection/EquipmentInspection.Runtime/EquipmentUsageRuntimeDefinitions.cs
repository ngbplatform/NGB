using EquipmentInspection.Definitions;
using NGB.Definitions;

namespace EquipmentInspection.Runtime;

public sealed class EquipmentUsageRuntimeDefinitions : IDefinitionsContributor
{
    public void Contribute(DefinitionsBuilder builder)
    {
        builder.ExtendDocument(EquipmentUsageDefinitions.DocumentCode, document => document
            .AddPostValidator<EquipmentUsagePostValidator>()
            .OperationalRegisterPostingHandler<EquipmentUsagePostingHandler>());
    }
}

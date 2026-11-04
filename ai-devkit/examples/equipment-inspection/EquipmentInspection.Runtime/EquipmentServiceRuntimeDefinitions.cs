using EquipmentInspection.Definitions;
using NGB.Definitions;

namespace EquipmentInspection.Runtime;

public sealed class EquipmentServiceRuntimeDefinitions : IDefinitionsContributor
{
    public void Contribute(DefinitionsBuilder builder)
    {
        builder.ExtendDocument(EquipmentServiceDefinitions.DocumentCode, document => document
            .AddPostValidator<EquipmentServicePostValidator>()
            .PostingHandler<EquipmentServicePostingHandler>()
            .OperationalRegisterPostingHandler<EquipmentServicePostingHandler>()
            .ReferenceRegisterPostingHandler<EquipmentServicePostingHandler>());
    }
}

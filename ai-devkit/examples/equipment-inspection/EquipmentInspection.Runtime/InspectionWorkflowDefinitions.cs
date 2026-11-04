using EquipmentInspection.Definitions;
using NGB.Definitions;

namespace EquipmentInspection.Runtime;

public sealed class InspectionWorkflowDefinitions : IDefinitionsContributor
{
    public void Contribute(DefinitionsBuilder builder)
    {
        builder.ExtendDocument(EquipmentDefinitions.InspectionCode,
            document => document.ReferenceRegisterPostingHandler<InspectionWorkflowPostingHandler>());
    }
}

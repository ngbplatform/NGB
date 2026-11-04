using EquipmentInspection.Definitions;
using NGB.Definitions;

namespace EquipmentInspection.Runtime;

public sealed class ChecklistRuntimeDefinitions : IDefinitionsContributor
{
    public void Contribute(DefinitionsBuilder builder)
    {
        builder.ExtendDocument(ChecklistDefinitions.DocumentCode, document => document
            .AddDraftValidator<ChecklistDraftValidator>()
            .AddPostValidator<ChecklistPostValidator>()
            .ApprovalPolicy<ChecklistApprovalPolicy>()
            .ReferenceRegisterPostingHandler<ChecklistWorkflowPostingHandler>());
    }
}

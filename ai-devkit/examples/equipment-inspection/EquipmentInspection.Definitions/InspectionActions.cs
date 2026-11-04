using NGB.Core.Documents.Actions;
using NGB.Definitions.Documents.Actions;
using NGB.Metadata.Documents.Actions;

namespace EquipmentInspection.Definitions;

public sealed class InspectionActions : IDocumentActionDefinitionsContributor
{
    public void Contribute(DocumentActionDefinitionsBuilder builder)
    {
        builder.Add(EquipmentDefinitions.InspectionCode, new DocumentActionMetadata(
            StandardDocumentActionCodes.Post,
            new DocumentActionPresentation("Post", Icon: "upload"),
            DocumentActionKind.Primary,
            DocumentActionExecutionKind.Command,
            Order: 100));
    }
}

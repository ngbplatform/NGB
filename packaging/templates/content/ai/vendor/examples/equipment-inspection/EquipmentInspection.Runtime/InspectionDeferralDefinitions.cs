using EquipmentInspection.Definitions;
using NGB.Core.Documents.Actions;
using NGB.Definitions.Documents.Actions;
using NGB.Metadata.Documents.Actions;

namespace EquipmentInspection.Runtime;

public sealed class InspectionDeferralDefinitions : IDocumentActionDefinitionsContributor
{
    public const string ActionCode = "example.defer-one-day";
    public const string EquipmentSelectedFact = "example.inspection.equipment-selected";

    public void Contribute(DocumentActionDefinitionsBuilder builder)
    {
        builder.Add(EquipmentDefinitions.InspectionCode,
            new DocumentActionMetadata(new DocumentActionCode(ActionCode),
                new DocumentActionPresentation("Defer one day", Description: "Move this draft inspection's date forward by one day."),
                DocumentActionKind.Secondary, DocumentActionExecutionKind.Command, 20,
                new DocumentActionConfirmationMetadata(DocumentActionConfirmationMode.RequireReason,
                    "Defer inspection?", "The inspection stays a draft. Record why its date needs to move forward by one day.", "Defer")),
            handlerType: typeof(InspectionDeferralHandler),
            availabilityEvaluatorType: typeof(InspectionDeferralAvailability),
            authorizationEvaluatorType: typeof(InspectionDeferralAuthorization));
    }
}

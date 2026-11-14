using EquipmentInspection.Definitions;
using NGB.Definitions;

namespace EquipmentInspection.Runtime;

public sealed class InspectionDerivationDefinitions : IDefinitionsContributor
{
    public const string ActionCode = "example.reinspect";
    public const string RelationshipCode = "example.reinspection_of";

    public void Contribute(DefinitionsBuilder builder)
    {
        builder.AddDocumentRelationshipType(RelationshipCode, relationship => relationship
            .Name("Reinspection of")
            .ManyToOne()
            .AllowFromDocumentTypes(EquipmentDefinitions.InspectionCode)
            .AllowToDocumentTypes(EquipmentDefinitions.InspectionCode));
        builder.AddDocumentDerivation(ActionCode, derivation => derivation
            .Name("Create reinspection")
            .From(EquipmentDefinitions.InspectionCode)
            .To(EquipmentDefinitions.InspectionCode)
            .Relationships(RelationshipCode, "created_from"));
    }
}

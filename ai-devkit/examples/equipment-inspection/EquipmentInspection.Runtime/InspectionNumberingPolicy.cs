using EquipmentInspection.Definitions;
using NGB.Definitions.Documents.Numbering;

namespace EquipmentInspection.Runtime;

public sealed class InspectionNumberingPolicy : IDocumentNumberingPolicy
{
    public string TypeCode => EquipmentDefinitions.InspectionCode;
    public bool EnsureNumberOnCreateDraft => true;
    public bool EnsureNumberOnPost => false;
}

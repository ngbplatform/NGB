using EquipmentInspection.Definitions;
using NGB.Definitions;

namespace EquipmentInspection.PostgreSql;

public sealed class ChecklistStorageDefinitions : IDefinitionsContributor
{
    public void Contribute(DefinitionsBuilder builder)
    {
        builder.ExtendDocument(ChecklistDefinitions.DocumentCode, document => document.TypedStorage<ChecklistStorage>());
    }
}

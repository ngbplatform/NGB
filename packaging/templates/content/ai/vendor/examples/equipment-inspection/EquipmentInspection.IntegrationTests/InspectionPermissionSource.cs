using NGB.Contracts.Security;
using NGB.Runtime.Security;

namespace EquipmentInspection.IntegrationTests;

internal sealed class InspectionPermissionSource : INgbPermissionDefinitionSource
{
    public Task<IReadOnlyList<PermissionDefinitionDto>> GetDefinitionsAsync(CancellationToken ct)
    {
        IReadOnlyList<PermissionDefinitionDto> definitions =
        [
            new("document", "example.inspection", "approve", "Approve inspection", "Inspections")
        ];
        return Task.FromResult(definitions);
    }
}

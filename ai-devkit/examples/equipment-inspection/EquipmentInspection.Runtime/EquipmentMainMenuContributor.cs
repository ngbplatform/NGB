using EquipmentInspection.Definitions;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Admin;

namespace EquipmentInspection.Runtime;

public sealed class EquipmentMainMenuContributor : IMainMenuContributor
{
    public Task<IReadOnlyList<MainMenuGroupDto>> ContributeAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<MainMenuGroupDto> groups =
        [
            new("Equipment inspections",
                [
                    new("catalog", EquipmentDefinitions.EquipmentCode, "Equipment",
                        "/catalogs/example.equipment", "wrench", 10),
                    new("document", EquipmentDefinitions.InspectionCode, "Inspections",
                        "/documents/example.inspection", "clipboard-check", 20),
                    new("report", "example.inspection-summary", "Inspection summary",
                        "/reports/example.inspection-summary", "chart-column", 30)
                ],
                Ordinal: 30,
                Icon: "clipboard-check")
        ];
        return Task.FromResult(groups);
    }
}

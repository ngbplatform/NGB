using EquipmentInspection.Definitions;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Admin;
using NGB.Contracts.Security;
using NGB.Runtime.Security;

namespace EquipmentInspection.Runtime;

public sealed class EquipmentMainMenuContributor : IMainMenuContributor, INgbPermissionDefinitionSource
{
    public const string DashboardCode = "example.dashboard";

    public Task<IReadOnlyList<PermissionDefinitionDto>> GetDefinitionsAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<PermissionDefinitionDto> permissions =
        [
            new("page", DashboardCode, "view", "View inspection dashboard", "Equipment inspections")
        ];
        return Task.FromResult(permissions);
    }

    public Task<IReadOnlyList<MainMenuGroupDto>> ContributeAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<MainMenuGroupDto> groups =
        [
            new("Dashboard", [new("page", DashboardCode, "Dashboard", "/dashboard", "home", 10)], Ordinal: 0, Icon: "home"),
            new("Equipment inspections",
                [
                    new("catalog", EquipmentDefinitions.EquipmentCode, "Equipment",
                        "/catalogs/example.equipment", "wrench", 10),
                    new("document", EquipmentDefinitions.InspectionCode, "Inspections",
                        "/documents/example.inspection", "file-text", 20),
                    new("report", "example.inspection-summary", "Inspection summary",
                        "/reports/example.inspection-summary", "bar-chart", 30)
                ],
                Ordinal: 30,
                Icon: "clipboard-list")
        ];
        return Task.FromResult(groups);
    }
}

using NGB.Application.Abstractions.Services;
using NGB.Contracts.Admin;

namespace NgbApplication.Api;

internal sealed class ApplicationMenu : IMainMenuContributor
{
    public Task<IReadOnlyList<MainMenuGroupDto>> ContributeAsync(CancellationToken ct)
    {
        IReadOnlyList<MainMenuGroupDto> groups =
        [
            new("Administration",
            [
                new("page", "system.users", "Users", "/admin/security/users", "users", 10),
                new("page", "system.roles", "Roles & Permissions", "/admin/security/roles", "shield", 20)
            ], 10, "settings")
        ];

        return Task.FromResult(groups);
    }
}

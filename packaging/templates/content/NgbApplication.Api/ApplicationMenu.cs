using Microsoft.Extensions.Configuration;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Admin;

namespace NgbApplication.Api;

internal sealed class ApplicationMenu(IConfiguration configuration) : IMainMenuContributor
{
    public Task<IReadOnlyList<MainMenuGroupDto>> ContributeAsync(CancellationToken ct)
    {
        IReadOnlyList<MainMenuGroupDto> groups =
        [
            new("Setup & Controls",
            [
                new("page", "system.users", "Users", "/admin/security/users", "users", 10),
                new("page", "system.roles", "Roles & Permissions", "/admin/security/roles", "shield", 20),
                new("page", "system.jobs", "Background Jobs",
                    configuration["ExternalLinksSettings:BackgroundJobsUiUrl"] ?? "http://localhost:5184/hangfire", "activity", 30),
                new("page", "system.health", "Health",
                    configuration["ExternalLinksSettings:HealthUiUrl"] ?? "http://localhost:5185/health-ui", "heart-pulse", 40)
            ], 10, "settings")
        ];

        return Task.FromResult(groups);
    }
}

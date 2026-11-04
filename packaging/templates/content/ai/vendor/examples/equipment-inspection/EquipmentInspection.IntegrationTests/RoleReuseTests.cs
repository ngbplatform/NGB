using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.Security;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.UnitOfWork;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class RoleReuseTests
{
    [Fact]
    public async Task Linked_users_share_the_registered_role_without_renaming_it_or_changing_account_state()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        await host.LinkUserAsync("application-administrator", true, InspectionHost.AdministratorRoleCode,
            "Registered supervisors");
        await host.LinkUserAsync("inactive-administrator", false, InspectionHost.AdministratorRoleCode,
            "Must not rename the shared role");
        await host.LinkUserAsync("application-administrator", true, InspectionHost.AdministratorRoleCode,
            "Must not rename on repeated linking");

        using var active = await host.CreateClientAsync("application-administrator");
        using var allowed = await active.GetAsync("/api/example-access/registered-permission");
        await EnsureSuccessAsync(allowed);
        Assert.True(await allowed.Content.ReadFromJsonAsync<bool>());

        using var inactive = await host.CreateClientAsync("inactive-administrator");
        using var denied = await inactive.GetAsync("/api/example-access/registered-permission");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        await services.GetRequiredService<IUnitOfWork>().ExecuteInUowTransactionAsync(async ct =>
        {
            var roles = await services.GetRequiredService<IPlatformRoleRepository>().GetAllAsync(ct);
            var role = Assert.Single(roles, item => item.Code == InspectionHost.AdministratorRoleCode);
            Assert.Equal("Registered supervisors", role.Name);
            var assignments = await services.GetRequiredService<IPlatformRoleRepository>().GetAssignedUserCountsAsync(ct);
            Assert.Equal(2, assignments[role.RoleId]);
        });
    }
}

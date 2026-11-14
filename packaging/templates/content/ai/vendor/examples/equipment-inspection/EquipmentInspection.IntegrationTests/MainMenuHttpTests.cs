using System.Net;
using System.Net.Http.Json;
using EquipmentInspection.Runtime;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Admin;
using NGB.Contracts.Security;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class MainMenuHttpTests
{
    [Theory]
    [InlineData("administrator")]
    [InlineData("application-administrator")]
    public async Task Server_contributions_merge_groups_and_keep_the_lowest_ordinal_item(string username)
    {
        await using var host = new InspectionHost(configureServices: services =>
            services.AddSingleton<IMainMenuContributor, AdditionalMenuContributor>());
        await host.StartAsync();
        if (username == "application-administrator")
        {
            await host.LinkUserAsync(username, true, InspectionHost.AdministratorRoleCode, "Renamed owners");
        }

        using var admin = await host.CreateClientAsync(username);
        var menu = (await admin.GetFromJsonAsync<MainMenuDto>("/api/main-menu"))!;
        var group = Assert.Single(menu.Groups, group => group.Label == "Equipment inspections");
        Assert.Equal(5, group.Ordinal);
        Assert.Equal("clipboard-check", group.Icon);
        Assert.Equal(3, group.Items.Count);
        Assert.Equal("file-text", group.Items[1].Icon);
        Assert.Equal("bar-chart", group.Items[2].Icon);
        Assert.Equal(new[] { "example.equipment", "example.inspection", "example.inspection-summary" },
            group.Items.Select(item => item.Code));
        Assert.Equal("Equipment overview", group.Items[0].Label);
        Assert.Equal(1, group.Items[0].Ordinal);
        Assert.Equal(menu.Groups.OrderBy(group => group.Ordinal).Select(group => group.Label),
            menu.Groups.Select(group => group.Label));
        Assert.DoesNotContain(menu.Groups, group => string.IsNullOrWhiteSpace(group.Label));
    }

    [Fact]
    public async Task Current_permissions_filter_items_and_empty_groups_after_a_role_change()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        await host.LinkUserAsync("reader", true, "inspection.menu-reader", "Menu reader");
        using var admin = await host.CreateClientAsync("administrator");
        using var reader = await host.CreateClientAsync("reader");
        Assert.Empty((await reader.GetFromJsonAsync<MainMenuDto>("/api/main-menu"))!.Groups);
        var access = (await reader.GetFromJsonAsync<CurrentAccessDto>("/api/security/me/access"))!;
        var role = await PostAsync<RoleDetailsDto>(admin, "/api/security/roles",
            new CreateRoleRequestDto("inspection.equipment-menu", "Equipment menu", null,
                [new PermissionAssignmentDto("catalog", "example.equipment", "view")]));
        using var assign = await admin.PutAsJsonAsync($"/api/security/users/{access.UserId}/roles",
            new ReplaceUserRolesRequestDto([role.RoleId]));
        await EnsureSuccessAsync(assign);
        var visible = (await reader.GetFromJsonAsync<MainMenuDto>("/api/main-menu"))!;
        var group = Assert.Single(visible.Groups);
        Assert.Equal("Equipment inspections", group.Label);
        Assert.Equal("example.equipment", Assert.Single(group.Items).Code);
        using var revoke = await admin.PutAsJsonAsync($"/api/security/roles/{role.RoleId}/permissions",
            new ReplaceRolePermissionsRequestDto([]));
        await EnsureSuccessAsync(revoke);
        Assert.Empty((await reader.GetFromJsonAsync<MainMenuDto>("/api/main-menu"))!.Groups);
    }

    [Fact]
    public async Task Inactive_and_display_name_only_administrators_receive_no_navigation()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        await host.LinkUserAsync("inactive-administrator", false, InspectionHost.AdministratorRoleCode, "Administrator");
        await host.LinkUserAsync("imitator", true, "inspection.menu-observer", "Administrator");
        foreach (var username in new[] { "inactive-administrator", "imitator" })
        {
            using var client = await host.CreateClientAsync(username);
            Assert.Empty((await client.GetFromJsonAsync<MainMenuDto>("/api/main-menu"))!.Groups);
        }
        using var anonymous = host.CreateAnonymousClient();
        using var response = await anonymous.GetAsync("/api/main-menu");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed class AdditionalMenuContributor : IMainMenuContributor
    {
        public Task<IReadOnlyList<MainMenuGroupDto>> ContributeAsync(CancellationToken ct)
        {
            IReadOnlyList<MainMenuGroupDto> groups =
            [
                new(" equipment inspections ",
                    [new("catalog", "example.equipment", "Equipment overview", "/catalogs/example.equipment", Ordinal: 1)],
                    Ordinal: 5),
                new(" ", [new("catalog", "example.equipment", "Ignored group", "/catalogs/example.equipment")])
            ];
            return Task.FromResult(groups);
        }
    }
}

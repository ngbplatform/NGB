using System.Net;
using System.Net.Http.Json;
using NGB.Contracts.Audit;
using NGB.Contracts.Common;
using NGB.Contracts.Security;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class SecurityAdministrationHttpTests
{
    private static readonly PermissionAssignmentDto EquipmentView = new("catalog", "example.equipment", "view");

    [Theory]
    [InlineData("administrator")]
    [InlineData("application-administrator")]
    public async Task Both_administrators_manage_users_with_real_identity_provider_and_retained_audit(string username)
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        if (username == "application-administrator")
        {
            await host.LinkUserAsync(username, true, InspectionHost.AdministratorRoleCode,
                "Renamed equipment supervisors");
        }

        using var admin = await host.CreateClientAsync(username);
        var role = await PostAsync<RoleDetailsDto>(admin, "/api/security/roles",
            new CreateRoleRequestDto("inspection.reviewers", "Inspection reviewers", null, [EquipmentView]));
        var user = await PostAsync<UserDetailsDto>(admin, "/api/security/users",
            new CreateUserRequestDto("new-reviewer@example.test", "New", "Reviewer", "New reviewer", true,
                null, false, [role.RoleId]));
        Assert.NotEqual(Guid.Empty, user.UserId);
        Assert.False(string.IsNullOrWhiteSpace(user.AuthSubject));
        Assert.True(user.IsActive);
        Assert.True(user.KeycloakEnabled);
        Assert.Equal(role.RoleId, Assert.Single(user.Roles).RoleId);

        var path = $"/api/security/users/{user.UserId}";
        using var update = await admin.PutAsJsonAsync(path,
            new UpdateUserRequestDto("renamed-reviewer@example.test", "Updated", "Reviewer",
                "Updated reviewer", true, null, false, [role.RoleId]));
        await EnsureSuccessAsync(update);
        var updated = (await update.Content.ReadFromJsonAsync<UserDetailsDto>())!;
        Assert.Equal(user.AuthSubject, updated.AuthSubject);
        Assert.Equal("renamed-reviewer@example.test", updated.Email);
        Assert.Equal("Updated reviewer", updated.DisplayName);
        Assert.True(updated.AccessVersion > user.AccessVersion);

        using var deactivate = await admin.PostAsync(path + "/deactivate", null);
        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);
        var inactive = (await admin.GetFromJsonAsync<UserDetailsDto>(path))!;
        Assert.False(inactive.IsActive);
        Assert.False(inactive.KeycloakEnabled);
        using var reactivate = await admin.PostAsync(path + "/reactivate", null);
        Assert.Equal(HttpStatusCode.NoContent, reactivate.StatusCode);
        var active = (await admin.GetFromJsonAsync<UserDetailsDto>(path))!;
        Assert.True(active.IsActive);
        Assert.True(active.KeycloakEnabled);
        Assert.True(active.AccessVersion > inactive.AccessVersion);

        using var unassign = await admin.PutAsJsonAsync(path + "/roles", new ReplaceUserRolesRequestDto([]));
        Assert.Equal(HttpStatusCode.NoContent, unassign.StatusCode);
        var unassigned = (await admin.GetFromJsonAsync<UserDetailsDto>(path))!;
        Assert.Empty(unassigned.Roles);
        var effective = (await admin.GetFromJsonAsync<EffectiveAccessDto>(path + "/effective-access"))!;
        Assert.Equal(user.UserId, effective.UserId);
        Assert.Equal(unassigned.AccessVersion, effective.AccessVersion);
        Assert.NotEmpty(effective.Groups);
        Assert.All(effective.Groups.SelectMany(group => group.Resources), resource => Assert.Empty(resource.Actions));

        var audit = (await admin.GetFromJsonAsync<AuditLogPageDto>(
            $"/api/audit/entities/SecurityUser/{user.UserId}?limit=100", Json))!;
        foreach (var action in new[] { "create", "update", "deactivate", "reactivate", "roles.replace" })
            Assert.Contains(audit.Items, item => item.ActionCode == "security.user." + action);
    }

    [Fact]
    public async Task Role_permissions_activation_and_membership_update_current_effective_access()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        await host.LinkUserAsync("reader", true, "inspection.initial", "Initial reader");
        using var admin = await host.CreateClientAsync("administrator");
        using var reader = await host.CreateClientAsync("reader");
        var initial = (await reader.GetFromJsonAsync<CurrentAccessDto>("/api/security/me/access"))!;
        var role = await PostAsync<RoleDetailsDto>(admin, "/api/security/roles",
            new CreateRoleRequestDto("inspection.reviewers", "Reviewers", "Test role", [EquipmentView, EquipmentView]));
        Assert.False(role.HasFullAccess);
        Assert.Equal(EquipmentView, Assert.Single(role.Permissions));
        using var assignment = await admin.PutAsJsonAsync($"/api/security/users/{initial.UserId}/roles",
            new ReplaceUserRolesRequestDto([role.RoleId]));
        Assert.Equal(HttpStatusCode.NoContent, assignment.StatusCode);
        var assigned = (await reader.GetFromJsonAsync<CurrentAccessDto>("/api/security/me/access"))!;
        Assert.True(assigned.AccessVersion > initial.AccessVersion);
        Assert.Contains(EquipmentView, assigned.Permissions);

        var path = $"/api/security/roles/{role.RoleId}";
        var details = (await admin.GetFromJsonAsync<RoleDetailsDto>(path))!;
        Assert.Equal(initial.UserId, Assert.Single(details.AssignedUsers).UserId);
        using var update = await admin.PutAsJsonAsync(path,
            new UpdateRoleRequestDto(role.Code, "Renamed reviewers", "Updated description", true, [EquipmentView]));
        await EnsureSuccessAsync(update);
        Assert.Equal("Renamed reviewers", (await update.Content.ReadFromJsonAsync<RoleDetailsDto>())!.Name);
        using var deactivate = await admin.PostAsync(path + "/deactivate", null);
        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);
        var inactive = (await reader.GetFromJsonAsync<CurrentAccessDto>("/api/security/me/access"))!;
        Assert.DoesNotContain(EquipmentView, inactive.Permissions);
        Assert.True(inactive.AccessVersion > assigned.AccessVersion);
        using var reactivate = await admin.PostAsync(path + "/reactivate", null);
        Assert.Equal(HttpStatusCode.NoContent, reactivate.StatusCode);
        var restored = (await reader.GetFromJsonAsync<CurrentAccessDto>("/api/security/me/access"))!;
        Assert.Contains(EquipmentView, restored.Permissions);

        using var revoke = await admin.PutAsJsonAsync(path + "/permissions", new ReplaceRolePermissionsRequestDto([]));
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        var revoked = (await reader.GetFromJsonAsync<CurrentAccessDto>("/api/security/me/access"))!;
        Assert.DoesNotContain(EquipmentView, revoked.Permissions);
        Assert.True(revoked.AccessVersion > restored.AccessVersion);
        var roles = (await admin.GetFromJsonAsync<RoleListItemDto[]>("/api/security/roles"))!;
        Assert.Equal(1, Assert.Single(roles, item => item.RoleId == role.RoleId).AssignedUsersCount);
        var audit = (await admin.GetFromJsonAsync<AuditLogPageDto>(
            $"/api/audit/entities/SecurityRole/{role.RoleId}?limit=100", Json))!;
        foreach (var action in new[] { "create", "update", "deactivate", "reactivate", "permissions.replace" })
            Assert.Contains(audit.Items, item => item.ActionCode == "security.role." + action);
    }

    [Fact]
    public async Task User_pages_bind_cursor_to_active_filter_and_administration_denies_ungranted_actors()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        await host.LinkUserAsync("reader", true, "inspection.readers", "Readers");
        await host.LinkUserAsync("imitator", true, "inspection.observers", "Administrator");
        await host.LinkUserAsync("inactive-administrator", false, "inspection.disabled", "Disabled administrator");
        using var admin = await host.CreateClientAsync("administrator");
        var first = (await admin.GetFromJsonAsync<PageResponseDto<UserListItemDto>>(
            "/api/security/users?limit=1&isActive=true"))!;
        Assert.Single(first.Items);
        Assert.True(first.HasMore);
        Assert.False(string.IsNullOrWhiteSpace(first.NextCursor));
        var second = (await admin.GetFromJsonAsync<PageResponseDto<UserListItemDto>>(
            "/api/security/users?limit=1&isActive=true&cursor=" + Uri.EscapeDataString(first.NextCursor!)))!;
        Assert.Single(second.Items);
        Assert.NotEqual(first.Items[0].UserId, second.Items[0].UserId);
        using var changedFilter = await admin.GetAsync(
            "/api/security/users?limit=1&isActive=false&cursor=" + Uri.EscapeDataString(first.NextCursor!));
        Assert.Equal(HttpStatusCode.BadRequest, changedFilter.StatusCode);
        var definitions = (await admin.GetFromJsonAsync<PermissionDefinitionDto[]>("/api/security/permissions/definitions"))!;
        Assert.Contains(definitions, item => item.ResourceCode == "example.inspection" && item.ActionCode == "approve");

        foreach (var username in new[] { "reader", "imitator", "inactive-administrator" })
        {
            using var actor = await host.CreateClientAsync(username);
            foreach (var endpoint in new[] { "/users", "/roles", "/permissions/definitions" })
            {
                using var denied = await actor.GetAsync("/api/security" + endpoint);
                Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            }
            using var createRole = await actor.PostAsJsonAsync("/api/security/roles",
                new CreateRoleRequestDto("forbidden.role", "Forbidden", null, []));
            Assert.Equal(HttpStatusCode.Forbidden, createRole.StatusCode);
            using var createUser = await actor.PostAsJsonAsync("/api/security/users",
                new CreateUserRequestDto("forbidden@example.test", null, null, null, true, null, false, []));
            Assert.Equal(HttpStatusCode.Forbidden, createUser.StatusCode);
        }
        using var anonymous = host.CreateAnonymousClient();
        using var unauthenticated = await anonymous.GetAsync("/api/security/users");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
    }

    [Fact]
    public async Task Registered_administrator_role_code_and_implicit_permissions_are_protected()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        await host.LinkUserAsync("application-administrator", true, InspectionHost.AdministratorRoleCode,
            "Renamed equipment supervisors");
        using var admin = await host.CreateClientAsync("administrator");
        var roles = (await admin.GetFromJsonAsync<RoleListItemDto[]>("/api/security/roles"))!;
        var role = Assert.Single(roles, item => item.Code == InspectionHost.AdministratorRoleCode);
        var path = $"/api/security/roles/{role.RoleId}";
        var details = (await admin.GetFromJsonAsync<RoleDetailsDto>(path))!;
        Assert.True(details.HasFullAccess);
        Assert.Contains(details.Permissions, item => item.ResourceCode == "example.inspection" && item.ActionCode == "approve");

        using var deactivate = await admin.PostAsync(path + "/deactivate", null);
        await AssertErrorAsync(deactivate, HttpStatusCode.Conflict, "ngb.security.administrator_role_protected");
        using var permissions = await admin.PutAsJsonAsync(path + "/permissions", new ReplaceRolePermissionsRequestDto([]));
        await AssertErrorAsync(permissions, HttpStatusCode.Conflict, "ngb.security.administrator_role_protected");
        using var renameCode = await admin.PutAsJsonAsync(path,
            new UpdateRoleRequestDto("inspection.demoted", "Changed", null, true, null));
        await AssertErrorAsync(renameCode, HttpStatusCode.Conflict, "ngb.security.administrator_role_protected");
        using var reserved = await admin.PostAsJsonAsync("/api/security/roles",
            new CreateRoleRequestDto(InspectionHost.AdministratorRoleCode, "Imitator", null, []));
        await AssertErrorAsync(reserved, HttpStatusCode.Conflict, "ngb.security.administrator_role_protected");

        using var renameDisplay = await admin.PutAsJsonAsync(path,
            new UpdateRoleRequestDto(role.Code, "Application owners", null, true, null));
        await EnsureSuccessAsync(renameDisplay);
        var renamed = (await renameDisplay.Content.ReadFromJsonAsync<RoleDetailsDto>())!;
        Assert.True(renamed.HasFullAccess);
        Assert.Equal(role.Code, renamed.Code);
        Assert.Equal("Application owners", renamed.Name);
    }
}

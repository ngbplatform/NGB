using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Contracts.Admin;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;
using NGB.Contracts.Security;
using NGB.Core.AuditLog;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.PostgreSql.Bootstrap;
using NGB.Persistence.AuditLog;
using NGB.Runtime.AuditLog;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Admin;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmSecurityAccessManagement_Http_P0Tests(PmIntegrationFixture fixture) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = CreateJson();

    public async Task InitializeAsync() => await fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SecurityEndpoints_UseNgbDatabaseRolesAndDenyDirectAdminRoutesForLimitedUsers()
    {
        await using var factory = new PmApiFactory(fixture);
        await SeedSecurityDefaultsAsync(factory);

        using var adminClient = CreateHttpsClient(factory);

        var payablesRole = (await GetRolesAsync(adminClient))
            .Should()
            .ContainSingle(role => role.Code == "pm-ap-clerk")
            .Subject;

        var (limitedEmail, limitedPassword, _) = await CreateUserAsync(
            adminClient,
            "payables-clerk",
            "Payables Clerk",
            [payablesRole.RoleId]);

        using var limitedClient = CreateHttpsClient(factory, new PmKeycloakTestUser(limitedEmail, limitedPassword));

        var access = await GetCurrentAccessAsync(limitedClient);
        access.IsBootstrapAdmin.Should().BeFalse();
        access.Roles.Should().ContainSingle(role => role.Code == "pm-ap-clerk");
        access.Permissions.Should().Contain(permission =>
            permission.ResourceKind == "document"
            && permission.ResourceCode == "pm.payable_charge"
            && permission.ActionCode == "view");
        access.Permissions.Should().NotContain(permission =>
            permission.ResourceKind == "system"
            && permission.ResourceCode == "users"
            && permission.ActionCode == "view");

        using var usersResponse = await limitedClient.GetAsync("/api/security/users");
        usersResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var usersProblem = await usersResponse.Content.ReadAsStringAsync();
        usersProblem.Should().Contain("permission_denied");
        usersProblem.Should().Contain("system");
        usersProblem.Should().Contain("users");

        using var rolesResponse = await limitedClient.GetAsync("/api/security/roles");
        rolesResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var menu = await limitedClient.GetFromJsonAsync<MainMenuDto>("/api/main-menu");
        menu.Should().NotBeNull();
        var menuLabels = menu!.Groups.SelectMany(group => group.Items).Select(item => item.Label).ToArray();
        menuLabels.Should().NotContain("Users");
        menuLabels.Should().NotContain("Roles & Permissions");
    }

    [Fact]
    public async Task RoleLifecycle_ChangesCurrentAccessForAssignedUsers()
    {
        await using var factory = new PmApiFactory(fixture);
        await SeedSecurityDefaultsAsync(factory);

        using var adminClient = CreateHttpsClient(factory);

        var role = await CreateRoleAsync(
            adminClient,
            $"pm-limited-report-{Guid.NewGuid():N}",
            "Limited Balance Sheet",
            [
                new PermissionAssignmentDto("report", "accounting.balance_sheet", "view"),
                new PermissionAssignmentDto("report", "accounting.balance_sheet", "execute")
            ]);

        var (limitedEmail, limitedPassword, _) = await CreateUserAsync(
            adminClient,
            "limited-report",
            "Limited Reporter",
            [role.RoleId]);

        using var limitedClient = CreateHttpsClient(factory, new PmKeycloakTestUser(limitedEmail, limitedPassword));

        var before = await GetCurrentAccessAsync(limitedClient);
        before.Roles.Should().ContainSingle(x => x.RoleId == role.RoleId);
        before.Permissions.Should().Contain(permission =>
            permission.ResourceKind == "report"
            && permission.ResourceCode == "accounting.balance_sheet"
            && permission.ActionCode == "execute");

        using var deactivateResponse = await adminClient.PostAsync($"/api/security/roles/{role.RoleId}/deactivate", content: null);
        deactivateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterDeactivate = await GetCurrentAccessAsync(limitedClient);
        afterDeactivate.AccessVersion.Should().BeGreaterThan(before.AccessVersion);
        afterDeactivate.Roles.Should().NotContain(x => x.RoleId == role.RoleId);
        afterDeactivate.Permissions.Should().NotContain(permission =>
            permission.ResourceKind == "report"
            && permission.ResourceCode == "accounting.balance_sheet");

        using var reactivateResponse = await adminClient.PostAsync($"/api/security/roles/{role.RoleId}/reactivate", content: null);
        reactivateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterReactivate = await GetCurrentAccessAsync(limitedClient);
        afterReactivate.AccessVersion.Should().BeGreaterThan(afterDeactivate.AccessVersion);
        afterReactivate.Roles.Should().ContainSingle(x => x.RoleId == role.RoleId);
        afterReactivate.Permissions.Should().Contain(permission =>
            permission.ResourceKind == "report"
            && permission.ResourceCode == "accounting.balance_sheet"
            && permission.ActionCode == "execute");
    }

    [Fact]
    public async Task DirectMetadataDocumentCatalogAndReportRoutes_EnforceLimitedDatabasePermissions()
    {
        await using var factory = new PmApiFactory(fixture);
        await SeedSecurityDefaultsAsync(factory);

        using var adminClient = CreateHttpsClient(factory);

        var role = await CreateRoleAsync(
            adminClient,
            $"pm-report-only-{Guid.NewGuid():N}",
            "Report Only",
            [
                new PermissionAssignmentDto("report", "accounting.balance_sheet", "view"),
                new PermissionAssignmentDto("report", "accounting.balance_sheet", "execute")
            ]);

        var (limitedEmail, limitedPassword, _) = await CreateUserAsync(
            adminClient,
            "report-only",
            "Report Only User",
            [role.RoleId]);

        using var limitedClient = CreateHttpsClient(factory, new PmKeycloakTestUser(limitedEmail, limitedPassword));

        using (var response = await limitedClient.GetAsync("/api/catalogs/metadata"))
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var metadata = await response.Content.ReadFromJsonAsync<IReadOnlyList<CatalogTypeMetadataDto>>();
            metadata.Should().NotBeNull();
            metadata!.Select(x => x.CatalogType).Should().NotContain(PropertyManagementCodes.Party);
        }

        using (var response = await limitedClient.GetAsync($"/api/catalogs/{PropertyManagementCodes.Party}/metadata"))
        {
            await AssertPermissionDeniedAsync(response, "catalog", PropertyManagementCodes.Party);
        }

        using (var response = await limitedClient.GetAsync($"/api/catalogs/{PropertyManagementCodes.Party}"))
        {
            await AssertPermissionDeniedAsync(response, "catalog", PropertyManagementCodes.Party);
        }

        using (var response = await limitedClient.GetAsync("/api/documents/metadata"))
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var metadata = await response.Content.ReadFromJsonAsync<IReadOnlyList<DocumentTypeMetadataDto>>();
            metadata.Should().NotBeNull();
            metadata!.Select(x => x.DocumentType).Should().NotContain(PropertyManagementCodes.ReceivableCharge);
        }

        using (var response = await limitedClient.GetAsync($"/api/documents/{PropertyManagementCodes.ReceivableCharge}/metadata"))
        {
            await AssertPermissionDeniedAsync(response, "document", PropertyManagementCodes.ReceivableCharge);
        }

        using (var response = await limitedClient.GetAsync($"/api/documents/{PropertyManagementCodes.ReceivableCharge}"))
        {
            await AssertPermissionDeniedAsync(response, "document", PropertyManagementCodes.ReceivableCharge);
        }

        using (var response = await limitedClient.GetAsync("/api/report-definitions"))
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var definitions = await response.Content.ReadFromJsonAsync<IReadOnlyList<ReportDefinitionDto>>(Json);
            definitions.Should().NotBeNull();
            definitions!.Select(x => x.ReportCode)
                .Should()
                .Contain("accounting.balance_sheet")
                .And.NotContain("accounting.cash_flow_statement_indirect");
        }

        using (var response = await limitedClient.GetAsync("/api/report-definitions/accounting.balance_sheet"))
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using (var response = await limitedClient.GetAsync("/api/report-definitions/accounting.cash_flow_statement_indirect"))
        {
            await AssertPermissionDeniedAsync(response, "report", "accounting.cash_flow_statement_indirect");
        }

        using (var response = await limitedClient.PostAsJsonAsync(
                   "/api/reports/accounting.cash_flow_statement_indirect/execute",
                   new ReportExecutionRequestDto(
                       Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                       {
                           ["from_utc"] = "2026-01-01",
                           ["to_utc"] = "2026-01-31"
                       },
                       Offset: 0,
                       Limit: 10)))
        {
            await AssertPermissionDeniedAsync(response, "report", "accounting.cash_flow_statement_indirect");
        }
    }

    [Fact]
    public async Task DeactivatedUser_CannotObtainUsableAccess()
    {
        await using var factory = new PmApiFactory(fixture);
        await SeedSecurityDefaultsAsync(factory);

        using var adminClient = CreateHttpsClient(factory);

        var role = await CreateRoleAsync(
            adminClient,
            $"pm-disabled-user-{Guid.NewGuid():N}",
            "Disabled User Probe",
            [
                new PermissionAssignmentDto("report", "accounting.balance_sheet", "view")
            ]);

        var (limitedEmail, limitedPassword, user) = await CreateUserAsync(
            adminClient,
            "disable-probe",
            "Disable Probe",
            [role.RoleId]);

        using (var activeClient = CreateHttpsClient(factory, new PmKeycloakTestUser(limitedEmail, limitedPassword)))
        {
            var beforeDeactivate = await GetCurrentAccessAsync(activeClient);
            beforeDeactivate.IsActive.Should().BeTrue();
            beforeDeactivate.Permissions.Should().Contain(permission =>
                permission.ResourceKind == "report"
                && permission.ResourceCode == "accounting.balance_sheet"
                && permission.ActionCode == "view");
        }

        using (var response = await adminClient.PostAsync($"/api/security/users/{user.UserId}/deactivate", content: null))
        {
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var refreshed = await adminClient.GetFromJsonAsync<UserDetailsDto>($"/api/security/users/{user.UserId}");
        refreshed.Should().NotBeNull();
        refreshed!.IsActive.Should().BeFalse();
        refreshed.KeycloakEnabled.Should().BeFalse();

        var createClient = () => CreateHttpsClient(factory, new PmKeycloakTestUser(limitedEmail, limitedPassword));
        HttpClient? disabledClient = null;
        try
        {
            disabledClient = createClient();
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("disabled", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        using (disabledClient)
        {
            var access = await GetCurrentAccessAsync(disabledClient);
            access.IsActive.Should().BeFalse();
            access.Roles.Should().BeEmpty();
            access.Permissions.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task InactiveAssignedRole_RemainsVisibleInAdminButDoesNotContributeCurrentOrEffectiveAccess()
    {
        await using var factory = new PmApiFactory(fixture);
        await SeedSecurityDefaultsAsync(factory);

        using var adminClient = CreateHttpsClient(factory);

        var role = await CreateRoleAsync(
            adminClient,
            $"pm-inactive-assigned-{Guid.NewGuid():N}",
            "Inactive Assigned",
            [
                new PermissionAssignmentDto("report", "accounting.balance_sheet", "view"),
                new PermissionAssignmentDto("report", "accounting.balance_sheet", "execute")
            ]);

        var (limitedEmail, limitedPassword, user) = await CreateUserAsync(
            adminClient,
            "inactive-role",
            "Inactive Role User",
            [role.RoleId]);

        using (var response = await adminClient.PostAsync($"/api/security/roles/{role.RoleId}/deactivate", content: null))
        {
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var adminUser = await adminClient.GetFromJsonAsync<UserDetailsDto>($"/api/security/users/{user.UserId}");
        adminUser.Should().NotBeNull();
        adminUser!.Roles.Should().ContainSingle(x => x.RoleId == role.RoleId && x.IsActive == false);

        using var limitedClient = CreateHttpsClient(factory, new PmKeycloakTestUser(limitedEmail, limitedPassword));
        var currentAccess = await GetCurrentAccessAsync(limitedClient);
        currentAccess.IsActive.Should().BeTrue();
        currentAccess.Roles.Should().NotContain(x => x.RoleId == role.RoleId);
        currentAccess.Permissions.Should().NotContain(permission =>
            permission.ResourceKind == "report"
            && permission.ResourceCode == "accounting.balance_sheet");

        var effective = await adminClient.GetFromJsonAsync<EffectiveAccessDto>(
            $"/api/security/users/{user.UserId}/effective-access");

        effective.Should().NotBeNull();
        effective!.Groups
            .SelectMany(group => group.Resources)
            .Where(resource => resource.ResourceKind == "report" && resource.ResourceCode == "accounting.balance_sheet")
            .SelectMany(resource => resource.Actions)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public async Task SecurityAudit_WritesBusinessRowsForUserRoleAndRolePermissionChanges()
    {
        await using var factory = new PmApiFactory(fixture);
        await SeedSecurityDefaultsAsync(factory);

        using var adminClient = CreateHttpsClient(factory);

        var roleA = await CreateRoleAsync(
            adminClient,
            $"pm-audit-a-{Guid.NewGuid():N}",
            "Audit Role A",
            [
                new PermissionAssignmentDto("report", "accounting.balance_sheet", "view")
            ]);

        var roleB = await CreateRoleAsync(
            adminClient,
            $"pm-audit-b-{Guid.NewGuid():N}",
            "Audit Role B",
            [
                new PermissionAssignmentDto("report", "accounting.balance_sheet", "execute")
            ]);

        var (_, _, user) = await CreateUserAsync(
            adminClient,
            "audit-user",
            "Audit User",
            [roleA.RoleId]);

        using (var response = await adminClient.PutAsJsonAsync(
                   $"/api/security/users/{user.UserId}/roles",
                   new ReplaceUserRolesRequestDto([roleB.RoleId])))
        {
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var userEvents = await ReadAuditEventsAsync(
            factory,
            AuditEntityKind.SecurityUser,
            user.UserId,
            AuditActionCodes.SecurityUserRolesReplace);

        var userRolesEvent = userEvents.Should().ContainSingle().Subject;
        var userRolesChange = GetChange(userRolesEvent, "roles");
        ReadObjectArrayProperty(userRolesChange.OldValueJson, "code").Should().Contain(roleA.Code).And.NotContain(roleB.Code);
        ReadObjectArrayProperty(userRolesChange.NewValueJson, "code").Should().Contain(roleB.Code).And.NotContain(roleA.Code);

        var roleARemovalEvents = await ReadAuditEventsAsync(
            factory,
            AuditEntityKind.SecurityRole,
            roleA.RoleId,
            AuditActionCodes.SecurityRoleUpdate);
        var roleARemoval = roleARemovalEvents
            .Select(e => GetChange(e, "assigned_users"))
            .Single(change => change.OldValueJson is not null && change.NewValueJson is null);
        ReadObjectProperty(roleARemoval.OldValueJson, "email").Should().Be(user.Email);

        var roleBAdditionEvents = await ReadAuditEventsAsync(
            factory,
            AuditEntityKind.SecurityRole,
            roleB.RoleId,
            AuditActionCodes.SecurityRoleUpdate);
        var roleBAddition = roleBAdditionEvents
            .Select(e => GetChange(e, "assigned_users"))
            .Single(change => change.OldValueJson is null && change.NewValueJson is not null);
        ReadObjectProperty(roleBAddition.NewValueJson, "email").Should().Be(user.Email);

        using (var response = await adminClient.PutAsJsonAsync(
                   $"/api/security/roles/{roleB.RoleId}/permissions",
                   new ReplaceRolePermissionsRequestDto(
                   [
                       new PermissionAssignmentDto("report", "accounting.balance_sheet", "view"),
                       new PermissionAssignmentDto("report", "accounting.balance_sheet", "export")
                   ])))
        {
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var permissionEvents = await ReadAuditEventsAsync(
            factory,
            AuditEntityKind.SecurityRole,
            roleB.RoleId,
            AuditActionCodes.SecurityRolePermissionsReplace);

        var permissionsChange = GetChange(permissionEvents.Should().ContainSingle().Subject, "permissions");
        ReadObjectArrayProperty(permissionsChange.OldValueJson, "key")
            .Should()
            .Contain("report.accounting.balance_sheet.execute")
            .And.NotContain("report.accounting.balance_sheet.export");
        ReadObjectArrayProperty(permissionsChange.NewValueJson, "key")
            .Should()
            .Contain("report.accounting.balance_sheet.view")
            .And.Contain("report.accounting.balance_sheet.export")
            .And.NotContain("report.accounting.balance_sheet.execute");
    }

    private static HttpClient CreateHttpsClient(PmApiFactory factory, PmKeycloakTestUser? user = null)
        => factory.CreateAuthenticatedClient(
            new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") },
            user);

    private static async Task<IReadOnlyList<RoleListItemDto>> GetRolesAsync(HttpClient client)
    {
        var roles = await client.GetFromJsonAsync<RoleListItemDto[]>("/api/security/roles");
        roles.Should().NotBeNull();
        return roles!;
    }

    private static async Task<RoleDetailsDto> CreateRoleAsync(
        HttpClient client,
        string code,
        string name,
        IReadOnlyList<PermissionAssignmentDto> permissions)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/security/roles",
            new CreateRoleRequestDto(code, name, "Integration-test role.", permissions));

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var role = await response.Content.ReadFromJsonAsync<RoleDetailsDto>();
        role.Should().NotBeNull();
        return role!;
    }

    private static async Task<(string Email, string Password, UserDetailsDto User)> CreateUserAsync(
        HttpClient client,
        string localPartPrefix,
        string displayName,
        IReadOnlyList<Guid> roleIds)
    {
        var email = $"{localPartPrefix}-{Guid.NewGuid():N}@integration.test";
        var password = "Ngb#2026-Strong";
        var nameParts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        using var response = await client.PostAsJsonAsync(
            "/api/security/users",
            new CreateUserRequestDto(
                email,
                FirstName: nameParts.FirstOrDefault() ?? displayName,
                LastName: nameParts.Skip(1).FirstOrDefault() ?? "User",
                DisplayName: displayName,
                Enabled: true,
                TemporaryPassword: password,
                RequirePasswordUpdate: false,
                roleIds));

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var user = await response.Content.ReadFromJsonAsync<UserDetailsDto>();
        user.Should().NotBeNull();
        user!.Email.Should().Be(email);
        user.Roles.Select(role => role.RoleId).Should().BeEquivalentTo(roleIds);

        return (email, password, user);
    }

    private static async Task<CurrentAccessDto> GetCurrentAccessAsync(HttpClient client)
    {
        var access = await client.GetFromJsonAsync<CurrentAccessDto>("/api/security/me/access");
        access.Should().NotBeNull();
        return access!;
    }

    private static async Task SeedSecurityDefaultsAsync(PmApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PropertyManagementSecuritySeeder>().EnsureSeededAsync();
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static async Task AssertPermissionDeniedAsync(
        HttpResponseMessage response,
        string resourceKind,
        string resourceCode)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("permission_denied");
        body.Should().Contain(resourceKind);
        body.Should().Contain(resourceCode);
    }

    private static async Task<IReadOnlyList<AuditEvent>> ReadAuditEventsAsync(
        PmApiFactory factory,
        AuditEntityKind entityKind,
        Guid entityId,
        string actionCode)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

        return await reader.QueryAsync(new AuditLogQuery(
            EntityKind: entityKind,
            EntityId: entityId,
            ActionCode: actionCode,
            Limit: 50,
            Offset: 0));
    }

    private static AuditFieldChange GetChange(AuditEvent auditEvent, string fieldPath)
        => auditEvent.Changes.Should().ContainSingle(change => change.FieldPath == fieldPath).Subject;

    private static string[] ReadObjectArrayProperty(string? json, string propertyName)
    {
        json.Should().NotBeNullOrWhiteSpace();

        using var doc = JsonDocument.Parse(json!);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);

        return doc.RootElement
            .EnumerateArray()
            .Select(element => element.GetProperty(propertyName).GetString())
            .Where(static value => value is not null)
            .Select(static value => value!)
            .ToArray();
    }

    private static string? ReadObjectProperty(string? json, string propertyName)
    {
        json.Should().NotBeNullOrWhiteSpace();

        using var doc = JsonDocument.Parse(json!);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
        return doc.RootElement.GetProperty(propertyName).GetString();
    }
}

internal static class SecurityHttpResponseAssertions
{
    public static async Task ShouldHaveStatusAsync(this HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode == expected)
            return;

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(expected, "response body was: {0}", body);
    }
}

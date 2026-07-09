using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.CRM.Runtime;
using NGB.Core.Security;
using NGB.CRM.Api.IntegrationTests.Infrastructure;
using NGB.Runtime.Admin;
using NGB.Runtime.Security;
using Xunit;

namespace NGB.CRM.Api.IntegrationTests.Admin;

[Collection(CrmPostgresCollection.Name)]
public sealed class CrmApplicationSurface_EndToEnd_P0Tests(CrmPostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Main_Menu_Exposes_Crm_And_System_Security_Surface_Without_Accounting()
    {
        using var host = CrmHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var menu = await scope.ServiceProvider
            .GetRequiredService<IMainMenuService>()
            .GetMainMenuAsync(CancellationToken.None);

        menu.Groups.Select(static group => group.Label)
            .Should()
            .BeEquivalentTo("Dashboard", "Pipeline", "Customers", "Quotes", "Insights", "Setup & Controls");

        var items = menu.Groups.SelectMany(static group => group.Items).ToArray();

        items.Single(static item => item.Code == CrmCodes.Dashboard).Route.Should().Be("/home");
        items.Select(static item => item.Code).Should().Contain(["system.users", "system.roles"]);
        items.Single(static item => item.Code == "system.users").Route.Should().Be("/admin/security/users");
        items.Single(static item => item.Code == "system.roles").Route.Should().Be("/admin/security/roles");
        items.Where(static item => item.Kind == NgbResourceKinds.Document)
            .Should()
            .OnlyContain(static item => item.Icon == "file-text");

        menu.Groups.Select(static group => group.Label).Should().NotContain("Accounting");
        items.Select(static item => item.Code).Should().NotContain(static code =>
            code.Contains("accounting", StringComparison.OrdinalIgnoreCase));
        items.Select(static item => item.Route).Should().NotContain(static route =>
            route.Contains("/accounting", StringComparison.OrdinalIgnoreCase)
            || route.Contains("chart-of-accounts", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Setup_Seeds_Crm_Roles_With_Crm_Permissions()
    {
        using var host = CrmHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        await scope.ServiceProvider
            .GetRequiredService<ICrmSetupService>()
            .EnsureDefaultsAsync(CancellationToken.None);

        var roles = scope.ServiceProvider.GetRequiredService<IRoleManagementService>();
        var roleList = await roles.GetRolesAsync(CancellationToken.None);

        roleList.Select(static role => role.Code).Should().Contain([
            "crm.administrator",
            "crm.manager",
            "crm.sales_rep"
        ]);

        var admin = await roles.GetRoleAsync(
            roleList.Single(static role => role.Code == "crm.administrator").RoleId,
            CancellationToken.None);
        admin.Permissions.Should().Contain(static permission =>
            permission.ResourceKind == NgbResourceKinds.System
            && permission.ResourceCode == NgbPermissionResources.Roles
            && permission.ActionCode == NgbPermissionActions.Manage);
        admin.Permissions.Should().Contain(static permission =>
            permission.ResourceKind == NgbResourceKinds.External
            && permission.ResourceCode == CrmCodes.BackgroundJobs
            && permission.ActionCode == NgbPermissionActions.View);

        var definitions = await scope.ServiceProvider
            .GetRequiredService<PermissionDefinitionRegistry>()
            .GetAllAsync(CancellationToken.None);

        definitions.Should().Contain(static definition =>
            definition.ResourceKind == NgbResourceKinds.Page
            && definition.ResourceCode == CrmCodes.Dashboard
            && definition.ActionCode == NgbPermissionActions.View
            && definition.DisplayName == "View CRM dashboard");

        var sales = await roles.GetRoleAsync(
            roleList.Single(static role => role.Code == "crm.sales_rep").RoleId,
            CancellationToken.None);
        sales.Permissions.Should().Contain(static permission =>
            permission.ResourceKind == NgbResourceKinds.Document
            && permission.ResourceCode == CrmCodes.LeadIntake
            && permission.ActionCode == NgbPermissionActions.Post);
        sales.Permissions.Should().Contain(static permission =>
            permission.ResourceKind == NgbResourceKinds.Report
            && permission.ResourceCode == CrmCodes.SalesPipelineReport
            && permission.ActionCode == NgbPermissionActions.Execute);
        sales.Permissions.Should().Contain(static permission =>
            permission.ResourceKind == NgbResourceKinds.Page
            && permission.ResourceCode == CrmCodes.Dashboard
            && permission.ActionCode == NgbPermissionActions.View);
        sales.Permissions.Should().NotContain(static permission =>
            permission.ResourceKind == NgbResourceKinds.System
            && permission.ResourceCode == NgbPermissionResources.Roles
            && permission.ActionCode == NgbPermissionActions.Manage);
    }

    [Fact]
    public async Task Setup_Seeds_Alex_Carter_Platform_User_With_Crm_Admin_Role()
    {
        using var host = CrmHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        await scope.ServiceProvider
            .GetRequiredService<ICrmSetupService>()
            .EnsureDefaultsAsync(CancellationToken.None);

        var users = scope.ServiceProvider.GetRequiredService<NGB.Persistence.AuditLog.IPlatformUserRepository>();
        var userRoles = scope.ServiceProvider.GetRequiredService<NGB.Persistence.Security.IPlatformUserRoleRepository>();

        var user = (await users.GetAllAsync(CancellationToken.None))
            .Should()
            .ContainSingle(x =>
                x.Email == "alex.carter@demo.ngbplatform.com"
                && x.DisplayName == "Alex Carter"
                && x.IsActive)
            .Subject;

        var assignedRoles = await userRoles.GetRolesForUserAsync(user.UserId, CancellationToken.None);
        assignedRoles.Should().ContainSingle(x => x.Code == "crm.administrator");
    }
}

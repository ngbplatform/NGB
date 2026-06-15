using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Security;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Security;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.Runtime.UnitOfWork;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Admin;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmSecurityRepositories_P0Tests(PmIntegrationFixture fixture) : IAsyncLifetime
{
    public async Task InitializeAsync() => await fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task BulkSecurityRepositories_DeduplicateAssignmentsCountAssignedUsersAndFilterSnapshots()
    {
        await using var factory = new PmApiFactory(fixture);
        await using var scope = factory.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var roles = scope.ServiceProvider.GetRequiredService<IPlatformRoleRepository>();
        var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();
        var userRoles = scope.ServiceProvider.GetRequiredService<IPlatformUserRoleRepository>();
        var permissions = scope.ServiceProvider.GetRequiredService<IPermissionSnapshotRepository>();
        var versions = scope.ServiceProvider.GetRequiredService<IUserAccessVersionRepository>();

        var suffix = Guid.CreateVersion7().ToString("N")[^10..];
        var activeRoleId = Guid.CreateVersion7();
        var inactiveRoleId = Guid.CreateVersion7();
        var userId = Guid.Empty;

        var activeView = new NgbPermissionKey(NgbResourceKinds.Report, "accounting.balance_sheet", NgbPermissionActions.View);
        var activeExecute = new NgbPermissionKey(NgbResourceKinds.Report, "accounting.balance_sheet", NgbPermissionActions.Execute);
        var inactiveExport = new NgbPermissionKey(NgbResourceKinds.Report, "accounting.cash_flow_statement_indirect", NgbPermissionActions.Export);

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await roles.UpsertAsync(
                activeRoleId,
                $"it-active-{suffix}",
                "IT Active Bulk Role",
                "Repository bulk-query test role.",
                isSystem: false,
                isActive: true,
                ct);

            await roles.UpsertAsync(
                inactiveRoleId,
                $"it-inactive-{suffix}",
                "IT Inactive Bulk Role",
                "Repository inactive snapshot test role.",
                isSystem: false,
                isActive: false,
                ct);

            await permissions.ReplaceRolePermissionsAsync(
                activeRoleId,
                [activeView, activeView, activeExecute],
                ct);

            await permissions.ReplaceRolePermissionsAsync(
                inactiveRoleId,
                [inactiveExport, inactiveExport],
                ct);

            userId = await users.UpsertAsync(
                $"it-security-bulk-{suffix}",
                $"bulk-{suffix}@integration.test",
                "Bulk Security User",
                isActive: true,
                ct);

            await userRoles.ReplaceUserRolesAsync(
                userId,
                [activeRoleId, activeRoleId, inactiveRoleId, inactiveRoleId],
                assignedByUserId: null,
                ct);

            await versions.GetOrCreateAsync(userId, ct);
        }, CancellationToken.None);

        var assignedCounts = await roles.GetAssignedUserCountsAsync();
        assignedCounts[activeRoleId].Should().Be(1);
        assignedCounts[inactiveRoleId].Should().Be(1);

        var rolesForUsers = await userRoles.GetRolesForUsersAsync(
            [userId, userId, Guid.CreateVersion7()],
            CancellationToken.None);

        rolesForUsers.Should().ContainKey(userId);
        rolesForUsers[userId].Select(role => role.RoleId)
            .Should()
            .BeEquivalentTo([activeRoleId, inactiveRoleId]);
        rolesForUsers[userId].Should().ContainSingle(role => role.RoleId == inactiveRoleId && role.IsActive == false);

        var activeRoleUsers = await userRoles.GetUserIdsForRoleAsync(activeRoleId);
        activeRoleUsers.Should().ContainSingle(id => id == userId);

        var storedActivePermissions = await permissions.GetRolePermissionsAsync(activeRoleId);
        storedActivePermissions.Should().BeEquivalentTo([activeView, activeExecute]);

        var storedInactivePermissions = await permissions.GetRolePermissionsAsync(inactiveRoleId);
        storedInactivePermissions.Should().BeEquivalentTo([inactiveExport]);

        var effective = await permissions.GetEffectivePermissionsAsync(userId);
        effective.Should().Contain(activeView);
        effective.Should().Contain(activeExecute);
        effective.Should().NotContain(inactiveExport);

        var version = await versions.GetAsync(userId);
        version.Should().NotBeNull();
        version!.Version.Should().BeGreaterThanOrEqualTo(1);

        await uow.ExecuteInUowTransactionAsync(
            ct => users.SetActiveAsync(userId, isActive: false, ct),
            CancellationToken.None);

        var inactiveUserEffective = await permissions.GetEffectivePermissionsAsync(userId);
        inactiveUserEffective.Should().BeEmpty();
    }
}

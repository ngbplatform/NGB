using NGB.Core.Security;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Security;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Definitions;
using NGB.PropertyManagement.Security;

namespace NGB.PropertyManagement.Runtime.Security;

public sealed class PropertyManagementSecuritySetupService(
    IUnitOfWork uow,
    IPlatformUserRepository users,
    IPlatformRoleRepository roles,
    IPlatformUserRoleRepository userRoles,
    IUserAccessVersionRepository versions,
    IPermissionSnapshotRepository permissions,
    PropertyManagementDemoAdministratorOptions demoAdministrator)
    : IPropertyManagementSecuritySetupService
{
    private const string AdministratorRoleCode = "pm-administrator";

    public async Task EnsureDefaultsAsync(CancellationToken ct = default)
    {
        var existingRolesByCode = (await roles.GetAllAsync(ct))
            .ToDictionary(static role => role.Code, StringComparer.OrdinalIgnoreCase);

        await uow.BeginTransactionAsync(ct);
        try
        {
            foreach (var roleDefault in PropertyManagementSecurityDefaults.Roles)
            {
                existingRolesByCode.TryGetValue(roleDefault.Code, out var existing);

                var role = await roles.UpsertAsync(
                    existing?.RoleId ?? Guid.CreateVersion7(),
                    roleDefault.Code,
                    roleDefault.Name,
                    roleDefault.Description,
                    isSystem: true,
                    isActive: true,
                    ct);

                await permissions.ReplaceRolePermissionsAsync(
                    role.RoleId,
                    roleDefault.Permissions
                        .Select(static x => new NgbPermissionKey(x.ResourceKind, x.ResourceCode, x.ActionCode))
                        .Distinct()
                        .ToArray(),
                    ct);
            }

            await uow.CommitAsync(ct);
        }
        catch
        {
            await uow.RollbackAsync(CancellationToken.None);
            throw;
        }

        await EnsureDemoAdministratorAsync(ct);
    }

    private async Task EnsureDemoAdministratorAsync(CancellationToken ct)
    {
        var adminRole = await roles.GetByCodeAsync(AdministratorRoleCode, ct);
        if (adminRole is null)
            return;

        var adminEmail = demoAdministrator.Email;
        var adminAuthSubject = demoAdministrator.AuthSubject;
        var adminDisplayName = demoAdministrator.DisplayName;

        var candidateIds = new HashSet<Guid>();

        if (!string.IsNullOrWhiteSpace(adminAuthSubject))
        {
            await uow.BeginTransactionAsync(ct);
            try
            {
                var userId = await users.UpsertAsync(
                    adminAuthSubject,
                    adminEmail,
                    string.IsNullOrWhiteSpace(adminDisplayName) ? adminEmail : adminDisplayName,
                    isActive: true,
                    ct);

                await versions.GetOrCreateAsync(userId, ct);
                await uow.CommitAsync(ct);
                candidateIds.Add(userId);
            }
            catch
            {
                await uow.RollbackAsync(CancellationToken.None);
                throw;
            }
        }

        if (!string.IsNullOrWhiteSpace(adminEmail))
        {
            var existingUsers = await users.GetByEmailsAsync([adminEmail], ct);
            foreach (var user in existingUsers)
            {
                candidateIds.Add(user.UserId);
            }
        }

        foreach (var userId in candidateIds)
        {
            await EnsureUserHasRoleAsync(userId, adminRole.RoleId, ct);
        }
    }

    private async Task EnsureUserHasRoleAsync(Guid userId, Guid roleId, CancellationToken ct)
    {
        var existingRoles = await userRoles.GetRolesForUserAsync(userId, ct);
        if (existingRoles.Any(role => role.RoleId == roleId))
            return;

        await uow.BeginTransactionAsync(ct);
        try
        {
            var nextRoleIds = existingRoles
                .Select(static role => role.RoleId)
                .Append(roleId)
                .Distinct()
                .ToArray();

            await userRoles.ReplaceUserRolesAsync(userId, nextRoleIds, assignedByUserId: null, ct);

            if (await versions.GetAsync(userId, ct) is null)
                await versions.GetOrCreateAsync(userId, ct);
            else
                await versions.IncrementAsync(userId, ct);

            await uow.CommitAsync(ct);
        }
        catch
        {
            await uow.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}

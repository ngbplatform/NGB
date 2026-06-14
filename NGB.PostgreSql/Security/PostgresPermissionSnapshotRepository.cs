using Dapper;
using NGB.Core.Security;
using NGB.Persistence.Security;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.UnitOfWork;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.Security;

public sealed class PostgresPermissionSnapshotRepository(IUnitOfWork uow, TimeProvider timeProvider)
    : IPermissionSnapshotRepository
{
    public async Task<IReadOnlyList<NgbPermissionKey>> GetEffectivePermissionsAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        userId.EnsureRequired(nameof(userId));
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT DISTINCT
                               rp.resource_kind AS ResourceKind,
                               rp.resource_code AS ResourceCode,
                               rp.action_code AS ActionCode
                           FROM platform_users u
                           JOIN platform_user_roles ur ON ur.user_id = u.user_id
                           JOIN platform_roles r ON r.role_id = ur.role_id
                           JOIN platform_role_permissions rp ON rp.role_id = r.role_id
                           WHERE u.user_id = @UserId
                             AND u.is_active = TRUE
                             AND r.is_active = TRUE;
                           """;

        var cmd = new CommandDefinition(sql, new { UserId = userId }, transaction: uow.Transaction, cancellationToken: ct);
        var rows = await uow.Connection.QueryAsync<PermissionRow>(cmd);
        return rows.Select(static x => new NgbPermissionKey(x.ResourceKind, x.ResourceCode, x.ActionCode)).ToArray();
    }

    public async Task<IReadOnlyList<NgbPermissionKey>> GetRolePermissionsAsync(
        Guid roleId,
        CancellationToken ct = default)
    {
        roleId.EnsureRequired(nameof(roleId));
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT
                               resource_kind AS ResourceKind,
                               resource_code AS ResourceCode,
                               action_code AS ActionCode
                           FROM platform_role_permissions
                           WHERE role_id = @RoleId
                           ORDER BY resource_kind, resource_code, action_code;
                           """;

        var cmd = new CommandDefinition(sql, new { RoleId = roleId }, transaction: uow.Transaction, cancellationToken: ct);
        var rows = await uow.Connection.QueryAsync<PermissionRow>(cmd);
        return rows.Select(static x => new NgbPermissionKey(x.ResourceKind, x.ResourceCode, x.ActionCode)).ToArray();
    }

    public async Task ReplaceRolePermissionsAsync(
        Guid roleId,
        IReadOnlyList<NgbPermissionKey> permissions,
        CancellationToken ct = default)
    {
        roleId.EnsureRequired(nameof(roleId));

        if (permissions is null)
            throw new ArgumentNullException(nameof(permissions));

        await uow.EnsureOpenForTransactionAsync(ct);

        const string deleteSql = """
                                 DELETE FROM platform_role_permissions
                                 WHERE role_id = @RoleId;
                                 """;

        var deleteCmd = new CommandDefinition(
            deleteSql,
            new { RoleId = roleId },
            transaction: uow.Transaction,
            cancellationToken: ct);

        await uow.Connection.ExecuteAsync(deleteCmd);

        var distinct = permissions.Distinct().ToArray();
        if (distinct.Length == 0)
            return;

        var nowUtc = timeProvider.GetUtcNowDateTime();
        nowUtc.EnsureUtc(nameof(nowUtc));

        const string insertSql = """
                                 INSERT INTO platform_role_permissions
                                 (role_id, resource_kind, resource_code, action_code, created_at_utc)
                                 SELECT @RoleId, p.resource_kind, p.resource_code, p.action_code, @NowUtc
                                 FROM unnest(
                                     @ResourceKinds::text[],
                                     @ResourceCodes::text[],
                                     @ActionCodes::text[]) AS p(resource_kind, resource_code, action_code)
                                 ON CONFLICT (role_id, resource_kind, resource_code, action_code) DO NOTHING;
                                 """;

        var insertCmd = new CommandDefinition(
            insertSql,
            new
            {
                RoleId = roleId,
                ResourceKinds = distinct.Select(static x => x.ResourceKind).ToArray(),
                ResourceCodes = distinct.Select(static x => x.ResourceCode).ToArray(),
                ActionCodes = distinct.Select(static x => x.ActionCode).ToArray(),
                NowUtc = nowUtc
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        await uow.Connection.ExecuteAsync(insertCmd);
    }

    private sealed record PermissionRow(string ResourceKind, string ResourceCode, string ActionCode);
}

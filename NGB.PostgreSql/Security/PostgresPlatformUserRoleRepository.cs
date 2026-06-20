using Dapper;
using NGB.Core.Security;
using NGB.Persistence.Security;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.UnitOfWork;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.Security;

public sealed class PostgresPlatformUserRoleRepository(IUnitOfWork uow, TimeProvider timeProvider)
    : IPlatformUserRoleRepository
{
    public async Task<IReadOnlyList<PlatformRole>> GetRolesForUserAsync(Guid userId, CancellationToken ct = default)
    {
        userId.EnsureRequired(nameof(userId));
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT
                               r.role_id AS RoleId,
                               r.code AS Code,
                               r.name AS Name,
                               r.description AS Description,
                               r.is_system AS IsSystem,
                               r.is_active AS IsActive,
                               r.created_at_utc AS CreatedAtUtc,
                               r.updated_at_utc AS UpdatedAtUtc
                           FROM platform_user_roles ur
                           JOIN platform_roles r ON r.role_id = ur.role_id
                           WHERE ur.user_id = @UserId
                           ORDER BY lower(trim(r.code));
                           """;

        var cmd = new CommandDefinition(sql, new { UserId = userId }, transaction: uow.Transaction, cancellationToken: ct);
        return (await uow.Connection.QueryAsync<PlatformRole>(cmd)).AsList();
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<PlatformRole>>> GetRolesForUsersAsync(
        IReadOnlyList<Guid> userIds,
        CancellationToken ct = default)
    {
        if (userIds is null)
            throw new ArgumentNullException(nameof(userIds));

        var distinct = userIds.Where(static x => x != Guid.Empty).Distinct().ToArray();
        if (distinct.Length == 0)
            return new Dictionary<Guid, IReadOnlyList<PlatformRole>>();

        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT
                               ur.user_id AS UserId,
                               r.role_id AS RoleId,
                               r.code AS Code,
                               r.name AS Name,
                               r.description AS Description,
                               r.is_system AS IsSystem,
                               r.is_active AS IsActive,
                               r.created_at_utc AS CreatedAtUtc,
                               r.updated_at_utc AS UpdatedAtUtc
                           FROM platform_user_roles ur
                           JOIN platform_roles r ON r.role_id = ur.role_id
                           WHERE ur.user_id = ANY(@UserIds)
                           ORDER BY ur.user_id, lower(trim(r.code));
                           """;

        var cmd = new CommandDefinition(sql, new { UserIds = distinct }, transaction: uow.Transaction, cancellationToken: ct);
        var rows = (await uow.Connection.QueryAsync<UserRoleRow>(cmd)).AsList();

        return rows
            .GroupBy(x => x.UserId)
            .ToDictionary(
                x => x.Key,
                x => (IReadOnlyList<PlatformRole>)x
                    .Select(r => new PlatformRole(
                        r.RoleId,
                        r.Code,
                        r.Name,
                        r.Description,
                        r.IsSystem,
                        r.IsActive,
                        r.CreatedAtUtc,
                        r.UpdatedAtUtc))
                    .ToArray());
    }

    public async Task<IReadOnlyList<Guid>> GetUserIdsForRoleAsync(Guid roleId, CancellationToken ct = default)
    {
        roleId.EnsureRequired(nameof(roleId));
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT user_id
                           FROM platform_user_roles
                           WHERE role_id = @RoleId
                           ORDER BY user_id;
                           """;

        var cmd = new CommandDefinition(sql, new { RoleId = roleId }, transaction: uow.Transaction, cancellationToken: ct);
        return (await uow.Connection.QueryAsync<Guid>(cmd)).AsList();
    }

    public async Task ReplaceUserRolesAsync(
        Guid userId,
        IReadOnlyList<Guid> roleIds,
        Guid? assignedByUserId,
        CancellationToken ct = default)
    {
        userId.EnsureRequired(nameof(userId));

        if (roleIds is null)
            throw new ArgumentNullException(nameof(roleIds));

        await uow.EnsureOpenForTransactionAsync(ct);

        const string deleteSql = """
                                 DELETE FROM platform_user_roles
                                 WHERE user_id = @UserId;
                                 """;

        var deleteCmd = new CommandDefinition(
            deleteSql,
            new { UserId = userId },
            transaction: uow.Transaction,
            cancellationToken: ct);

        await uow.Connection.ExecuteAsync(deleteCmd);

        var distinctRoleIds = roleIds.Where(static x => x != Guid.Empty).Distinct().ToArray();
        if (distinctRoleIds.Length == 0)
            return;

        var nowUtc = timeProvider.GetUtcNowDateTime();
        nowUtc.EnsureUtc(nameof(nowUtc));

        const string insertSql = """
                                 INSERT INTO platform_user_roles
                                 (user_id, role_id, assigned_at_utc, assigned_by_user_id)
                                 SELECT @UserId, role_id, @NowUtc, @AssignedByUserId
                                 FROM unnest(@RoleIds::uuid[]) AS x(role_id)
                                 ON CONFLICT (user_id, role_id) DO NOTHING;
                                 """;

        var insertCmd = new CommandDefinition(
            insertSql,
            new
            {
                UserId = userId,
                RoleIds = distinctRoleIds,
                NowUtc = nowUtc,
                AssignedByUserId = assignedByUserId
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        await uow.Connection.ExecuteAsync(insertCmd);
    }

    private sealed record UserRoleRow(
        Guid UserId,
        Guid RoleId,
        string Code,
        string Name,
        string? Description,
        bool IsSystem,
        bool IsActive,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc);
}

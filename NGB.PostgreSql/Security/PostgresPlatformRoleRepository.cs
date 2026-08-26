using Dapper;
using NGB.Core.Security;
using NGB.Persistence.Security;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.UnitOfWork;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.Security;

public sealed class PostgresPlatformRoleRepository(IUnitOfWork uow, TimeProvider timeProvider) : IPlatformRoleRepository
{
    public async Task<IReadOnlyList<PlatformRole>> GetAllAsync(CancellationToken ct = default)
    {
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT
                               role_id AS RoleId,
                               code AS Code,
                               name AS Name,
                               description AS Description,
                               is_system AS IsSystem,
                               is_active AS IsActive,
                               created_at_utc AS CreatedAtUtc,
                               updated_at_utc AS UpdatedAtUtc
                           FROM platform_roles
                           ORDER BY lower(trim(code));
                           """;

        var cmd = new CommandDefinition(sql, transaction: uow.Transaction, cancellationToken: ct);
        return (await uow.Connection.QueryAsync<PlatformRole>(cmd)).AsList();
    }

    public async Task<IReadOnlyList<PlatformRoleListRecord>> GetListAsync(int limit, CancellationToken ct = default)
    {
        if (limit is <= 0 or > 500)
            throw new NgbArgumentOutOfRangeException(nameof(limit), limit, "Argument is out of range.");

        await uow.EnsureConnectionOpenAsync(ct);
        const string sql = """
                           WITH selected_roles AS (
                               SELECT
                                   role_id,
                                   code,
                                   name,
                                   description,
                                   is_system,
                                   is_active,
                                   created_at_utc,
                                   updated_at_utc
                               FROM platform_roles
                               ORDER BY lower(trim(code)), role_id
                               LIMIT @Limit
                           )
                           SELECT
                               r.role_id AS RoleId,
                               r.code AS Code,
                               r.name AS Name,
                               r.description AS Description,
                               r.is_system AS IsSystem,
                               r.is_active AS IsActive,
                               r.created_at_utc AS CreatedAtUtc,
                               r.updated_at_utc AS UpdatedAtUtc,
                               count(ur.user_id)::int AS AssignedUserCount
                           FROM selected_roles r
                           LEFT JOIN platform_user_roles ur ON ur.role_id = r.role_id
                           GROUP BY
                               r.role_id, r.code, r.name, r.description, r.is_system,
                               r.is_active, r.created_at_utc, r.updated_at_utc
                           ORDER BY lower(trim(r.code)), r.role_id;
                           """;

        var rows = await uow.Connection.QueryAsync<RoleListRow>(new CommandDefinition(
            sql,
            new { Limit = limit },
            transaction: uow.Transaction,
            cancellationToken: ct));

        return rows
            .Select(static row => new PlatformRoleListRecord(row.ToRole(), row.AssignedUserCount))
            .ToArray();
    }

    public async Task<PlatformRole?> GetByIdAsync(Guid roleId, CancellationToken ct = default)
    {
        roleId.EnsureRequired(nameof(roleId));
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT
                               role_id AS RoleId,
                               code AS Code,
                               name AS Name,
                               description AS Description,
                               is_system AS IsSystem,
                               is_active AS IsActive,
                               created_at_utc AS CreatedAtUtc,
                               updated_at_utc AS UpdatedAtUtc
                           FROM platform_roles
                           WHERE role_id = @RoleId;
                           """;

        var cmd = new CommandDefinition(sql, new { RoleId = roleId }, transaction: uow.Transaction, cancellationToken: ct);
        return await uow.Connection.QuerySingleOrDefaultAsync<PlatformRole>(cmd);
    }

    public async Task<PlatformRole?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new NgbArgumentRequiredException(nameof(code));

        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT
                               role_id AS RoleId,
                               code AS Code,
                               name AS Name,
                               description AS Description,
                               is_system AS IsSystem,
                               is_active AS IsActive,
                               created_at_utc AS CreatedAtUtc,
                               updated_at_utc AS UpdatedAtUtc
                           FROM platform_roles
                           WHERE lower(trim(code)) = lower(trim(@Code))
                           LIMIT 1;
                           """;

        var cmd = new CommandDefinition(sql, new { Code = code }, transaction: uow.Transaction, cancellationToken: ct);
        return await uow.Connection.QuerySingleOrDefaultAsync<PlatformRole>(cmd);
    }

    public async Task<PlatformRole> UpsertAsync(
        Guid roleId,
        string code,
        string name,
        string? description,
        bool isSystem,
        bool isActive,
        CancellationToken ct = default)
    {
        roleId.EnsureRequired(nameof(roleId));

        if (string.IsNullOrWhiteSpace(code))
            throw new NgbArgumentRequiredException(nameof(code));

        if (string.IsNullOrWhiteSpace(name))
            throw new NgbArgumentRequiredException(nameof(name));

        await uow.EnsureOpenForTransactionAsync(ct);

        var nowUtc = timeProvider.GetUtcNowDateTime();
        nowUtc.EnsureUtc(nameof(nowUtc));

        const string sql = """
                           INSERT INTO platform_roles
                           (role_id, code, name, description, is_system, is_active, created_at_utc, updated_at_utc)
                           VALUES
                           (@RoleId, @Code, @Name, @Description, @IsSystem, @IsActive, @NowUtc, @NowUtc)
                           ON CONFLICT (role_id)
                           DO UPDATE SET
                               code = EXCLUDED.code,
                               name = EXCLUDED.name,
                               description = EXCLUDED.description,
                               is_system = EXCLUDED.is_system,
                               is_active = EXCLUDED.is_active,
                               updated_at_utc = EXCLUDED.updated_at_utc
                           RETURNING
                               role_id AS RoleId,
                               code AS Code,
                               name AS Name,
                               description AS Description,
                               is_system AS IsSystem,
                               is_active AS IsActive,
                               created_at_utc AS CreatedAtUtc,
                               updated_at_utc AS UpdatedAtUtc;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new
            {
                RoleId = roleId,
                Code = code.Trim().ToLowerInvariant(),
                Name = name.Trim(),
                Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                IsSystem = isSystem,
                IsActive = isActive,
                NowUtc = nowUtc
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        return await uow.Connection.QuerySingleAsync<PlatformRole>(cmd);
    }

    public async Task SetActiveAsync(Guid roleId, bool isActive, CancellationToken ct = default)
    {
        roleId.EnsureRequired(nameof(roleId));
        await uow.EnsureOpenForTransactionAsync(ct);

        var nowUtc = timeProvider.GetUtcNowDateTime();
        nowUtc.EnsureUtc(nameof(nowUtc));

        const string sql = """
                           UPDATE platform_roles
                           SET is_active = @IsActive,
                               updated_at_utc = @NowUtc
                           WHERE role_id = @RoleId;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new { RoleId = roleId, IsActive = isActive, NowUtc = nowUtc },
            transaction: uow.Transaction,
            cancellationToken: ct);

        await uow.Connection.ExecuteAsync(cmd);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetAssignedUserCountsAsync(CancellationToken ct = default)
    {
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT role_id AS RoleId, count(*)::int AS Count
                           FROM platform_user_roles
                           GROUP BY role_id;
                           """;

        var cmd = new CommandDefinition(sql, transaction: uow.Transaction, cancellationToken: ct);
        var rows = await uow.Connection.QueryAsync<RoleUserCountRow>(cmd);
        return rows.ToDictionary(x => x.RoleId, x => x.Count);
    }

    private sealed record RoleUserCountRow(Guid RoleId, int Count);

    private sealed record RoleListRow(
        Guid RoleId,
        string Code,
        string Name,
        string? Description,
        bool IsSystem,
        bool IsActive,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc,
        int AssignedUserCount)
    {
        public PlatformRole ToRole() => new(
            RoleId,
            Code,
            Name,
            Description,
            IsSystem,
            IsActive,
            CreatedAtUtc,
            UpdatedAtUtc);
    }
}

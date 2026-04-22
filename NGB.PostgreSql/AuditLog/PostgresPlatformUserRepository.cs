using Dapper;
using NGB.Core.AuditLog;
using NGB.Persistence.AuditLog;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.UnitOfWork;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.AuditLog;

public sealed class PostgresPlatformUserRepository(IUnitOfWork uow, TimeProvider timeProvider) : IPlatformUserRepository
{
    public async Task<Guid> UpsertAsync(
        string authSubject,
        string? email,
        string? displayName,
        bool isActive,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(authSubject))
            throw new NgbArgumentRequiredException(nameof(authSubject));

        await uow.EnsureOpenForTransactionAsync(ct);

        var nowUtc = timeProvider.GetUtcNowDateTime();
        nowUtc.EnsureUtc(nameof(nowUtc));

        const string sql = """
                           INSERT INTO platform_users
                           (user_id, auth_subject, email, display_name, is_active, created_at_utc, updated_at_utc)
                           VALUES
                           (@UserId, @AuthSubject, @Email, @DisplayName, @IsActive, @NowUtc, @NowUtc)
                           ON CONFLICT (auth_subject)
                           DO UPDATE SET
                               email = EXCLUDED.email,
                               display_name = EXCLUDED.display_name,
                               is_active = EXCLUDED.is_active,
                               updated_at_utc = EXCLUDED.updated_at_utc
                           RETURNING user_id;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new
            {
                UserId = Guid.CreateVersion7(),
                AuthSubject = authSubject.Trim(),
                Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim(),
                IsActive = isActive,
                NowUtc = nowUtc
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        return await uow.Connection.ExecuteScalarAsync<Guid>(cmd);
    }

    public async Task<PlatformUser?> GetByAuthSubjectAsync(string authSubject, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(authSubject))
            throw new NgbArgumentRequiredException(nameof(authSubject));

        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT
                               user_id AS UserId,
                               auth_subject AS AuthSubject,
                               email AS Email,
                               display_name AS DisplayName,
                               is_active AS IsActive,
                               created_at_utc AS CreatedAtUtc,
                               updated_at_utc AS UpdatedAtUtc
                           FROM platform_users
                           WHERE auth_subject = @AuthSubject;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new { AuthSubject = authSubject.Trim() },
            transaction: uow.Transaction,
            cancellationToken: ct);

        return await uow.Connection.QuerySingleOrDefaultAsync<PlatformUser>(cmd);
    }

    public async Task<IReadOnlyDictionary<Guid, PlatformUser>> GetByIdsAsync(IReadOnlyList<Guid> userIds, CancellationToken ct = default)
    {
        if (userIds is null)
            throw new NgbArgumentRequiredException(nameof(userIds));

        var distinct = userIds.Where(x => x != Guid.Empty).Distinct().ToArray();
        if (distinct.Length == 0)
            return new Dictionary<Guid, PlatformUser>();

        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT
                               user_id AS UserId,
                               auth_subject AS AuthSubject,
                               email AS Email,
                               display_name AS DisplayName,
                               is_active AS IsActive,
                               created_at_utc AS CreatedAtUtc,
                               updated_at_utc AS UpdatedAtUtc
                           FROM platform_users
                           WHERE user_id = ANY(@UserIds);
                           """;

        var cmd = new CommandDefinition(
            sql,
            new { UserIds = distinct },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var rows = (await uow.Connection.QueryAsync<PlatformUser>(cmd)).AsList();
        return rows.ToDictionary(x => x.UserId);
    }
}

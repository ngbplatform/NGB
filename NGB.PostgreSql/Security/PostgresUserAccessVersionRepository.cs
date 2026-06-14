using Dapper;
using NGB.Core.Security;
using NGB.Persistence.Security;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.UnitOfWork;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.Security;

public sealed class PostgresUserAccessVersionRepository(IUnitOfWork uow, TimeProvider timeProvider)
    : IUserAccessVersionRepository
{
    public async Task<PlatformUserAccessVersion?> GetAsync(Guid userId, CancellationToken ct = default)
    {
        userId.EnsureRequired(nameof(userId));
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT
                               user_id AS UserId,
                               version AS Version,
                               updated_at_utc AS UpdatedAtUtc
                           FROM platform_user_access_versions
                           WHERE user_id = @UserId;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new { UserId = userId },
            transaction: uow.Transaction,
            cancellationToken: ct);

        return await uow.Connection.QuerySingleOrDefaultAsync<PlatformUserAccessVersion>(cmd);
    }

    public async Task<PlatformUserAccessVersion> GetOrCreateAsync(Guid userId, CancellationToken ct = default)
    {
        userId.EnsureRequired(nameof(userId));
        await uow.EnsureOpenForTransactionAsync(ct);

        var nowUtc = timeProvider.GetUtcNowDateTime();
        nowUtc.EnsureUtc(nameof(nowUtc));

        const string sql = """
                           INSERT INTO platform_user_access_versions
                           (user_id, version, updated_at_utc)
                           VALUES
                           (@UserId, 1, @NowUtc)
                           ON CONFLICT (user_id)
                           DO UPDATE SET user_id = EXCLUDED.user_id
                           RETURNING
                               user_id AS UserId,
                               version AS Version,
                               updated_at_utc AS UpdatedAtUtc;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new { UserId = userId, NowUtc = nowUtc },
            transaction: uow.Transaction,
            cancellationToken: ct);

        return await uow.Connection.QuerySingleAsync<PlatformUserAccessVersion>(cmd);
    }

    public async Task<PlatformUserAccessVersion> IncrementAsync(Guid userId, CancellationToken ct = default)
    {
        userId.EnsureRequired(nameof(userId));
        await uow.EnsureOpenForTransactionAsync(ct);

        var nowUtc = timeProvider.GetUtcNowDateTime();
        nowUtc.EnsureUtc(nameof(nowUtc));

        const string sql = """
                           INSERT INTO platform_user_access_versions
                           (user_id, version, updated_at_utc)
                           VALUES
                           (@UserId, 2, @NowUtc)
                           ON CONFLICT (user_id)
                           DO UPDATE SET
                               version = platform_user_access_versions.version + 1,
                               updated_at_utc = EXCLUDED.updated_at_utc
                           RETURNING
                               user_id AS UserId,
                               version AS Version,
                               updated_at_utc AS UpdatedAtUtc;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new { UserId = userId, NowUtc = nowUtc },
            transaction: uow.Transaction,
            cancellationToken: ct);

        return await uow.Connection.QuerySingleAsync<PlatformUserAccessVersion>(cmd);
    }

    public async Task IncrementManyAsync(IReadOnlyList<Guid> userIds, CancellationToken ct = default)
    {
        if (userIds is null)
            throw new ArgumentNullException(nameof(userIds));

        var distinct = userIds.Where(static x => x != Guid.Empty).Distinct().ToArray();
        if (distinct.Length == 0)
            return;

        await uow.EnsureOpenForTransactionAsync(ct);

        var nowUtc = timeProvider.GetUtcNowDateTime();
        nowUtc.EnsureUtc(nameof(nowUtc));

        const string sql = """
                           INSERT INTO platform_user_access_versions
                           (user_id, version, updated_at_utc)
                           SELECT user_id, 2, @NowUtc
                           FROM unnest(@UserIds::uuid[]) AS x(user_id)
                           ON CONFLICT (user_id)
                           DO UPDATE SET
                               version = platform_user_access_versions.version + 1,
                               updated_at_utc = EXCLUDED.updated_at_utc;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new { UserIds = distinct, NowUtc = nowUtc },
            transaction: uow.Transaction,
            cancellationToken: ct);

        await uow.Connection.ExecuteAsync(cmd);
    }
}

using Dapper;
using NGB.Contracts.Common;
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
                           WITH email_lock AS (
                               SELECT 1 AS locked
                               WHERE @NormalizedEmail IS NULL

                               UNION ALL

                               SELECT 1 AS locked
                               FROM (SELECT pg_advisory_xact_lock(hashtextextended('platform_users.email:' || @NormalizedEmail, 0))) l
                               WHERE @NormalizedEmail IS NOT NULL
                           ),
                           matched AS (
                               SELECT
                                   u.user_id,
                                   u.auth_subject,
                                   u.email,
                                   u.display_name,
                                   u.is_active
                               FROM platform_users u
                               CROSS JOIN email_lock
                               WHERE u.auth_subject = @AuthSubject
                                  OR (@NormalizedEmail IS NOT NULL AND lower(trim(u.email)) = @NormalizedEmail)
                               ORDER BY
                                   CASE WHEN u.auth_subject = @AuthSubject THEN 0 ELSE 1 END,
                                   u.updated_at_utc DESC,
                                   u.user_id
                               LIMIT 1
                           ),
                           updated AS (
                               UPDATE platform_users u
                               SET auth_subject = @AuthSubject,
                                   email = @Email,
                                   display_name = @DisplayName,
                                   is_active = @IsActive,
                                   updated_at_utc = @NowUtc
                               FROM matched m
                               WHERE u.user_id = m.user_id
                                 AND (m.auth_subject IS DISTINCT FROM @AuthSubject
                                   OR m.email IS DISTINCT FROM @Email
                                   OR m.display_name IS DISTINCT FROM @DisplayName
                                   OR m.is_active IS DISTINCT FROM @IsActive)
                               RETURNING u.user_id
                           ),
                           unchanged AS (
                               SELECT m.user_id
                               FROM matched m
                               WHERE NOT EXISTS (SELECT 1 FROM updated)
                           ),
                           inserted AS (
                               INSERT INTO platform_users
                               (user_id, auth_subject, email, display_name, is_active, created_at_utc, updated_at_utc)
                               SELECT @UserId, @AuthSubject, @Email, @DisplayName, @IsActive, @NowUtc, @NowUtc
                               FROM email_lock
                               WHERE NOT EXISTS (SELECT 1 FROM matched)
                               ON CONFLICT (auth_subject)
                               DO UPDATE SET
                                   email = EXCLUDED.email,
                                   display_name = EXCLUDED.display_name,
                                   is_active = EXCLUDED.is_active,
                                   updated_at_utc = EXCLUDED.updated_at_utc
                               RETURNING user_id
                           )
                           SELECT user_id FROM updated
                           UNION ALL
                           SELECT user_id FROM unchanged
                           UNION ALL
                           SELECT user_id FROM inserted
                           LIMIT 1;
                           """;

        var normalizedEmail = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

        var cmd = new CommandDefinition(
            sql,
            new
            {
                UserId = Guid.CreateVersion7(),
                AuthSubject = authSubject.Trim(),
                Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
                NormalizedEmail = normalizedEmail,
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

    public async Task<PlatformUser?> GetByIdAsync(Guid userId, CancellationToken ct = default)
    {
        userId.EnsureRequired(nameof(userId));

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
                           WHERE user_id = @UserId;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new { UserId = userId },
            transaction: uow.Transaction,
            cancellationToken: ct);

        return await uow.Connection.QuerySingleOrDefaultAsync<PlatformUser>(cmd);
    }

    public async Task<PlatformUserPage> GetPageAsync(
        int offset,
        int limit,
        bool? isActive,
        CancellationToken ct = default)
        => await GetPageCoreAsync(offset, limit, isActive, null, false, ct);

    public async Task<PlatformUserPage> GetCursorPageAsync(
        PlatformUserPageCursor cursor,
        int limit,
        bool? isActive,
        CancellationToken ct = default)
        => await GetPageCoreAsync(cursor.Offset, limit, isActive, cursor, true, ct);

    private async Task<PlatformUserPage> GetPageCoreAsync(
        int offset,
        int limit,
        bool? isActive,
        PlatformUserPageCursor? cursor,
        bool cursorPaging,
        CancellationToken ct)
    {
        if (offset < 0)
            throw new NgbArgumentOutOfRangeException(nameof(offset), offset, "Offset must be non-negative.");

        if (limit <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(limit), limit, "Limit must be positive.");

        offset = PagingLimits.BoundOffset(offset);

        await uow.EnsureConnectionOpenAsync(ct);

        var useSeek = cursor is { AfterSortKey: not null, AfterUserId: not null };
        var seekSql = useSeek
            ? "AND (lower(coalesce(display_name, email, auth_subject)), user_id) > (@AfterSortKey::text, @AfterUserId::uuid)"
            : string.Empty;
        var offsetSql = useSeek ? string.Empty : "OFFSET @Offset";
        var pageSql = $"""
                           SELECT
                               user_id AS UserId,
                               auth_subject AS AuthSubject,
                               email AS Email,
                               display_name AS DisplayName,
                               is_active AS IsActive,
                               created_at_utc AS CreatedAtUtc,
                               updated_at_utc AS UpdatedAtUtc,
                               lower(coalesce(display_name, email, auth_subject)) AS SortKey
                           FROM platform_users
                           WHERE (@IsActive IS NULL OR is_active = @IsActive)
                           {seekSql}
                           ORDER BY lower(coalesce(display_name, email, auth_subject)), user_id
                           {offsetSql}
                           LIMIT @Limit;
                           """;

        var parameters = new
        {
            Offset = offset,
            Limit = cursorPaging && limit < int.MaxValue ? limit + 1 : limit,
            IsActive = isActive,
            AfterSortKey = cursor?.AfterSortKey,
            AfterUserId = cursor?.AfterUserId
        };
        long total;
        List<PlatformUserPageRow> rows;
        if (cursor is null)
        {
            await using var grid = await uow.Connection.QueryMultipleAsync(new CommandDefinition(
                $"""
                 SELECT COUNT(*)::bigint
                 FROM platform_users
                 WHERE @IsActive IS NULL OR is_active = @IsActive;

                 {pageSql}
                 """,
                parameters,
                transaction: uow.Transaction,
                cancellationToken: ct));
            total = await grid.ReadSingleAsync<long>();
            rows = (await grid.ReadAsync<PlatformUserPageRow>()).AsList();
        }
        else
        {
            total = cursor.Total;
            rows = (await uow.Connection.QueryAsync<PlatformUserPageRow>(new CommandDefinition(
                pageSql,
                parameters,
                transaction: uow.Transaction,
                cancellationToken: ct))).AsList();
        }

        var hasMore = cursorPaging && rows.Count > limit;
        if (hasMore)
            rows.RemoveRange(limit, rows.Count - limit);

        var last = rows.LastOrDefault();
        return new PlatformUserPage(
            rows.Select(static row => row.ToPlatformUser()).ToArray(),
            total,
            hasMore,
            last?.SortKey,
            last?.UserId);
    }

    private sealed class PlatformUserPageRow
    {
        public Guid UserId { get; init; }
        public string AuthSubject { get; init; } = string.Empty;
        public string? Email { get; init; }
        public string? DisplayName { get; init; }
        public bool IsActive { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public DateTime UpdatedAtUtc { get; init; }
        public string SortKey { get; init; } = string.Empty;

        public PlatformUser ToPlatformUser()
            => new(UserId, AuthSubject, Email, DisplayName, IsActive, CreatedAtUtc, UpdatedAtUtc);
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

    public async Task<IReadOnlyList<PlatformUser>> GetByEmailsAsync(
        IReadOnlyList<string> emails,
        CancellationToken ct = default)
    {
        if (emails is null)
            throw new NgbArgumentRequiredException(nameof(emails));

        var normalized = emails
            .Where(static email => !string.IsNullOrWhiteSpace(email))
            .Select(static email => email.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (normalized.Length == 0)
            return [];

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
                           WHERE lower(trim(email)) = ANY(@Emails)
                           ORDER BY lower(trim(email)), user_id;
                           """;

        return (await uow.Connection.QueryAsync<PlatformUser>(new CommandDefinition(
            sql,
            new { Emails = normalized },
            transaction: uow.Transaction,
            cancellationToken: ct))).AsList();
    }

    public async Task SetActiveAsync(Guid userId, bool isActive, CancellationToken ct = default)
    {
        userId.EnsureRequired(nameof(userId));

        await uow.EnsureOpenForTransactionAsync(ct);

        var nowUtc = timeProvider.GetUtcNowDateTime();
        nowUtc.EnsureUtc(nameof(nowUtc));

        const string sql = """
                           UPDATE platform_users
                           SET is_active = @IsActive,
                               updated_at_utc = @NowUtc
                           WHERE user_id = @UserId;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new
            {
                UserId = userId,
                IsActive = isActive,
                NowUtc = nowUtc
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        await uow.Connection.ExecuteAsync(cmd);
    }
}

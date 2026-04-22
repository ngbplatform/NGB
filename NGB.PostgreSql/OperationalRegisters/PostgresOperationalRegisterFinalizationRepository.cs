using Dapper;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.UnitOfWork;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.OperationalRegisters;

public sealed class PostgresOperationalRegisterFinalizationRepository(IUnitOfWork uow)
    : IOperationalRegisterFinalizationRepository
{
    public async Task<OperationalRegisterFinalization?> GetAsync(
        Guid registerId,
        DateOnly period,
        CancellationToken ct = default)
    {
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT
                               register_id      AS "RegisterId",
                               period           AS "Period",
                               status           AS "Status",
                               finalized_at_utc AS "FinalizedAtUtc",
                               dirty_since_utc  AS "DirtySinceUtc",
                               blocked_since_utc AS "BlockedSinceUtc",
                               blocked_reason    AS "BlockedReason",
                               created_at_utc   AS "CreatedAtUtc",
                               updated_at_utc   AS "UpdatedAtUtc"
                           FROM operational_register_finalizations
                           WHERE register_id = @RegisterId AND period = @Period
                           LIMIT 1;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new { RegisterId = registerId, Period = period },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var row = await uow.Connection.QuerySingleOrDefaultAsync<Row>(cmd);
        return row?.ToFinalization();
    }

    public async Task MarkFinalizedAsync(
        Guid registerId,
        DateOnly period,
        DateTime finalizedAtUtc,
        DateTime nowUtc,
        CancellationToken ct = default)
    {
        EnsureValidKey(registerId, period);
        finalizedAtUtc.EnsureUtc(nameof(finalizedAtUtc));
        nowUtc.EnsureUtc(nameof(nowUtc));

        await uow.EnsureOpenForTransactionAsync(ct);

        const string sql = """
                           INSERT INTO operational_register_finalizations(
                               register_id,
                               period,
                               status,
                               finalized_at_utc,
                               dirty_since_utc,
                               blocked_since_utc,
                               blocked_reason,
                               created_at_utc,
                               updated_at_utc
                           )
                           VALUES (
                               @RegisterId,
                               @Period,
                               @Status,
                               @FinalizedAtUtc,
                               NULL,
                               NULL,
                               NULL,
                               @NowUtc,
                               @NowUtc
                           )
                           ON CONFLICT (register_id, period) DO UPDATE
                           SET status = EXCLUDED.status,
                               finalized_at_utc = EXCLUDED.finalized_at_utc,
                               dirty_since_utc = NULL,
                               blocked_since_utc = NULL,
                               blocked_reason = NULL,
                               updated_at_utc = EXCLUDED.updated_at_utc;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new
            {
                RegisterId = registerId,
                Period = period,
                Status = (short)OperationalRegisterFinalizationStatus.Finalized,
                FinalizedAtUtc = finalizedAtUtc,
                NowUtc = nowUtc
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        await uow.Connection.ExecuteAsync(cmd);
    }

    public async Task MarkDirtyAsync(
        Guid registerId,
        DateOnly period,
        DateTime dirtySinceUtc,
        DateTime nowUtc,
        CancellationToken ct = default)
    {
        EnsureValidKey(registerId, period);
        dirtySinceUtc.EnsureUtc(nameof(dirtySinceUtc));
        nowUtc.EnsureUtc(nameof(nowUtc));

        await uow.EnsureOpenForTransactionAsync(ct);

        const string sql = """
                           INSERT INTO operational_register_finalizations(
                               register_id,
                               period,
                               status,
                               finalized_at_utc,
                               dirty_since_utc,
                               blocked_since_utc,
                               blocked_reason,
                               created_at_utc,
                               updated_at_utc
                           )
                           VALUES (
                               @RegisterId,
                               @Period,
                               @Status,
                               NULL,
                               @DirtySinceUtc,
                               NULL,
                               NULL,
                               @NowUtc,
                               @NowUtc
                           )
                           ON CONFLICT (register_id, period) DO UPDATE
                           SET status = EXCLUDED.status,
                               finalized_at_utc = NULL,
                               dirty_since_utc = EXCLUDED.dirty_since_utc,
                               blocked_since_utc = NULL,
                               blocked_reason = NULL,
                               updated_at_utc = EXCLUDED.updated_at_utc;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new
            {
                RegisterId = registerId,
                Period = period,
                Status = (short)OperationalRegisterFinalizationStatus.Dirty,
                DirtySinceUtc = dirtySinceUtc,
                NowUtc = nowUtc
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        await uow.Connection.ExecuteAsync(cmd);
    }

    public async Task MarkBlockedNoProjectorAsync(
        Guid registerId,
        DateOnly period,
        DateTime blockedSinceUtc,
        string blockedReason,
        DateTime nowUtc,
        CancellationToken ct = default)
    {
        EnsureValidKey(registerId, period);
        blockedSinceUtc.EnsureUtc(nameof(blockedSinceUtc));
        nowUtc.EnsureUtc(nameof(nowUtc));

        if (string.IsNullOrWhiteSpace(blockedReason))
            throw new NgbArgumentRequiredException(nameof(blockedReason));

        // Keep the reason concise: it's primarily for diagnostics and admin UX.
        if (blockedReason.Length > 128)
            throw new NgbArgumentOutOfRangeException(nameof(blockedReason), blockedReason, "BlockedReason must be <= 128 characters.");

        await uow.EnsureOpenForTransactionAsync(ct);

        const string sql = """
                           INSERT INTO operational_register_finalizations(
                               register_id,
                               period,
                               status,
                               finalized_at_utc,
                               dirty_since_utc,
                               blocked_since_utc,
                               blocked_reason,
                               created_at_utc,
                               updated_at_utc
                           )
                           VALUES (
                               @RegisterId,
                               @Period,
                               @Status,
                               NULL,
                               NULL,
                               @BlockedSinceUtc,
                               @BlockedReason,
                               @NowUtc,
                               @NowUtc
                           )
                           ON CONFLICT (register_id, period) DO UPDATE
                           SET status = EXCLUDED.status,
                               finalized_at_utc = NULL,
                               dirty_since_utc = NULL,
                               blocked_since_utc = EXCLUDED.blocked_since_utc,
                               blocked_reason = EXCLUDED.blocked_reason,
                               updated_at_utc = EXCLUDED.updated_at_utc;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new
            {
                RegisterId = registerId,
                Period = period,
                Status = (short)OperationalRegisterFinalizationStatus.BlockedNoProjector,
                BlockedSinceUtc = blockedSinceUtc,
                BlockedReason = blockedReason,
                NowUtc = nowUtc
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        await uow.Connection.ExecuteAsync(cmd);
    }

    public async Task<IReadOnlyList<OperationalRegisterFinalization>> GetDirtyAsync(
        Guid registerId,
        int limit = 100,
        CancellationToken ct = default)
    {
        if (registerId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(registerId), "RegisterId must not be empty.");

        if (limit <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(limit), limit, "Limit must be positive.");

        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT
                               register_id      AS "RegisterId",
                               period           AS "Period",
                               status           AS "Status",
                               finalized_at_utc AS "FinalizedAtUtc",
                               dirty_since_utc  AS "DirtySinceUtc",
                               blocked_since_utc AS "BlockedSinceUtc",
                               blocked_reason    AS "BlockedReason",
                               created_at_utc   AS "CreatedAtUtc",
                               updated_at_utc   AS "UpdatedAtUtc"
                           FROM operational_register_finalizations
                           WHERE register_id = @RegisterId
                             AND status = @DirtyStatus
                           ORDER BY period
                           LIMIT @Limit;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new
            {
                RegisterId = registerId,
                DirtyStatus = (short)OperationalRegisterFinalizationStatus.Dirty,
                Limit = limit
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var rows = await uow.Connection.QueryAsync<Row>(cmd);
        return rows.Select(r => r.ToFinalization()).ToArray();
    }

    public async Task<IReadOnlyList<OperationalRegisterFinalization>> GetBlockedAsync(
        Guid registerId,
        int limit = 100,
        CancellationToken ct = default)
    {
        if (registerId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(registerId), "RegisterId must not be empty.");

        if (limit <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(limit), limit, "Limit must be positive.");

        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT
                               register_id      AS "RegisterId",
                               period           AS "Period",
                               status           AS "Status",
                               finalized_at_utc AS "FinalizedAtUtc",
                               dirty_since_utc  AS "DirtySinceUtc",
                               blocked_since_utc AS "BlockedSinceUtc",
                               blocked_reason    AS "BlockedReason",
                               created_at_utc   AS "CreatedAtUtc",
                               updated_at_utc   AS "UpdatedAtUtc"
                           FROM operational_register_finalizations
                           WHERE register_id = @RegisterId
                             AND status = @BlockedStatus
                           ORDER BY blocked_since_utc, period
                           LIMIT @Limit;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new
            {
                RegisterId = registerId,
                BlockedStatus = (short)OperationalRegisterFinalizationStatus.BlockedNoProjector,
                Limit = limit
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var rows = await uow.Connection.QueryAsync<Row>(cmd);
        return rows.Select(r => r.ToFinalization()).ToArray();
    }

    public async Task<IReadOnlyList<OperationalRegisterFinalization>> GetDirtyAcrossAllAsync(
        int limit = 100,
        CancellationToken ct = default)
    {
        if (limit <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(limit), limit, "Limit must be positive.");

        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT
                               register_id      AS "RegisterId",
                               period           AS "Period",
                               status           AS "Status",
                               finalized_at_utc AS "FinalizedAtUtc",
                               dirty_since_utc  AS "DirtySinceUtc",
                               blocked_since_utc AS "BlockedSinceUtc",
                               blocked_reason    AS "BlockedReason",
                               created_at_utc   AS "CreatedAtUtc",
                               updated_at_utc   AS "UpdatedAtUtc"
                           FROM operational_register_finalizations
                           WHERE status = @DirtyStatus
                           ORDER BY dirty_since_utc, register_id, period
                           LIMIT @Limit;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new
            {
                DirtyStatus = (short)OperationalRegisterFinalizationStatus.Dirty,
                Limit = limit
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var rows = await uow.Connection.QueryAsync<Row>(cmd);
        return rows.Select(r => r.ToFinalization()).ToArray();
    }

    public async Task<IReadOnlyList<OperationalRegisterFinalization>> GetBlockedAcrossAllAsync(
        int limit = 100,
        CancellationToken ct = default)
    {
        if (limit <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(limit), limit, "Limit must be positive.");

        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT
                               register_id      AS "RegisterId",
                               period           AS "Period",
                               status           AS "Status",
                               finalized_at_utc AS "FinalizedAtUtc",
                               dirty_since_utc  AS "DirtySinceUtc",
                               blocked_since_utc AS "BlockedSinceUtc",
                               blocked_reason    AS "BlockedReason",
                               created_at_utc   AS "CreatedAtUtc",
                               updated_at_utc   AS "UpdatedAtUtc"
                           FROM operational_register_finalizations
                           WHERE status = @BlockedStatus
                           ORDER BY blocked_since_utc, register_id, period
                           LIMIT @Limit;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new
            {
                BlockedStatus = (short)OperationalRegisterFinalizationStatus.BlockedNoProjector,
                Limit = limit
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var rows = await uow.Connection.QueryAsync<Row>(cmd);
        return rows.Select(r => r.ToFinalization()).ToArray();
    }

    public async Task<IReadOnlyList<DateOnly>> GetTrackedPeriodsOnOrAfterAsync(
        Guid registerId,
        DateOnly fromInclusive,
        CancellationToken ct = default)
    {
        if (registerId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(registerId), "RegisterId must not be empty.");

        if (fromInclusive.Day != 1)
            throw new NgbArgumentInvalidException(nameof(fromInclusive), "Period must be a month start (day=1).");

        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT period
                           FROM operational_register_finalizations
                           WHERE register_id = @RegisterId
                             AND period >= @FromInclusive
                           ORDER BY period;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new { RegisterId = registerId, FromInclusive = fromInclusive },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var periods = await uow.Connection.QueryAsync<DateOnly>(cmd);
        return periods.AsList();
    }

    public async Task<DateOnly?> GetLatestFinalizedPeriodBeforeAsync(
        Guid registerId,
        DateOnly beforeExclusive,
        CancellationToken ct = default)
    {
        if (registerId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(registerId), "RegisterId must not be empty.");

        if (beforeExclusive.Day != 1)
            throw new NgbArgumentInvalidException(nameof(beforeExclusive), "Period must be a month start (day=1).");

        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT period
                           FROM operational_register_finalizations
                           WHERE register_id = @RegisterId
                             AND status = @FinalizedStatus
                             AND period < @BeforeExclusive
                           ORDER BY period DESC
                           LIMIT 1;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new
            {
                RegisterId = registerId,
                FinalizedStatus = (short)OperationalRegisterFinalizationStatus.Finalized,
                BeforeExclusive = beforeExclusive
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        return await uow.Connection.QuerySingleOrDefaultAsync<DateOnly?>(cmd);
    }

    private static void EnsureValidKey(Guid registerId, DateOnly period)
    {
        if (registerId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(registerId), "RegisterId must not be empty.");

        // The DB has a hard invariant; validate early to avoid hard-to-read Postgres exception.
        if (period.Day != 1)
            throw new NgbArgumentInvalidException(nameof(period), "Period must be a month start (day=1).");
    }

    private sealed class Row
    {
        public Guid RegisterId { get; init; }
        public DateOnly Period { get; init; }
        public short Status { get; init; }
        public DateTime? FinalizedAtUtc { get; init; }
        public DateTime? DirtySinceUtc { get; init; }
        public DateTime? BlockedSinceUtc { get; init; }
        public string? BlockedReason { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public DateTime UpdatedAtUtc { get; init; }

        public OperationalRegisterFinalization ToFinalization() => new(
            RegisterId,
            Period,
            (OperationalRegisterFinalizationStatus)Status,
            FinalizedAtUtc,
            DirtySinceUtc,
            BlockedSinceUtc,
            BlockedReason,
            CreatedAtUtc,
            UpdatedAtUtc);
    }
}

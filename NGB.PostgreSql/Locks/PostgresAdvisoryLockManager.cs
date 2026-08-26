using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NGB.Core.Locks;
using NGB.Persistence.Locks;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.DependencyInjection;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.Locks;

public sealed class PostgresAdvisoryLockManager(
    IUnitOfWork uow,
    IOptions<PostgresOptions> options,
    ILogger<PostgresAdvisoryLockManager> logger,
    TimeProvider timeProvider)
    : IAdvisoryLockBatchManager
{
    private const uint FnvOffset = 2166136261u;
    private const uint FnvPrime = 16777619u;
    private object? _trackedTransaction;
    private readonly HashSet<int> _heldDocumentKey2Values = [];

    private static uint Avalanche(uint h)
    {
        // Murmur3 finalizer.
        h ^= h >> 16;
        h *= 0x85EBCA6Bu;
        h ^= h >> 13;
        h *= 0xC2B2AE35u;
        h ^= h >> 16;
        return h;
    }

    private static (int Key2A, int Key2B) GetGuidLockKeys(Guid id)
    {
        // We intentionally mix all 16 bytes; this is fast, stable, and allocation-free.
        Span<byte> bytes = stackalloc byte[16];
        _ = id.TryWriteBytes(bytes);

        uint h1 = FnvOffset;
        uint h2 = FnvOffset ^ 0x9E3779B9u; // different seed

        foreach (var b in bytes)
        {
            h1 ^= b;
            h1 *= FnvPrime;

            h2 ^= b;
            h2 *= FnvPrime;
        }

        h1 = Avalanche(h1);
        h2 = Avalanche(h2 ^ 0x85EBCA6Bu); // small post-mix tweak

        return NormalizeGuidLockKeys(unchecked((int)h1), unchecked((int)h2));
    }

    internal static (int Key2A, int Key2B) NormalizeGuidLockKeys(int key2A, int key2B)
    {
        key2A = key2A == 0 ? 1 : key2A;
        key2B = key2B == 0 ? 2 : key2B;

        if (key2B == key2A)
        {
            key2B = unchecked(key2A + 1);
            if (key2B == 0)
                key2B = 1;
        }

        return (key2A, key2B);
    }

    internal static (int First, int Second) OrderGuidLockKeys(int key2A, int key2B)
        => key2A <= key2B ? (key2A, key2B) : (key2B, key2A);

    private async Task LockTwoIntAsync(int key1, int key2, CancellationToken ct)
    {
        // NOTE:
        // We intentionally do a bounded wait via pg_try_advisory_xact_lock to avoid
        // "infinite" hangs under contention (e.g., a stuck transaction holding locks).
        //
        // If you want "wait forever" semantics, set AdvisoryLockWaitTimeoutSeconds to a large value.

        var timeoutSeconds = options.Value.AdvisoryLockWaitTimeoutSeconds;
        if (timeoutSeconds <= 0)
            throw new NgbConfigurationViolationException($"{nameof(PostgresOptions.AdvisoryLockWaitTimeoutSeconds)} must be > 0.", new Dictionary<string, object?> { ["value"] = timeoutSeconds });

        var timeout = TimeSpan.FromSeconds(timeoutSeconds);
        var deadline = timeProvider.GetUtcNowDateTime() + timeout;

        var attempt = 0;
        var backoff = TimeSpan.FromMilliseconds(20);
        var backoffMax = TimeSpan.FromMilliseconds(250);

        const string sql = "SELECT pg_try_advisory_xact_lock(@Key1, @Key2);";

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var cmd = new CommandDefinition(
                sql,
                new { Key1 = key1, Key2 = key2 },
                transaction: uow.Transaction,
                cancellationToken: ct);

            await uow.EnsureConnectionOpenAsync(ct);
            var acquired = await uow.Connection.ExecuteScalarAsync<bool>(cmd);
            if (acquired)
                return;

            if (timeProvider.GetUtcNowDateTime() >= deadline)
            {
                var inner = new TimeoutException(
                    $"Timed out waiting for advisory lock ({key1}, {key2}) after {timeout.TotalSeconds:0} seconds.");

                throw new NgbTimeoutException(
                    operation: "postgres.advisory_lock",
                    innerException: inner,
                    additionalContext: new Dictionary<string, object?>
                    {
                        ["key1"] = key1,
                        ["key2"] = key2,
                        ["timeoutSeconds"] = timeoutSeconds,
                        ["attempt"] = attempt
                    });
            }

            attempt++;
            if (ShouldLogWaitAttempt(attempt))
            {
                logger.LogDebug(
                    "Waiting for advisory lock ({Key1},{Key2}); attempt={Attempt}, remaining={RemainingMs}ms.",
                    key1,
                    key2,
                    attempt,
                    Math.Max(0, (deadline - timeProvider.GetUtcNowDateTime()).TotalMilliseconds));
            }

            // Small bounded backoff to reduce hot spinning while still being responsive.
            await Task.Delay(backoff, ct);
            backoff = NextBackoff(backoff, backoffMax);
        }
    }

    internal static bool ShouldLogWaitAttempt(int attempt) => attempt == 1 || attempt % 50 == 0;

    internal static TimeSpan NextBackoff(TimeSpan current, TimeSpan maximum)
        => current < maximum
            ? TimeSpan.FromMilliseconds(Math.Min(maximum.TotalMilliseconds, current.TotalMilliseconds * 2))
            : current;

    public Task LockPeriodAsync(DateOnly period, CancellationToken ct = default)
        => LockPeriodAsync(period, AdvisoryLockPeriodScope.Accounting, ct);

    public async Task LockPeriodAsync(DateOnly period, AdvisoryLockPeriodScope scope, CancellationToken ct = default)
    {
        if (!uow.HasActiveTransaction || uow.Transaction is null)
            throw new NgbInvariantViolationException("Advisory locks require an active transaction. Call BeginTransactionAsync() first.");

        // Period locks are monthly. Normalize any date to YYYY-MM-01.
        var monthStart = new DateOnly(period.Year, period.Month, 1);

        // Payload key: YYYYMM, unique across years/months.
        // Example: 2026-01-15 -> 202601
        var key1 = scope switch
        {
            AdvisoryLockPeriodScope.Accounting => AdvisoryLockNamespaces.Period,
            AdvisoryLockPeriodScope.OperationalRegister => AdvisoryLockNamespaces.OperationalRegisterPeriod,
            _ => throw new NgbArgumentOutOfRangeException(nameof(scope), scope, "Unknown period advisory lock scope.")
        };

        var key2 = checked(monthStart.Year * 100 + monthStart.Month);

        var ns = AdvisoryLockNamespaces.Format(key1);
        logger.LogDebug(
            "Acquiring period advisory lock {Namespace}/{Key2} for {PeriodMonth} (scope={Scope}).",
            ns,
            key2,
            monthStart,
            scope);

        await LockTwoIntAsync(key1, key2, ct);

        logger.LogDebug(
            "Period advisory lock acquired {Namespace}/{Key2} for {PeriodMonth} (scope={Scope}).",
            ns,
            key2,
            monthStart,
            scope);
    }

    public async Task LockDocumentAsync(Guid documentId, CancellationToken ct = default)
    {
        if (!uow.HasActiveTransaction || uow.Transaction is null)
            throw new NgbInvariantViolationException("Advisory locks require an active transaction. Call BeginTransactionAsync() first.");

        var key1 = AdvisoryLockNamespaces.Document;
        var (a, b) = GetGuidLockKeys(documentId);
        var (first, second) = OrderGuidLockKeys(a, b);
        ResetDocumentLockCacheWhenTransactionChanges();

        if (_heldDocumentKey2Values.Contains(first) && _heldDocumentKey2Values.Contains(second))
            return;

        var ns = AdvisoryLockNamespaces.Format(key1);
        logger.LogDebug("Acquiring document advisory locks {Namespace}: {Key2A} and {Key2B}.", ns, first,  second);
        if (!_heldDocumentKey2Values.Contains(first))
        {
            await LockTwoIntAsync(key1, first, ct);
            _heldDocumentKey2Values.Add(first);
        }

        if (!_heldDocumentKey2Values.Contains(second))
        {
            await LockTwoIntAsync(key1, second, ct);
            _heldDocumentKey2Values.Add(second);
        }

        logger.LogDebug("Document advisory locks acquired {Namespace}: {Key2A} and {Key2B}.", ns, first, second);
    }

    public async Task LockDocumentsAsync(IReadOnlyCollection<Guid> documentIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(documentIds);

        if (!uow.HasActiveTransaction || uow.Transaction is null)
            throw new NgbInvariantViolationException("Advisory locks require an active transaction. Call BeginTransactionAsync() first.");

        ResetDocumentLockCacheWhenTransactionChanges();
        var keys = documentIds
            .Where(static id => id != Guid.Empty)
            .Distinct()
            .OrderBy(static id => id)
            .SelectMany(static id =>
            {
                var (a, b) = GetGuidLockKeys(id);
                var (first, second) = OrderGuidLockKeys(a, b);
                return new[] { first, second };
            })
            .Distinct()
            .Where(key => !_heldDocumentKey2Values.Contains(key))
            .ToArray();

        if (keys.Length == 0)
            return;

        await LockKeySetAsync(AdvisoryLockNamespaces.Document, keys, ct);
        _heldDocumentKey2Values.UnionWith(keys);
    }

    public Task LockPeriodsAsync(
        IReadOnlyCollection<DateOnly> periods,
        AdvisoryLockPeriodScope scope,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(periods);

        if (!uow.HasActiveTransaction || uow.Transaction is null)
            throw new NgbInvariantViolationException("Advisory locks require an active transaction. Call BeginTransactionAsync() first.");

        var key1 = scope switch
        {
            AdvisoryLockPeriodScope.Accounting => AdvisoryLockNamespaces.Period,
            AdvisoryLockPeriodScope.OperationalRegister => AdvisoryLockNamespaces.OperationalRegisterPeriod,
            _ => throw new NgbArgumentOutOfRangeException(nameof(scope), scope, "Unknown period advisory lock scope.")
        };
        var keys = NormalizePeriodLockKeys(periods);

        return LockKeySetAsync(key1, keys, ct);
    }

    internal static int[] NormalizePeriodLockKeys(IEnumerable<DateOnly> periods)
        => periods
            .Select(static period => new DateOnly(period.Year, period.Month, 1))
            .Distinct()
            .OrderBy(static period => period)
            .Select(static period => checked(period.Year * 100 + period.Month))
            .ToArray();

    private async Task LockKeySetAsync(int key1, int[] keys, CancellationToken ct)
    {
        if (keys.Length == 0)
            return;

        var timeoutSeconds = options.Value.AdvisoryLockWaitTimeoutSeconds;
        if (timeoutSeconds <= 0)
        {
            throw new NgbConfigurationViolationException(
                $"{nameof(PostgresOptions.AdvisoryLockWaitTimeoutSeconds)} must be > 0.",
                new Dictionary<string, object?> { ["value"] = timeoutSeconds });
        }

        var timeout = TimeSpan.FromSeconds(timeoutSeconds);
        var deadline = timeProvider.GetUtcNowDateTime() + timeout;
        var attempt = 0;
        var backoff = TimeSpan.FromMilliseconds(20);
        var backoffMax = TimeSpan.FromMilliseconds(250);

        const string sql = """
WITH RECURSIVE requested AS MATERIALIZED (
    SELECT key2, ordinal
    FROM UNNEST(@Key2Values::integer[]) WITH ORDINALITY AS requested(key2, ordinal)
    ORDER BY ordinal
), attempts(ordinal, acquired) AS (
    SELECT requested.ordinal,
           pg_try_advisory_xact_lock(@Key1, requested.key2)
    FROM requested
    WHERE requested.ordinal = 1
    UNION ALL
    SELECT requested.ordinal,
           CASE WHEN attempts.acquired
               THEN pg_try_advisory_xact_lock(@Key1, requested.key2)
               ELSE FALSE
           END
    FROM attempts
    JOIN requested ON requested.ordinal = attempts.ordinal + 1
)
SELECT COALESCE(BOOL_AND(acquired), TRUE)
FROM attempts;
""";

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            await uow.EnsureConnectionOpenAsync(ct);
            var acquired = await uow.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                sql,
                new { Key1 = key1, Key2Values = keys },
                transaction: uow.Transaction,
                cancellationToken: ct));
            if (acquired)
                return;

            if (timeProvider.GetUtcNowDateTime() >= deadline)
            {
                throw new NgbTimeoutException(
                    operation: "postgres.advisory_lock_batch",
                    innerException: new TimeoutException(
                        $"Timed out waiting for {keys.Length} advisory lock keys after {timeout.TotalSeconds:0} seconds."),
                    additionalContext: new Dictionary<string, object?>
                    {
                        ["key1"] = key1,
                        ["keyCount"] = keys.Length,
                        ["timeoutSeconds"] = timeoutSeconds,
                        ["attempt"] = attempt
                    });
            }

            attempt++;
            await Task.Delay(backoff, ct);
            backoff = NextBackoff(backoff, backoffMax);
        }
    }

    private void ResetDocumentLockCacheWhenTransactionChanges()
    {
        var transaction = uow.Transaction;
        if (ReferenceEquals(_trackedTransaction, transaction))
            return;

        _trackedTransaction = transaction;
        _heldDocumentKey2Values.Clear();
    }

    public async Task LockCatalogAsync(Guid catalogId, CancellationToken ct = default)
    {
        if (!uow.HasActiveTransaction || uow.Transaction is null)
            throw new NgbInvariantViolationException("Advisory locks require an active transaction. Call BeginTransactionAsync() first.");

        var key1 = AdvisoryLockNamespaces.Catalog;
        var (a, b) = GetGuidLockKeys(catalogId);
        var (first, second) = OrderGuidLockKeys(a, b);

        var ns = AdvisoryLockNamespaces.Format(key1);
        logger.LogDebug("Acquiring catalog advisory locks {Namespace}: {Key2A} and {Key2B}.", ns, first, second);
        await LockTwoIntAsync(key1, first, ct);
        await LockTwoIntAsync(key1, second, ct);

        logger.LogDebug("Catalog advisory locks acquired {Namespace}: {Key2A} and {Key2B}.", ns, first, second);
    }

    public async Task LockOperationalRegisterAsync(Guid registerId, CancellationToken ct = default)
    {
        if (!uow.HasActiveTransaction || uow.Transaction is null)
            throw new NgbInvariantViolationException("Advisory locks require an active transaction. Call BeginTransactionAsync() first.");

        var key1 = AdvisoryLockNamespaces.OperationalRegister;
        var (a, b) = GetGuidLockKeys(registerId);
        var (first, second) = OrderGuidLockKeys(a, b);

        var ns = AdvisoryLockNamespaces.Format(key1);
        logger.LogDebug("Acquiring operational register advisory locks {Namespace}: {Key2A} and {Key2B}.", ns, first, second);
        await LockTwoIntAsync(key1, first, ct);
        await LockTwoIntAsync(key1, second, ct);

        logger.LogDebug("Operational register advisory locks acquired {Namespace}: {Key2A} and {Key2B}.", ns, first, second);
    }
}

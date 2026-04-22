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
    : IAdvisoryLockManager
{
    private const uint FnvOffset = 2166136261u;
    private const uint FnvPrime = 16777619u;

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
        if (!id.TryWriteBytes(bytes))
        {
            throw new NgbInvariantViolationException(
                "Failed to write Guid bytes.",
                new Dictionary<string, object?> { ["id"] = id });
        }

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

        var k1 = unchecked((int)h1);
        var k2 = unchecked((int)h2);

        // Avoid obvious hotspot keys.
        if (k1 == 0) k1 = 1;
        if (k2 == 0) k2 = 2;

        // Guarantee distinct payload keys so callers can always take two locks per Guid.
        // (This makes tests deterministic and removes reliance on astronomically rare hash collisions.)
        if (k2 == k1)
        {
            // Make a deterministic, stable alternate key.
            k2 = unchecked((int)Avalanche(unchecked((uint)k2) ^ 0x9E3779B9u));
            if (k2 == 0) k2 = 2;
            if (k2 == k1) k2 ^= unchecked((int)0xA5A5A5A5);
            if (k2 == 0) k2 = 2;
            if (k2 == k1) k2 = unchecked(k1 + 1);
        }

        return (k1, k2);
    }

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
            if (attempt == 1 || attempt % 50 == 0)
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
            if (backoff < backoffMax)
                backoff = TimeSpan.FromMilliseconds(Math.Min(backoffMax.TotalMilliseconds, backoff.TotalMilliseconds * 2));
        }
    }

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
        var first = a <= b ? a : b;
        var second = a <= b ? b : a;

        var ns = AdvisoryLockNamespaces.Format(key1);
        logger.LogDebug("Acquiring document advisory locks {Namespace}: {Key2A} and {Key2B}.", ns, first,  second);
        await LockTwoIntAsync(key1, first, ct);
        await LockTwoIntAsync(key1, second, ct);

        logger.LogDebug("Document advisory locks acquired {Namespace}: {Key2A} and {Key2B}.", ns, first, second);
    }

    public async Task LockCatalogAsync(Guid catalogId, CancellationToken ct = default)
    {
        if (!uow.HasActiveTransaction || uow.Transaction is null)
            throw new NgbInvariantViolationException("Advisory locks require an active transaction. Call BeginTransactionAsync() first.");

        var key1 = AdvisoryLockNamespaces.Catalog;
        var (a, b) = GetGuidLockKeys(catalogId);
        var first = a <= b ? a : b;
        var second = a <= b ? b : a;

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
        var first = a <= b ? a : b;
        var second = a <= b ? b : a;

        var ns = AdvisoryLockNamespaces.Format(key1);
        logger.LogDebug("Acquiring operational register advisory locks {Namespace}: {Key2A} and {Key2B}.", ns, first, second);
        await LockTwoIntAsync(key1, first, ct);
        await LockTwoIntAsync(key1, second, ct);

        logger.LogDebug("Operational register advisory locks acquired {Namespace}: {Key2A} and {Key2B}.", ns, first, second);
    }
}

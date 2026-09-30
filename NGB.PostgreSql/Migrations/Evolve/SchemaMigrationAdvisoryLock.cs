using System.Data.Common;
using Dapper;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.Migrations.Evolve;

internal static class SchemaMigrationAdvisoryLock
{
    // "NGBSCHEM" (8 bytes) => one global schema lock per database.
    public const long Key = 0x4E4742534348454DL;

    public static async Task<bool> TryAcquireAsync(DbConnection connection, CancellationToken ct)
    {
        var cmd = new CommandDefinition(
            "SELECT pg_try_advisory_lock(@key);",
            parameters: new { key = Key },
            cancellationToken: ct);

        return await connection.ExecuteScalarAsync<bool>(cmd);
    }

    public static async Task AcquireOrThrowAsync(
        DbConnection connection,
        SchemaMigrationLockMode mode,
        TimeSpan? waitTimeout,
        Action<string>? log,
        CancellationToken ct)
    {
        if (mode == SchemaMigrationLockMode.Skip)
        {
            // Skip is handled by AcquireOrSkipAsync.
            throw new NgbArgumentInvalidException(nameof(mode), "Use AcquireOrSkipAsync for Skip mode.");
        }

        await AcquireOrSkipAsync(connection, mode, waitTimeout, log, ct);
    }

    /// <summary>
    /// Attempts to acquire the lock according to <paramref name="mode"/>.
    /// Returns false only when <paramref name="mode"/> is <see cref="SchemaMigrationLockMode.Skip"/>.
    /// </summary>
    public static async Task<bool> AcquireOrSkipAsync(
        DbConnection connection,
        SchemaMigrationLockMode mode,
        TimeSpan? waitTimeout,
        Action<string>? log,
        CancellationToken ct)
        => await AcquireOrSkipCoreAsync(
            connection,
            mode,
            waitTimeout,
            log,
            TimeProvider.System,
            Task.Delay,
            ct);

    internal static async Task<bool> AcquireOrSkipCoreAsync(
        DbConnection connection,
        SchemaMigrationLockMode mode,
        TimeSpan? waitTimeout,
        Action<string>? log,
        TimeProvider timeProvider,
        Func<TimeSpan, CancellationToken, Task> delay,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (!Enum.IsDefined(mode))
            throw new NgbArgumentOutOfRangeException(nameof(mode), mode, "Unknown schema migration lock mode.");

        if (mode == SchemaMigrationLockMode.Try || mode == SchemaMigrationLockMode.Skip)
        {
            var acquired = await TryAcquireAsync(connection, ct);
            if (acquired)
            {
                log?.Invoke("Schema lock acquired.");
                return true;
            }

            if (mode == SchemaMigrationLockMode.Skip)
            {
                log?.Invoke("Schema lock is held by another session. Skipping migration work.");
                return false;
            }

            throw new SchemaMigrationLockNotAcquiredException(mode, waitTimeout);
        }

        // Wait mode: retry loop with optional timeout.
        var start = timeProvider.GetUtcNowDateTime();

        while (true)
        {
            var acquired = await TryAcquireAsync(connection, ct);
            if (acquired)
            {
                log?.Invoke("Schema lock acquired.");
                return true;
            }

            if (waitTimeout is not null)
            {
                var elapsed = timeProvider.GetUtcNowDateTime() - start;
                if (elapsed >= waitTimeout.Value)
                    throw new SchemaMigrationLockNotAcquiredException(mode, waitTimeout);
            }

            await delay(TimeSpan.FromMilliseconds(500), ct);
        }
    }

    public static async Task ReleaseAsync(DbConnection connection, CancellationToken ct)
    {
        var cmd = new CommandDefinition(
            "SELECT pg_advisory_unlock(@key);",
            parameters: new { key = Key },
            cancellationToken: ct);

        await connection.ExecuteAsync(cmd);
    }
}

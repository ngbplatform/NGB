using Dapper;
using Npgsql;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.Migrations.Evolve;

internal static class SchemaMigrationAdvisoryLock
{
    // "NGBSCHEM" (8 bytes) => one global schema lock per database.
    public const long Key = 0x4E4742534348454DL;

    public static async Task<bool> TryAcquireAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        var cmd = new CommandDefinition(
            "SELECT pg_try_advisory_lock(@key);",
            parameters: new { key = Key },
            cancellationToken: ct);

        return await connection.ExecuteScalarAsync<bool>(cmd);
    }

    public static async Task AcquireOrThrowAsync(
        NpgsqlConnection connection,
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

        var acquired = await AcquireOrSkipAsync(connection, mode, waitTimeout, log, ct);
        if (!acquired)
            throw new SchemaMigrationLockNotAcquiredException(mode, waitTimeout);
    }

    /// <summary>
    /// Attempts to acquire the lock according to <paramref name="mode"/>.
    /// Returns false only when <paramref name="mode"/> is <see cref="SchemaMigrationLockMode.Skip"/>.
    /// </summary>
    public static async Task<bool> AcquireOrSkipAsync(
        NpgsqlConnection connection,
        SchemaMigrationLockMode mode,
        TimeSpan? waitTimeout,
        Action<string>? log,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

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
        var start = TimeProvider.System.GetUtcNowDateTime();

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
                var elapsed = TimeProvider.System.GetUtcNowDateTime() - start;
                if (elapsed >= waitTimeout.Value)
                    throw new SchemaMigrationLockNotAcquiredException(mode, waitTimeout);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
        }
    }

    public static async Task ReleaseAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        var cmd = new CommandDefinition(
            "SELECT pg_advisory_unlock(@key);",
            parameters: new { key = Key },
            cancellationToken: ct);

        await connection.ExecuteAsync(cmd);
    }
}

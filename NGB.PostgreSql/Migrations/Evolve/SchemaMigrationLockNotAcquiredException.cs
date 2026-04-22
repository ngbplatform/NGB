using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Migrations.Evolve;

/// <summary>
/// Thrown when the migrator cannot acquire the global schema migration lock.
/// </summary>
public sealed class SchemaMigrationLockNotAcquiredException(
    SchemaMigrationLockMode mode,
    TimeSpan? waitTimeout)
    : NgbInfrastructureException(
        message: "Schema migration lock not acquired.",
        errorCode: Code,
        context: BuildContext(mode, waitTimeout))
{
    public const string Code = "ngb.schema.migrations.lock_not_acquired";

    public SchemaMigrationLockMode Mode { get; } = mode;
    public TimeSpan? WaitTimeout { get; } = waitTimeout;

    private static IReadOnlyDictionary<string, object?> BuildContext(SchemaMigrationLockMode mode, TimeSpan? waitTimeout)
        => new Dictionary<string, object?>
        {
            ["lockKey"] = SchemaMigrationAdvisoryLock.Key,
            ["mode"] = mode.ToString(),
            ["waitTimeoutSeconds"] = waitTimeout?.TotalSeconds,
        };
}

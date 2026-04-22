namespace NGB.Migrator.Core;

/// <summary>
/// Stable process exit codes for CI/CD and Kubernetes Jobs.
/// </summary>
internal static class MigratorExitCodes
{
    public const int Success = 0;
    public const int Failure = 1;
    public const int InvalidArguments = 2;

    /// <summary>
    /// The global schema migration lock was not acquired (Try/Wait timeout).
    /// </summary>
    public const int LockNotAcquired = 3;
}

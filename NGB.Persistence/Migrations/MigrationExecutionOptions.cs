namespace NGB.Persistence.Migrations;

/// <summary>
/// Session-level execution options for schema operations (migrations / drift repair).
/// </summary>
public sealed record MigrationExecutionOptions(
    TimeSpan? LockTimeout = null,
    TimeSpan? StatementTimeout = null,
    bool SkipAdvisoryLock = false);

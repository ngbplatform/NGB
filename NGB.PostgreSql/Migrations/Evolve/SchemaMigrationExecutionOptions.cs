namespace NGB.PostgreSql.Migrations.Evolve;

/// <summary>
/// Execution options for a schema migration run.
/// </summary>
public sealed record SchemaMigrationExecutionOptions(
    string? ApplicationName = null,
    SchemaMigrationLockMode LockMode = SchemaMigrationLockMode.Wait,
    TimeSpan? LockWaitTimeout = null);

namespace NGB.PostgreSql.Bootstrap;

/// <summary>
/// Controls whether a host application is allowed to mutate the database schema on startup.
///
/// Best practice:
/// - Production deployments should use a dedicated migrator (CI step / Kubernetes Job).
/// - Application hosts should default to <see cref="None"/>.
/// - Development/demo hosts may opt in explicitly.
/// </summary>
public enum SchemaInitMode
{
    /// <summary>
    /// Do not apply schema migrations.
    /// </summary>
    None = 0,

    /// <summary>
    /// Apply versioned + repeatable schema migrations (Evolve).
    /// </summary>
    Migrate = 1,

    /// <summary>
    /// Apply drift-repair only (explicit ops/test tool).
    /// </summary>
    Repair = 2,

    /// <summary>
    /// Apply migrations, then run drift-repair.
    ///
    /// Useful for integration tests and local development when you intentionally simulate drift.
    /// </summary>
    MigrateAndRepair = 3,
}

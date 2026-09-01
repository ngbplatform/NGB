namespace NGB.Persistence.Databases;

/// <summary>
/// Provider-neutral boundary for idempotently provisioning a database identified
/// by a provider connection string.
/// </summary>
public interface IDatabaseProvisioner
{
    Task EnsureDatabaseExistsAsync(string connectionString, CancellationToken ct = default);
}

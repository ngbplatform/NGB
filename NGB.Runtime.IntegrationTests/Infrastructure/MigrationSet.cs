using NGB.PostgreSql.Bootstrap;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

internal static class MigrationSet
{
    /// <summary>
    /// Applies the platform migration set used by integration tests.
    /// IMPORTANT: use the same bootstrapper as production/demo so schema stays consistent.
    /// NOTE: integration tests also rely on drift-repair (they intentionally drop guards/indexes).
    /// </summary>
    public static Task ApplyPlatformMigrationsAsync(string connectionString, CancellationToken ct = default)
        => ApplyPlatformMigrationsAndRepairAsync(connectionString, ct);

    private static async Task ApplyPlatformMigrationsAndRepairAsync(string connectionString, CancellationToken ct)
    {
        await DatabaseBootstrapper.InitializeAsync(connectionString, ct);
        await DatabaseBootstrapper.RepairAsync(connectionString, ct);
    }
}

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace NGB.PostgreSql.AspNetCore.Health;

internal sealed class PostgresHealthCheck : IHealthCheck
{
    private readonly Func<CancellationToken, Task> _probe;

    public PostgresHealthCheck(string connectionString)
        : this(ct => ProbeAsync(connectionString, ct))
    {
    }

    internal PostgresHealthCheck(Func<CancellationToken, Task> probe)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _probe(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL did not complete its health probe.", exception);
        }
    }

    private static async Task ProbeAsync(string connectionString, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1;";
        await command.ExecuteScalarAsync(ct);
    }
}

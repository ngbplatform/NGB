using Dapper;
using Npgsql;
using NGB.Persistence.Migrations;

namespace NGB.PropertyManagement.PostgreSql.Bootstrap;

/// <summary>
/// Property Management module drift-repair hooks.
///
/// PM drift-repair hooks.
/// Versioned migrations install the required typed-document immutability guards;
/// this repair step remains as an idempotent safety net for drift recovery only.
/// </summary>
public static class PropertyManagementDatabaseBootstrapper
{
    public static Task RepairModuleAsync(string connectionString, CancellationToken ct = default)
        => RepairModuleAsync(connectionString, options: null, ct);

    public static async Task RepairModuleAsync(
        string connectionString,
        MigrationExecutionOptions? options,
        CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        // Keep consistent semantics with other migration/repair runners.
        await conn.ExecuteAsync(new CommandDefinition("SET TIME ZONE 'UTC';", cancellationToken: ct));

        if (options?.LockTimeout is not null)
        {
            var ms = (long)Math.Max(0, options.LockTimeout.Value.TotalMilliseconds);
            await conn.ExecuteAsync(new CommandDefinition($"SET lock_timeout = '{ms}ms';", cancellationToken: ct));
        }

        if (options?.StatementTimeout is not null)
        {
            var ms = (long)Math.Max(0, options.StatementTimeout.Value.TotalMilliseconds);
            await conn.ExecuteAsync(new CommandDefinition($"SET statement_timeout = '{ms}ms';", cancellationToken: ct));
        }

        // Module migrations may create new doc_* tables; ensure all typed document tables are protected
        // by the reusable "posted document immutability" trigger (idempotent).
        await conn.ExecuteAsync(new CommandDefinition(
            "SELECT ngb_install_typed_document_immutability_guards();",
            cancellationToken: ct));
    }
}

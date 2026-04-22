using Dapper;
using Npgsql;
using NGB.Persistence.Migrations;

namespace NGB.Trade.PostgreSql.Bootstrap;

/// <summary>
/// Trade module drift-repair hooks.
/// </summary>
public static class TradeDatabaseBootstrapper
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

        await conn.ExecuteAsync(new CommandDefinition(
            """
            CREATE INDEX IF NOT EXISTS ix_doc_trd_item_price_update__lines__currency
                ON doc_trd_item_price_update__lines(currency);

            SELECT ngb_install_typed_document_immutability_guards();
            """,
            cancellationToken: ct));
    }
}

using Dapper;
using NGB.Persistence.Migrations;
using Npgsql;

namespace NGB.CRM.PostgreSql.Bootstrap;

public static class CrmDatabaseBootstrapper
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
            DROP INDEX IF EXISTS ix_doc_crm_lead_intake__email;

            CREATE INDEX IF NOT EXISTS ix_doc_crm_lead_intake__email
                ON doc_crm_lead_intake(email);

            CREATE INDEX IF NOT EXISTS ix_doc_crm_quote__lines__product_id
                ON doc_crm_quote__lines(product_id);

            SELECT ngb_install_typed_document_immutability_guards();
            SELECT ngb_install_search_trigram_indexes(ARRAY['cat_crm_', 'doc_crm_']);
            """,
            cancellationToken: ct));
    }
}

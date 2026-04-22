using System.Text;
using Dapper;
using Npgsql;
using NGB.Persistence.Migrations;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Migrations;

public sealed class PostgresMigrationRunner(string connectionString) : IMigrationRunner
{
    // NOTE:
    // PostgreSQL DDL like "CREATE TABLE IF NOT EXISTS" is not fully concurrency-safe.
    // Two concurrent sessions can still race while creating underlying catalog objects
    // (e.g., pg_type_typname_nsp_index).
    //
    // We serialize schema writes within a database using an advisory lock.
    // This key MUST match the one used by the Evolve migrator to prevent concurrent
    // schema writes (e.g., multiple migrator Jobs/pods running at the same time).
    private const long SchemaMigrationAdvisoryLockKey = Evolve.SchemaMigrationAdvisoryLock.Key;

    public async Task RunAsync(
        IDdlObject[] ddlObjects,
        MigrationExecutionOptions? options = null,
        CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);

        // Defense-in-depth: ensure predictable UTC semantics for all timestamp operations.
        // This is particularly important for DDL that uses NOW()/CURRENT_TIMESTAMP.
        await connection.ExecuteAsync(new CommandDefinition("SET TIME ZONE 'UTC';", cancellationToken: ct));

        await ApplySessionOptionsAsync(connection, options, ct);

        var acquiredLock = false;

        if (options?.SkipAdvisoryLock != true)
        {
            // Session-level lock (auto-released on connection close).
            await connection.ExecuteAsync(new CommandDefinition(
                "SELECT pg_advisory_lock(@key);",
                parameters: new { key = SchemaMigrationAdvisoryLockKey },
                cancellationToken: ct));

            acquiredLock = true;
        }

        try
        {
            foreach (var ddl in ddlObjects)
            {
                var sql = ddl.Generate();
                try
                {
                    var cmd = new CommandDefinition(sql, cancellationToken: ct);
                    await connection.ExecuteAsync(cmd);
                }
                catch (PostgresException ex)
                {
                    var snippet = BuildSqlSnippet(sql, ex.Position, contextChars: 180);

                    var sb = new StringBuilder();
                    sb.AppendLine("PostgreSQL migration failed.");
                    sb.AppendLine();
                    sb.AppendLine($"DDL object: {ddl.Name}");
                    sb.AppendLine($"SQLSTATE: {ex.SqlState}");
                    sb.AppendLine($"Message: {ex.MessageText}");
                    
                    if (!string.IsNullOrWhiteSpace(ex.Where))
                        sb.AppendLine($"Where: {ex.Where}");
                    
                    if (!string.IsNullOrWhiteSpace(ex.Detail))
                        sb.AppendLine($"Detail: {ex.Detail}");
                    
                    if (!string.IsNullOrWhiteSpace(ex.Hint))
                        sb.AppendLine($"Hint: {ex.Hint}");
                    
                    if (!string.IsNullOrWhiteSpace(ex.InternalQuery))
                        sb.AppendLine($"InternalQuery: {ex.InternalQuery}");
                    
                    sb.AppendLine($"Position: {ex.Position}");
                    sb.AppendLine();
                    sb.AppendLine("SQL near position:");
                    sb.AppendLine(snippet);

                    throw new NgbConfigurationViolationException(
                        sb.ToString(),
                        context: new Dictionary<string, object?>
                        {
                            ["ddlName"] = ddl.Name,
                            ["sqlState"] = ex.SqlState,
                            ["position"] = ex.Position,
                            ["where"] = ex.Where
                        },
                        innerException: ex);
                }
            }
        }
        finally
        {
            if (acquiredLock)
            {
                // Best-effort unlock.
                try
                {
                    await connection.ExecuteAsync(new CommandDefinition(
                        "SELECT pg_advisory_unlock(@key);",
                        parameters: new { key = SchemaMigrationAdvisoryLockKey },
                        cancellationToken: ct));
                }
                catch
                {
                    // ignore
                }
            }
        }
    }

    private static async Task ApplySessionOptionsAsync(
        NpgsqlConnection connection,
        MigrationExecutionOptions? options,
        CancellationToken ct)
    {
        if (options is null)
            return;

        // Fail fast on unexpected locks and runaway DDL.
        // Use milliseconds to avoid locale issues.
        if (options.LockTimeout is not null)
        {
            var ms = (long)Math.Max(0, options.LockTimeout.Value.TotalMilliseconds);
            await connection.ExecuteAsync(new CommandDefinition($"SET lock_timeout = '{ms}ms';", cancellationToken: ct));
        }

        if (options.StatementTimeout is not null)
        {
            var ms = (long)Math.Max(0, options.StatementTimeout.Value.TotalMilliseconds);
            await connection.ExecuteAsync(new CommandDefinition($"SET statement_timeout = '{ms}ms';", cancellationToken: ct));
        }
    }

    private static string BuildSqlSnippet(string sql, int position1Based, int contextChars)
    {
        if (string.IsNullOrEmpty(sql) || position1Based <= 0)
            return "<no SQL snippet available>";

        // PostgreSQL error positions are 1-based character indexes within the submitted statement.
        var idx = Math.Clamp(position1Based - 1, 0, Math.Max(0, sql.Length - 1));

        // Flatten whitespace without changing string length, so the caret aligns with Position.
        var chars = sql.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] is '\r' or '\n' or '\t') chars[i] = ' ';
        }
        
        var flat = new string(chars);

        var start = Math.Max(0, idx - contextChars);
        var end = Math.Min(flat.Length, idx + contextChars);
        var snippet = flat.Substring(start, end - start);
        var caret = new string(' ', idx - start) + '^';

        return snippet + Environment.NewLine + caret;
    }
}

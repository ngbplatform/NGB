using System.Text;

namespace NGB.PostgreSql.Migrations.Internal;

/// <summary>
/// Small SQL helpers for PostgreSQL migrations.
///
/// We keep these helpers minimal and deterministic: they only build idempotent DO $$ blocks
/// for objects that PostgreSQL doesn't support with IF NOT EXISTS (e.g. triggers).
/// </summary>
internal static class PostgresMigrationTriggerSql
{
    /// <summary>
    /// Build an idempotent block that creates a trigger only when it does not exist.
    ///
    /// <paramref name="triggerEvents"/> examples:
    /// - "BEFORE UPDATE OR DELETE"
    /// - "BEFORE INSERT OR UPDATE OR DELETE"
    /// </summary>
    public static string CreateTriggerIfNotExists(
        string triggerName,
        string tableName,
        string triggerEvents,
        string functionName)
    {
        // NOTE: migration code already treats trigger/table/function names as part of the contract.
        // Identifiers are static (not user input). Keep quoting consistent with other migrations.

        var sb = new StringBuilder(capacity: 512);

        sb.AppendLine("DO $$");
        sb.AppendLine("BEGIN");
        sb.AppendLine("    IF NOT EXISTS (");
        sb.AppendLine("        SELECT 1");
        sb.AppendLine("        FROM pg_trigger");
        sb.AppendLine($"        WHERE tgname = '{triggerName}'");
        sb.AppendLine($"          AND tgrelid = '{tableName}'::regclass");
        sb.AppendLine("    ) THEN");
        sb.AppendLine($"        CREATE TRIGGER {triggerName}");
        sb.AppendLine($"            {triggerEvents}");
        sb.AppendLine($"            ON {tableName}");
        sb.AppendLine("            FOR EACH ROW");
        sb.AppendLine($"            EXECUTE FUNCTION {functionName}();");
        sb.AppendLine("    END IF;");
        sb.AppendLine("END$$;");

        return sb.ToString();
    }
}

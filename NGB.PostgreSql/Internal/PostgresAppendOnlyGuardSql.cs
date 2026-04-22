using Dapper;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Internal;

/// <summary>
/// Shared SQL helpers for append-only table guards.
///
/// NOTE: PostgreSQL does not support CREATE TRIGGER IF NOT EXISTS, so we use a DO block.
/// </summary>
internal static class PostgresAppendOnlyGuardSql
{
    public static Task EnsureUpdateDeleteForbiddenTriggerAsync(
        IUnitOfWork uow,
        string table,
        string triggerName,
        CancellationToken ct = default)
    {
        if (uow is null)
            throw new NgbArgumentRequiredException(nameof(uow));

        if (string.IsNullOrWhiteSpace(table))
            throw new NgbArgumentRequiredException(nameof(table));

        if (string.IsNullOrWhiteSpace(triggerName))
            throw new NgbArgumentRequiredException(nameof(triggerName));

        var sql = $"""
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_trigger
        WHERE tgname = '{triggerName}'
          AND tgrelid = '{table}'::regclass
    ) THEN
        EXECUTE format(
            'CREATE TRIGGER %I BEFORE UPDATE OR DELETE ON %I FOR EACH ROW EXECUTE FUNCTION ngb_forbid_mutation_of_append_only_table();',
            '{triggerName}',
            '{table}'
        );
    END IF;
END $$;
""";

        return uow.Connection.ExecuteAsync(new CommandDefinition(sql, transaction: uow.Transaction, cancellationToken: ct));
    }
}

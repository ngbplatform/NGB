using Dapper;
using NGB.Persistence.UnitOfWork;

namespace NGB.PostgreSql.Internal;

internal static class PostgresTableExistence
{
    /// <summary>
    /// Uses <c>to_regclass</c> to check whether a table exists.
    /// <para>
    /// Caller must ensure the connection is open.
    /// </para>
    /// </summary>
    public static async Task<bool> ExistsAsync(IUnitOfWork uow, string tableName, CancellationToken ct)
    {
        const string sql = "SELECT to_regclass(@TableName) IS NOT NULL;";

        var cmd = new CommandDefinition(
            sql,
            new { TableName = tableName },
            transaction: uow.Transaction,
            cancellationToken: ct);

        return await uow.Connection.ExecuteScalarAsync<bool>(cmd);
    }
}

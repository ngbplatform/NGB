using System.Runtime.CompilerServices;
using Dapper;
using NGB.Persistence.UnitOfWork;

namespace NGB.PostgreSql.Reporting;

public static class PostgresReportCursorStream
{
    public static async IAsyncEnumerable<IReadOnlyList<T>> ReadAsync<T>(
        IUnitOfWork uow,
        string sql,
        object? args,
        [EnumeratorCancellation] CancellationToken ct)
    {
        uow.EnsureActiveTransaction();

        var name = "ngb_stream_" + Guid.NewGuid().ToString("N");

        await uow.Connection.ExecuteAsync(new CommandDefinition(
            $"DECLARE {name} NO SCROLL CURSOR FOR {sql}",
            args,
            uow.Transaction,
            commandTimeout: PostgresReportStreamingDefaults.CommandTimeoutSeconds,
            cancellationToken: ct));

        try
        {
            while (true)
            {
                var batch = (await uow.Connection.QueryAsync<T>(new CommandDefinition(
                        $"FETCH FORWARD {PostgresReportStreamingDefaults.FetchBatchSize} FROM {name}",
                        transaction: uow.Transaction,
                        commandTimeout: PostgresReportStreamingDefaults.CommandTimeoutSeconds,
                        cancellationToken: ct)))
                    .AsList();

                if (batch.Count > 0)
                    yield return batch;

                if (batch.Count < PostgresReportStreamingDefaults.FetchBatchSize)
                    break;
            }
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                try
                {
                    await uow.Connection.ExecuteAsync(new CommandDefinition($"CLOSE {name}", transaction: uow.Transaction, cancellationToken: ct));
                }
                catch (System.Data.Common.DbException) { }
            }
        }
    }
}

using System.Security.Cryptography;
using System.Text;
using Dapper;
using Npgsql;

namespace ExternalMcp.Tests;

internal static class DatabaseSnapshot
{
    internal static async Task<IReadOnlyDictionary<string, string>> CaptureAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var tables = await connection.QueryAsync<string>(
            "SELECT tablename FROM pg_tables WHERE schemaname = 'public' ORDER BY tablename");
        var snapshot = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var table in tables)
        {
            // Names come exclusively from this disposable fixture's catalog, and are quoted.
            var quoted = '"' + table.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
            var rows = await connection.QueryAsync<string>($"SELECT row_to_json(t)::text FROM public.{quoted} t");
            var canonical = string.Join('\n', rows.Order(StringComparer.Ordinal));
            snapshot.Add(table, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))));
        }
        return snapshot;
    }
}

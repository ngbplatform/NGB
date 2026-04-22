using Dapper;

namespace NGB.PostgreSql.Dapper;

/// <summary>
/// Centralized place to register Dapper type handlers used by NGB.PostgreSql.
/// Call this once at application startup (composition root) before any Dapper queries.
/// </summary>
public static class DapperTypeHandlers
{
    private static int _registered;

    public static void Register()
    {
        // idempotent and thread-safe: run only once
        if (Interlocked.Exchange(ref _registered, 1) == 1)
            return;

        SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());
    }
}

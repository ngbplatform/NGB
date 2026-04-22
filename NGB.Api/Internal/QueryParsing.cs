using Microsoft.AspNetCore.Http;
using NGB.Contracts.Common;

namespace NGB.Api.Internal;

internal static class QueryParsing
{
    public static PageRequestDto ToPageRequest(IQueryCollection query)
    {
        var offset = TryGetInt(query, "offset") ?? 0;
        var limit = TryGetInt(query, "limit") ?? 50;
        var search = query.TryGetValue("search", out var s) ? s.ToString() : null;

        var filters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in query)
        {
            var key = kv.Key;
            if (string.Equals(key, "offset", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "limit", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "search", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            filters[key] = kv.Value.ToString();
        }

        return new PageRequestDto(offset, limit, search, filters.Count == 0 ? null : filters);
    }

    private static int? TryGetInt(IQueryCollection query, string key)
    {
        if (!query.TryGetValue(key, out var v))
            return null;
        
        if (int.TryParse(v.ToString(), out var i))
            return i;
        
        return null;
    }
}

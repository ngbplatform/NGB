using Microsoft.AspNetCore.Http;
using NGB.Contracts.Common;

namespace NGB.Api.Internal;

internal static class QueryParsing
{
    public static PageRequestDto ToPageRequest(IQueryCollection query)
    {
        var offset = Math.Clamp(TryGetInt(query, "offset") ?? 0, 0, PagingLimits.MaxOffset);
        var requestedLimit = TryGetInt(query, "limit") ?? PagingLimits.DefaultPageSize;
        var limit = requestedLimit <= 0
            ? PagingLimits.DefaultPageSize
            : Math.Min(requestedLimit, PagingLimits.MaxPageSize);
        var search = query.TryGetValue("search", out var s) ? s.ToString() : null;
        var cursor = query.TryGetValue("cursor", out var c) ? c.ToString() : null;
        var includeTotal = TryGetBool(query, "includeTotal") ?? true;

        var filters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in query)
        {
            var key = kv.Key;
            if (string.Equals(key, "offset", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "limit", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "search", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "cursor", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "includeTotal", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            filters[key] = kv.Value.ToString();
        }

        return new PageRequestDto(offset, limit, search, filters.Count == 0 ? null : filters, cursor, includeTotal);
    }

    private static int? TryGetInt(IQueryCollection query, string key)
    {
        if (!query.TryGetValue(key, out var v))
            return null;
        
        if (int.TryParse(v.ToString(), out var i))
            return i;
        
        return null;
    }

    private static bool? TryGetBool(IQueryCollection query, string key)
    {
        if (!query.TryGetValue(key, out var value))
            return null;

        return bool.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }
}

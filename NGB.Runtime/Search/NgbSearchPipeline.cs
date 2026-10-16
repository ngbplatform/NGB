using Microsoft.Extensions.Logging;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Search;

public sealed record NgbSearchGroups<T>(IReadOnlyList<T> Groups, IReadOnlyList<string> UnavailableSources);

/// <summary>
/// Shared bounded search orchestration. Hosts supply presentation and ranking profiles over secured reads.
/// Sources execute sequentially because their scoped readers can share a database connection.
/// </summary>
public static class NgbSearchPipeline
{
    public static async Task<NgbSearchGroups<T>> ExecuteAsync<T>(
        string? query,
        int requestedLimit,
        string? scope,
        int maximumLimit,
        Func<string, int, CancellationToken, Task<T?>> documents,
        Func<string, int, CancellationToken, Task<T?>> catalogs,
        Func<string, int, CancellationToken, Task<T?>> reports,
        ILogger logger,
        CancellationToken ct)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(query))
            return new NgbSearchGroups<T>([], []);

        if (query.Length > 256)
            throw new NgbArgumentOutOfRangeException(nameof(query), query.Length, "Search text can contain up to 256 characters.");

        if (maximumLimit is < 1 or > 50)
            throw new ArgumentOutOfRangeException(nameof(maximumLimit));

        query = query.Trim();
        var limit = Math.Min(requestedLimit <= 0 ? 20 : requestedLimit, maximumLimit);
        var normalizedScope = NormalizeScope(scope);
        var groups = new List<T>(3);
        var unavailable = new List<string>(3);
        await AddAsync("documents", documents);
        await AddAsync("catalogs", catalogs);
        await AddAsync("reports", reports);

        return new NgbSearchGroups<T>(groups, unavailable);

        async Task AddAsync(string code, Func<string, int, CancellationToken, Task<T?>> search)
        {
            if (normalizedScope is not null && normalizedScope != code)
                return;

            ct.ThrowIfCancellationRequested();
            try
            {
                var group = await search(query, limit, ct);
                if (group is not null)
                    groups.Add(group);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                unavailable.Add(code);
                logger.LogWarning("Search source {SourceCode} failed with {ExceptionType}.", code, exception.GetType().Name);
            }
        }
    }

    private static string? NormalizeScope(string? scope)
        => (scope ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "" => null,
            ":" or "document" or "documents" => "documents",
            "@" or "catalog" or "catalogs" => "catalogs",
            "#" or "report" or "reports" => "reports",
            "/" or "page" or "pages" => "pages",
            ">" or "command" or "commands" => "commands",
            var value => value
        };
}

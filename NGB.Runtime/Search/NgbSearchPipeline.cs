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
        NgbSearchScope scope,
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

        if (!Enum.IsDefined(scope))
            throw new ArgumentOutOfRangeException(nameof(scope));

        query = query.Trim();
        var limit = Math.Min(requestedLimit <= 0 ? 20 : requestedLimit, maximumLimit);
        var groups = new List<T>(3);
        var unavailable = new List<string>(3);
        await AddAsync(NgbSearchScope.Documents, "documents", documents);
        await AddAsync(NgbSearchScope.Catalogs, "catalogs", catalogs);
        await AddAsync(NgbSearchScope.Reports, "reports", reports);

        return new NgbSearchGroups<T>(groups, unavailable);

        async Task AddAsync(NgbSearchScope source, string code, Func<string, int, CancellationToken, Task<T?>> search)
        {
            if (scope != NgbSearchScope.All && scope != source)
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
}

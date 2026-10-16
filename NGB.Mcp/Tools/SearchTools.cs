using System.Text.Json;
using NGB.Application.Abstractions.Services;

namespace NGB.Mcp.Tools;

internal sealed class SearchTool(INgbSearchService search, IMcpNgbLinkResolver links)
    : IMcpReadToolHandler<SearchRequest, SearchResult>
{
    public async Task<SearchResult> ExecuteAsync(SearchRequest request, CancellationToken ct)
    {
        var result = await search.SearchAsync(request.Query, request.Limit, request.Kind, request.TypeCode, ct);

        return new SearchResult(result.Items.Select(item => new SearchItem(
            SourceId(item),
            item.Title,
            item.Kind,
            item.TypeCode,
            item.Id,
            links.Resolve(item.Kind, item.TypeCode, item.Id)?.AbsoluteUri)).ToArray(),
            result.Truncated,
            result.UnavailableSources);
    }

    private static string SourceId(NgbSearchHit item)
        => item.Id is null ? $"{item.Kind}:{item.TypeCode}" : $"{item.Kind}:{item.TypeCode}:{item.Id:D}";
}

internal sealed class CompatibilitySearchTool(SearchTool search)
    : IMcpReadToolHandler<CompatibilitySearchRequest, CompatibilitySearchResult>
{
    public async Task<CompatibilitySearchResult> ExecuteAsync(CompatibilitySearchRequest request, CancellationToken ct)
    {
        var result = await search.ExecuteAsync(new SearchRequest(request.Query), ct);
        if (result.UnavailableSources.Count > 0)
            throw new McpReadException("source_unavailable", "A search source is unavailable. Try again or use ngb_search for explicit partial results.");

        return new CompatibilitySearchResult(result.Items
            .Select(item => new CompatibilitySearchItem(item.Id, item.Title, item.Url))
            .ToArray());
    }
}

internal sealed class FetchTool(GetRecordTool records, IAuthorizedReportReadService reports, IMcpNgbLinkResolver links)
    : IMcpReadToolHandler<FetchRequest, FetchResult>
{
    public async Task<FetchResult> ExecuteAsync(FetchRequest request, CancellationToken ct)
    {
        var parts = request.Id.Split(':');
        if (parts is ["report", _])
        {
            var report = await reports.GetDefinitionAsync(parts[1], ct);

            return new FetchResult(
                request.Id,
                report.Name,
                JsonSerializer.Serialize(report, McpJson.Options),
                links.Resolve("report", parts[1], null)?.AbsoluteUri);
        }

        if (parts.Length != 3 || !Guid.TryParseExact(parts[2], "D", out var id)
            || parts[0] is not ("catalog" or "document" or "generalJournalEntry"))
        {
            throw new McpReadException("invalid_input", "Use a canonical source ID returned by search.");
        }

        var kind = parts[0] switch
        {
            "catalog" => McpSourceKind.Catalog,
            "generalJournalEntry" => McpSourceKind.GeneralJournalEntry,
            _ => McpSourceKind.Document
        };

        var record = await records.ExecuteAsync(new RecordRequest(kind, parts[1], id), ct);
        var title = record.Catalog?.Display
            ?? record.Document?.Display
            ?? record.GeneralJournalEntry?.Document.Display
            ?? parts[1];
        
        return new FetchResult(
            request.Id,
            title,
            JsonSerializer.Serialize(record, McpJson.Options),
            links.Resolve(parts[0], parts[1], id)?.AbsoluteUri);
    }
}

internal sealed class NoNgbLinkResolver : IMcpNgbLinkResolver
{
    public Uri? Resolve(string kind, string typeCode, Guid? id) => null;
}

using Microsoft.Extensions.Logging;
using NGB.Accounting.Documents;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Search;

/// <summary>The generic installed-definition profile of the shared secured search pipeline.</summary>
public sealed class NgbSearchService(
    IAuthorizedCatalogReadService catalogs,
    IAuthorizedDocumentReadService documents,
    IAuthorizedReportReadService reports,
    ILogger<NgbSearchService> logger)
    : INgbSearchService
{
    public Task<NgbSearchResult> SearchAsync(string query, int limit, CancellationToken ct)
        => SearchAsync(query, limit, null, null, ct);

    public async Task<NgbSearchResult> SearchAsync(
        string query,
        int limit,
        NgbSearchKind? kind,
        string? typeCode,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > 256 || limit is < 1 or > 50)
            throw new NgbArgumentInvalidException(nameof(query), "Search requires 1–256 characters and a limit between 1 and 50.");

        if (kind is not null && !Enum.IsDefined(kind.Value))
            throw new NgbArgumentInvalidException(nameof(kind), "Use a supported search kind.");

        if (typeCode is not null && (string.IsNullOrWhiteSpace(typeCode) || typeCode.Length > 128))
            throw new NgbArgumentInvalidException(nameof(typeCode), "Use a canonical type code between 1 and 128 characters.");

        var scope = kind switch
        {
            NgbSearchKind.Catalog => "catalogs",
            NgbSearchKind.Document or NgbSearchKind.GeneralJournalEntry => "documents",
            NgbSearchKind.Report => "reports",
            _ => null
        };

        var result = await NgbSearchPipeline.ExecuteAsync(
            query, limit, scope, 50,
            (text, count, token) => SearchDocumentsAsync(text, count, kind, typeCode, token),
            (text, count, token) => SearchCatalogsAsync(text, count, typeCode, token),
            (text, count, token) => SearchReportsAsync(text, count, typeCode, token), logger, ct);

        var hits = result.Groups.SelectMany(group => group.Items).ToArray();

        var ordered = hits.OrderByDescending(item => Score(item.Title, query.Trim()))
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Kind, StringComparer.Ordinal)
            .ThenBy(item => item.TypeCode, StringComparer.Ordinal)
            .ThenBy(item => item.Id)
            .Take(limit)
            .ToArray();

        return new NgbSearchResult(
            ordered,
            result.Groups.Any(group => group.Truncated) || hits.Length > limit || result.UnavailableSources.Count > 0,
            result.UnavailableSources);
    }

    private async Task<SearchGroup?> SearchCatalogsAsync(
        string query,
        int limit,
        string? typeCode,
        CancellationToken ct)
    {
        var metadata = await catalogs.GetAllMetadataAsync(ct);
        var matching = metadata
            .Where(item => typeCode is null || item.CatalogType == typeCode)
            .ToArray();
        var codes = matching
            .OrderBy(item => item.CatalogType, StringComparer.Ordinal)
            .Take(PagingLimits.MaxLookupTypes)
            .Select(item => item.CatalogType)
            .ToArray();

        if (codes.Length == 0)
            return null;

        var perType = Math.Min(limit, 6);
        var records = await catalogs.LookupAcrossTypesAsync(codes, query, perType, true, ct);

        return new SearchGroup(
            records.Select(item => new NgbSearchHit(
                    NgbSourceKinds.Catalog,
                    item.CatalogType,
                    item.Id,
                    item.Display ?? item.Id.ToString()))
                .ToArray(),
            matching.Length > codes.Length
                || records
                    .GroupBy(item => item.CatalogType)
                    .Any(group => group.Count() >= perType));
    }

    private async Task<SearchGroup?> SearchDocumentsAsync(
        string query,
        int limit,
        NgbSearchKind? kind,
        string? typeCode,
        CancellationToken ct)
    {
        var metadata = await documents.GetAllMetadataAsync(ct);
        var matching = metadata.Where(item => (typeCode is null || item.DocumentType == typeCode)
                && MatchesDocumentKind(item.DocumentType, kind))
            .ToArray();

        var codes = matching
            .OrderBy(item => item.DocumentType, StringComparer.Ordinal)
            .Take(PagingLimits.MaxLookupTypes)
            .Select(item => item.DocumentType)
            .ToArray();

        if (codes.Length == 0)
            return null;

        var perType = Math.Min(limit, 6);
        var records = await documents.LookupAcrossTypesAsync(codes, query, perType, false, ct);

        return new SearchGroup(records.Select(item => new NgbSearchHit(
                item.DocumentType == AccountingDocumentTypeCodes.GeneralJournalEntry
                    ? NgbSourceKinds.GeneralJournalEntry
                    : NgbSourceKinds.Document,
                item.DocumentType,
                item.Id,
                item.Number ?? item.Display ?? item.Id.ToString())).ToArray(),
            matching.Length > codes.Length || records.GroupBy(item => item.DocumentType).Any(group => group.Count() >= perType));
    }

    private async Task<SearchGroup?> SearchReportsAsync(string query, int limit, string? typeCode, CancellationToken ct)
    {
        var definitions = await reports.GetAllDefinitionsAsync(ct);
        var hits = definitions.Where(item => typeCode is null || item.ReportCode == typeCode)
            .Where(item => item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.ReportCode.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(item => new NgbSearchHit(NgbSourceKinds.Report, item.ReportCode, null, item.Name))
            .OrderByDescending(item => Score(item.Title, query))
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.TypeCode, StringComparer.Ordinal)
            .Take(limit + 1)
            .ToArray();

        return new SearchGroup(hits.Take(limit).ToArray(), hits.Length > limit);
    }

    private static bool MatchesDocumentKind(string typeCode, NgbSearchKind? kind)
        => kind switch
        {
            NgbSearchKind.Document => typeCode != AccountingDocumentTypeCodes.GeneralJournalEntry,
            NgbSearchKind.GeneralJournalEntry => typeCode == AccountingDocumentTypeCodes.GeneralJournalEntry,
            _ => true
        };

    private static int Score(string title, string query)
        => title.Equals(query, StringComparison.OrdinalIgnoreCase)
            ? 2
            : title.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 1 : 0;

    private sealed record SearchGroup(IReadOnlyList<NgbSearchHit> Items, bool Truncated);
}

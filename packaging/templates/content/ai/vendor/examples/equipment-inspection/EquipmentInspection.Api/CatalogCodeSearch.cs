using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Core.Security;
using NGB.Runtime.Security;

namespace EquipmentInspection.Api;

public sealed record CatalogCodeField(string CatalogType, string Field);

public sealed class CatalogCodeSearch(
    INgbSearchService search,
    IAuthorizedCatalogReadService catalogs,
    INgbAccessChecker access,
    IEnumerable<CatalogCodeField> codeFields)
{
    public async Task<NgbSearchResult> SearchAsync(
        string query, int limit, NgbSearchKind? kind, string? typeCode, CancellationToken ct)
    {
        var result = await search.SearchAsync(query, limit, kind, typeCode, ct);
        if (kind is not (null or NgbSearchKind.Catalog) || result.UnavailableSources.Contains("catalogs"))
            return result;

        var fields = codeFields.Where(field => typeCode is null || field.CatalogType == typeCode).ToArray();
        if (fields.Length == 0)
            return result;

        var permissions = await access.GetSnapshotAsync(ct);
        var exact = new List<NgbSearchHit>();
        var truncated = result.Truncated;
        foreach (var field in fields)
        {
            if (!permissions.Has(NgbResourceKinds.Catalog, field.CatalogType, NgbPermissionActions.View)
                || !permissions.Has(NgbResourceKinds.Catalog, field.CatalogType, NgbPermissionActions.Lookup))
                continue;

            var filters = new Dictionary<string, string>
            {
                [field.Field] = query.Trim(),
                ["deleted"] = "active"
            };
            var page = await catalogs.GetPageAsync(field.CatalogType,
                new PageRequestDto(Limit: limit, Filters: filters, IncludeTotal: false), ct);
            truncated |= page.HasMore;
            exact.AddRange(page.Items.Select(item =>
                new NgbSearchHit(NgbSourceKinds.Catalog, field.CatalogType, item.Id, item.Display ?? item.Id.ToString())));
        }

        var merged = exact.Concat(result.Items).DistinctBy(hit => (hit.Kind, hit.TypeCode, hit.Id)).ToArray();
        return result with { Items = merged.Take(limit).ToArray(), Truncated = truncated || merged.Length > limit };
    }
}

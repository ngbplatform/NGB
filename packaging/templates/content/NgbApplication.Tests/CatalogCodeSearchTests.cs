using Moq;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Services;
using NGB.Core.Security;
using NGB.Runtime.Security;
using NgbApplication.Api;
using Xunit;

namespace NgbApplication.Tests;

public sealed class CatalogCodeSearchTests
{
    private const string CatalogType = "example.asset";
    private readonly Mock<INgbSearchService> _search = new(MockBehavior.Strict);
    private readonly Mock<IAuthorizedCatalogReadService> _catalogs = new(MockBehavior.Strict);
    private readonly Mock<INgbAccessChecker> _access = new(MockBehavior.Strict);

    [Theory]
    [InlineData(NgbSearchKind.Report, null, false)]
    [InlineData(NgbSearchKind.Catalog, "example.other", false)]
    [InlineData(null, null, true)]
    public async Task Other_scopes_and_unavailable_sources_preserve_the_service_response(
        NgbSearchKind? kind, string? type, bool unavailable)
    {
        var result = new NgbSearchResult([], unavailable, unavailable ? ["catalogs"] : []);
        _search.Setup(service => service.SearchAsync("A-12", 2, kind, type, default)).ReturnsAsync(result);

        Assert.Same(result, await CreateSearch().SearchAsync("A-12", 2, kind, type, default));
    }

    [Theory]
    [InlineData("lookup")]
    [InlineData("view")]
    public async Task Both_view_and_lookup_are_required_to_query_by_code(string action)
    {
        var access = new PermissionSnapshot(Guid.NewGuid(), "reader", true, true, false, 1,
            [new NgbPermissionKey(NgbResourceKinds.Catalog, CatalogType, action)]);
        _access.Setup(service => service.GetSnapshotAsync(default)).ReturnsAsync(access);
        _search.Setup(service => service.SearchAsync("A-12", 2, null, null, default))
            .ReturnsAsync(new NgbSearchResult([], false, []));

        var result = await CreateSearch().SearchAsync("A-12", 2, null, null, default);

        Assert.Empty(result.Items);
        Assert.False(result.Truncated);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public async Task Exact_codes_keep_the_display_name_and_share_the_limit_with_other_hits(int limit, bool truncated)
    {
        using var cancellation = new CancellationTokenSource();
        var ct = cancellation.Token;
        var admin = new PermissionSnapshot(null, "administrator", true, true, true, 1, []);
        _access.Setup(service => service.GetSnapshotAsync(ct)).ReturnsAsync(admin);
        var id = Guid.NewGuid();
        var sameRecord = new NgbSearchHit(NgbSourceKinds.Catalog, CatalogType, id, "Bench");
        var otherRecord = new NgbSearchHit(NgbSourceKinds.Catalog, CatalogType, Guid.NewGuid(), "Storage shelf A-12");
        _search.Setup(service => service.SearchAsync(" A-12 ", limit, NgbSearchKind.Catalog, CatalogType, ct))
            .ReturnsAsync(new NgbSearchResult([sameRecord, otherRecord], false, []));
        _catalogs.Setup(service => service.GetPageAsync(CatalogType, It.Is<PageRequestDto>(page =>
                page.Limit == limit && !page.IncludeTotal && page.Filters!["asset_code"] == "A-12"
                && page.Filters["deleted"] == "active"), ct))
            .ReturnsAsync(new PageResponseDto<CatalogItemDto>([new(id, "Bench", new RecordPayload(), false, false)], 0, limit, null));

        var result = await CreateSearch().SearchAsync(" A-12 ", limit, NgbSearchKind.Catalog, CatalogType, ct);

        Assert.Equal(limit, result.Items.Count);
        Assert.Equal(sameRecord, result.Items[0]);
        Assert.Equal(truncated, result.Truncated);
    }

    [Fact]
    public async Task A_partial_code_page_preserves_record_identity_when_the_display_is_missing()
    {
        var admin = new PermissionSnapshot(null, "administrator", true, true, true, 1, []);
        _access.Setup(service => service.GetSnapshotAsync(default)).ReturnsAsync(admin);
        _search.Setup(service => service.SearchAsync("A-12", 1, null, null, default))
            .ReturnsAsync(new NgbSearchResult([], false, []));
        var item = new CatalogItemDto(Guid.NewGuid(), null, new RecordPayload(), false, false);
        _catalogs.Setup(service => service.GetPageAsync(CatalogType, It.IsAny<PageRequestDto>(), default))
            .ReturnsAsync(new PageResponseDto<CatalogItemDto>([item], 0, 1, null, HasMore: true));

        var result = await CreateSearch().SearchAsync("A-12", 1, null, null, default);

        var hit = Assert.Single(result.Items);
        Assert.Equal(item.Id, hit.Id);
        Assert.Equal(item.Id.ToString(), hit.Title);
        Assert.True(result.Truncated);
    }

    private CatalogCodeSearch CreateSearch() =>
        new(_search.Object, _catalogs.Object, _access.Object, [new CatalogCodeField(CatalogType, "asset_code")]);
}
